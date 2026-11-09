using System.Data.SQLite;
using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AttendanceSystem.Database;
using Microsoft.IdentityModel.Tokens;

namespace AttendanceSystem.AdminApi;

/// <summary>
/// Offline authorization/regression suite with synthetic SQLite and locally
/// signed TEST JWTs. No real OIDC provider, human identity or internet calls.
/// </summary>
public static class AdminApiSmokeChecks
{
    public static async Task RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(),
            "attendance-admin-api-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "synthetic.db");
            DatabaseSetup.InitializeDatabaseAtPath(path);
            using (var db = DatabaseSetup.Open(path))
            using (var cmd = db.CreateCommand())
            {
                cmd.CommandText = """
                    INSERT INTO Devices(DeviceID,Enabled,RegisteredAtUtc)
                    VALUES('synthetic-device',1,'2026-11-09T09:00:00Z');
                    INSERT INTO AttendanceEvents
                        (DeviceID,EventKey,StudentID,OccurredAtUtc,ReceivedAtUtc)
                    VALUES
                        ('synthetic-device','synthetic-evt-1','private-test-student-1',
                         '2026-11-09T10:00:00.0000000+00:00','2026-11-09T10:00:00.0000000+00:00'),
                        ('synthetic-device','synthetic-evt-2','private-test-student-2',
                         '2026-11-09T10:01:00.0000000+00:00','2026-11-09T10:01:00.0000000+00:00');
                    INSERT INTO AttendanceDecisions(EventID,RuleVersion,Status,EvaluatedAtUtc)
                    VALUES (1,'policy-v5','Late','2026-11-09T10:05:00Z'),
                           (2,'policy-v5','Late','2026-11-09T10:05:00Z');
                    """;
                cmd.ExecuteNonQuery();
            }

            var store = new AttendanceAdminStore(path);
            Check(store.ListEvents(0,10).Count == 2, "listed synthetic rows");
            Check(store.FindEvent(1)?.Status == "Late" && store.FindEvent(1)?.Revision == 0,
                  "original derived status");
            ExpectUnauthorized(() => store.Correct(
                1,0,"OnTime","clock-skew","fake-viewer","Viewer"),
                "viewer cannot correct directly");
            var first = store.Correct(
                1,0,"OnTime","clock-skew","synthetic-staff","Staff");
            Check(first.Revision == 1 && first.BeforeStatus == "Late" &&
                  first.AfterStatus == "OnTime","revision 1 and before-after preserved");
            ExpectConflict(() => store.Correct(
                1,0,"Excused","approved-excuse","synthetic-staff","Staff"),
                "stale expected revision cannot overwrite");
            Check(store.Corrections(1).Count == 1, "exactly one correction history row");
            Check(store.FindEvent(1)?.Status == "OnTime", "effective status uses manual correction");
            Check(store.Overview().GetValueOrDefault("Late") == 1 &&
                  store.Overview().GetValueOrDefault("OnTime") == 1,
                  "aggregate effective attendance without private fields");
            using (var db = DatabaseSetup.Open(path))
            using (var c = db.CreateCommand())
            {
                c.CommandText = """
                    SELECT COUNT(*) FROM AttendanceEvents
                    WHERE StudentID LIKE 'private-test-student-%';
                    """;
                Check(Convert.ToInt64(c.ExecuteScalar(),CultureInfo.InvariantCulture) == 2,
                      "raw original events never changed");
            }

            var issuer = "https://issuer.invalid.example";
            var audience = "attendance-synthetic-test";
            var key = new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(32));
            var validation = new TokenValidationParameters
            {
                ValidIssuer = issuer,
                ValidateIssuer = true,
                ValidAudience = audience,
                ValidateAudience = true,
                IssuerSigningKey = key,
                ValidateIssuerSigningKey = true,
                ValidateLifetime = true,
                RequireSignedTokens = true,
                RequireExpirationTime = true,
                NameClaimType = "sub",
                RoleClaimType = "role",
                ClockSkew = TimeSpan.Zero
            };

            string Token(string role) => new JwtSecurityTokenHandler().WriteToken(
                new JwtSecurityToken(
                    issuer, audience,
                    [new Claim("sub","synthetic-actor-" + role), new Claim("role",role)],
                    notBefore: DateTime.UtcNow.AddMinutes(-1),
                    expires: DateTime.UtcNow.AddMinutes(10),
                    signingCredentials: new SigningCredentials(
                        key,SecurityAlgorithms.HmacSha256)));

            await using var app = AdminApiServer.CreateApp(path,0,issuer,audience,validation);
            await app.StartAsync();
            try
            {
                var address = app.Urls.FirstOrDefault(x =>
                    x.StartsWith("http://127.0.0.1:",StringComparison.OrdinalIgnoreCase));
                if (address is null)
                    throw new InvalidOperationException("Admin API smoke listener is not loopback.");
                using var client = new HttpClient { BaseAddress=new Uri(address) };
                async Task<HttpResponseMessage> SendAsync(
                    HttpMethod method, string url, string? jwt = null,
                    object? payload = null)
                {
                    var request = new HttpRequestMessage(method,url);
                    if (jwt is not null)
                        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer",jwt);
                    if (payload is not null)
                        request.Content = new StringContent(
                            JsonSerializer.Serialize(payload),Encoding.UTF8,"application/json");
                    using (request) return await client.SendAsync(request);
                }

                var admin = Token("Administrator");
                var staff = Token("Staff");
                var viewer = Token("Viewer");

                using (var res=await SendAsync(HttpMethod.Get,"/app"))
                {
                    Check(res.StatusCode==HttpStatusCode.OK,
                          "public HTML shell contains no attendance data");
                    var body=await res.Content.ReadAsStringAsync();
                    Check(body.Contains("dir=\"rtl\"",StringComparison.Ordinal) &&
                          !body.Contains("private-test-student",StringComparison.Ordinal),
                          "public shell serves only static UI");
                }
                using (var res=await SendAsync(HttpMethod.Get,"/api/v1/health/ready"))
                {
                    Check(res.StatusCode==HttpStatusCode.OK,
                          "coarse readiness succeeds for healthy synthetic DB");
                    var body = await res.Content.ReadAsStringAsync();
                    Check(body.Contains("\"ready\":true",StringComparison.Ordinal) &&
                          !body.Contains("synthetic-device",StringComparison.Ordinal) &&
                          !body.Contains("private-test-student",StringComparison.Ordinal),
                          "anonymous readiness reveals only boolean");
                }
                using (var res=await SendAsync(HttpMethod.Get,"/api/v1/ops/status"))
                    Check(res.StatusCode==HttpStatusCode.Unauthorized,
                          "anonymous operational metrics denied");
                using (var res=await SendAsync(HttpMethod.Get,"/api/v1/overview"))
                    Check(res.StatusCode==HttpStatusCode.Unauthorized,"anonymous denied counts");
                using (var res=await SendAsync(HttpMethod.Get,"/api/v1/overview",viewer))
                    Check(res.StatusCode==HttpStatusCode.OK,"viewer aggregate allowed");
                using (var res=await SendAsync(HttpMethod.Get,"/api/v1/ops/status",viewer))
                    Check(res.StatusCode==HttpStatusCode.Forbidden,
                          "Viewer role denied operational details");
                using (var res=await SendAsync(HttpMethod.Get,"/api/v1/ops/status",staff))
                    Check(res.StatusCode==HttpStatusCode.Forbidden,
                          "Staff role denied operational details");
                using (var res=await SendAsync(HttpMethod.Get,"/api/v1/ops/status",admin))
                {
                    Check(res.StatusCode==HttpStatusCode.OK,
                          "Administrator sees operational aggregates");
                    var body=await res.Content.ReadAsStringAsync();
                    Check(body.Contains("not_verified",StringComparison.Ordinal) &&
                          !body.Contains("private-test-student",StringComparison.Ordinal) &&
                          !body.Contains("synthetic-device",StringComparison.Ordinal),
                          "operational metrics do not expose device or student IDs");
                }
                using (var res=await SendAsync(HttpMethod.Get,"/api/v1/events",viewer))
                    Check(res.StatusCode==HttpStatusCode.Forbidden,"viewer cannot read event list");
                using (var res=await SendAsync(HttpMethod.Get,"/api/v1/exports",viewer))
                    Check(res.StatusCode==HttpStatusCode.Forbidden,"viewer cannot export");
                using (var res=await SendAsync(HttpMethod.Get,"/api/v1/exports",admin))
                    Check(res.StatusCode==HttpStatusCode.NotImplemented,"unapproved export disabled");
                using (var res=await SendAsync(HttpMethod.Get,"/api/v1/events?limit=1000",staff))
                    Check(res.StatusCode==HttpStatusCode.BadRequest,"pagination bounded");
                using (var res=await SendAsync(HttpMethod.Get,"/api/v1/events",staff))
                {
                    Check(res.StatusCode==HttpStatusCode.OK,"staff may list events");
                    var body=await res.Content.ReadAsStringAsync();
                    Check(!body.Contains("private-test-student",StringComparison.Ordinal),
                          "no student identifiers in API projection");
                }
                using (var res=await SendAsync(HttpMethod.Get,
                    "/api/v1/corrections?limit=20",viewer))
                    Check(res.StatusCode==HttpStatusCode.Forbidden,"viewer denied global audit");
                using (var res=await SendAsync(HttpMethod.Get,
                    "/api/v1/corrections?limit=20",staff))
                    Check(res.StatusCode==HttpStatusCode.Forbidden,"staff denied global audit");
                using (var res=await SendAsync(HttpMethod.Get,
                    "/api/v1/corrections?limit=1000",admin))
                    Check(res.StatusCode==HttpStatusCode.BadRequest,
                          "global audit pagination bounded");
                using (var res=await SendAsync(HttpMethod.Get,
                    "/api/v1/corrections?limit=20",admin))
                {
                    Check(res.StatusCode==HttpStatusCode.OK,"admin global audit allowed");
                    var body=await res.Content.ReadAsStringAsync();
                    Check(!body.Contains("private-test-student",StringComparison.Ordinal) &&
                          body.Contains("clock-skew",StringComparison.Ordinal),
                          "global audit has approved reason, not student data");
                }
                using (var res=await SendAsync(HttpMethod.Get,
                    "/api/v1/events/1/corrections",staff))
                    Check(res.StatusCode==HttpStatusCode.Forbidden,"only admin reads actor audit");
                using (var res=await SendAsync(HttpMethod.Get,
                    "/api/v1/events/1/corrections",admin))
                    Check(res.StatusCode==HttpStatusCode.OK,"admin can audit");
                using (var res=await SendAsync(HttpMethod.Post,
                    "/api/v1/events/2/corrections",viewer,
                    new {expectedRevision=0,newStatus="Excused",reasonCode="approved-excuse"}))
                    Check(res.StatusCode==HttpStatusCode.Forbidden,
                          "viewer cannot submit corrections");
                using (var res=await SendAsync(HttpMethod.Post,
                    "/api/v1/events/2/corrections",staff,
                    new {expectedRevision=0,newStatus="Excused",reasonCode="approved-excuse"}))
                    Check(res.StatusCode==HttpStatusCode.Created,
                          "staff correction with optimistic revision succeeds");
                using (var res=await SendAsync(HttpMethod.Post,
                    "/api/v1/events/2/corrections",staff,
                    new {expectedRevision=0,newStatus="OnTime",reasonCode="clock-skew"}))
                    Check(res.StatusCode==HttpStatusCode.Conflict,
                          "stale correction returns conflict");
                using (var res=await SendAsync(HttpMethod.Post,
                    "/api/v1/events/2/corrections",staff,
                    new {expectedRevision=1,newStatus="Invalid",reasonCode="arbitrary-text"}))
                    Check(res.StatusCode==HttpStatusCode.UnprocessableEntity,
                          "free-form unapproved correction reason rejected");
                using (var res=await SendAsync(HttpMethod.Get,
                    "/api/v1/events/2",admin))
                {
                    Check(res.StatusCode==HttpStatusCode.OK,"read corrected status");
                    var body=await res.Content.ReadAsStringAsync();
                    Check(body.Contains("Excused",StringComparison.Ordinal) &&
                          body.Contains("\"revision\":1",StringComparison.Ordinal),
                          "current revision visible for concurrency");
                }
                using (var res=await SendAsync(HttpMethod.Get,
                    "/api/v1/me",viewer))
                {
                    var body=await res.Content.ReadAsStringAsync();
                    Check(res.StatusCode==HttpStatusCode.OK &&
                          body.Contains("Viewer",StringComparison.Ordinal) &&
                          !body.Contains("synthetic-actor",StringComparison.Ordinal),
                          "role visible without actor identity");
                }

                Check(store.Corrections(2).Count==1, "HTTP created exactly one auditable revision");
                using (var db = DatabaseSetup.Open(path))
                using (var cmd = db.CreateCommand())
                {
                    cmd.CommandText = """
                        SELECT COUNT(*) FROM AuditEvents
                        WHERE Action='attendance.correction.created';
                        """;
                    Check(Convert.ToInt32(cmd.ExecuteScalar(),CultureInfo.InvariantCulture)==2,
                          "both corrections audited with no student PII");
                }
            }
            finally
            {
                await app.StopAsync();
            }

            Console.WriteLine("OIDC mock JWT + RBAC API smoke passed: anonymous/Viewer/Staff/Admin, " +
                              "bounded pagination, protected audit/export, optimistic revision, " +
                              "append-only correction and no student PII.");
        }
        finally
        {
            Directory.Delete(root,recursive:true);
        }
    }

    private static void Check(bool ok,string scenario)
    {
        if(!ok)throw new InvalidOperationException("Admin API smoke failed: "+scenario);
    }
    private static void ExpectUnauthorized(Action action,string scenario)
    {
        try{action();}
        catch(UnauthorizedAccessException){return;}
        throw new InvalidOperationException("Failed to reject: "+scenario);
    }
    private static void ExpectConflict(Action action,string scenario)
    {
        try{action();}
        catch(AdminConflictException){return;}
        throw new InvalidOperationException("Failed to conflict: "+scenario);
    }
}
