using System.Data.SQLite;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using AttendanceSystem.Database;

namespace AttendanceSystem.Gateway;

/// <summary>
/// Actual loopback HTTP round trip using an ephemeral port and synthetic SQLite DB.
/// Does not contact Realand equipment, third-party services or production records.
/// </summary>
public static class BridgeHttpSmokeChecks
{
    public static async Task RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(),
            "attendance-http-smoke-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "http-synthetic.db");
            DatabaseSetup.InitializeDatabaseAtPath(path);
            using (var db = DatabaseSetup.Open(path))
            using (var command = db.CreateCommand())
            {
                command.CommandText = """
                    INSERT INTO Devices(DeviceID, Enabled, RegisteredAtUtc)
                    VALUES ('http-test-device', 1, '2026-11-09T00:00:00Z');
                    """;
                command.ExecuteNonQuery();
            }

            var secret = RandomNumberGenerator.GetBytes(32);
            var gateway = new AuthenticatedEventGateway(
                new AttendanceEventStore(path), "http-test-device", secret);
            await using var app = BridgeServer.CreateApp(gateway, 0);
            await app.StartAsync();
            try
            {
                var url = app.Urls.FirstOrDefault(uri =>
                    uri.StartsWith("http://127.0.0.1:", StringComparison.OrdinalIgnoreCase));
                if (url is null)
                    throw new InvalidOperationException("HTTP test server did not bind to 127.0.0.1.");

                using var client = new HttpClient { BaseAddress = new Uri(url) };
                var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var occurred = DateTimeOffset.UtcNow.AddMinutes(-1)
                    .ToString("O", CultureInfo.InvariantCulture);
                var xml =
                    "<TimeLog><EventKey>synthetic-http-event</EventKey>" +
                    "<StudentID>synthetic-student</StudentID>" +
                    "<OccurredAtUtc>" + occurred + "</OccurredAtUtc></TimeLog>";

                async Task<HttpStatusCode> SubmitAsync(
                    string body, byte[] signingSecret, string mediaType = "application/xml")
                {
                    using var req = new HttpRequestMessage(HttpMethod.Post, "/v1/events");
                    req.Content = new StringContent(body, new UTF8Encoding(false), mediaType);
                    req.Headers.TryAddWithoutValidation(
                        "X-Attendance-Device", "http-test-device");
                    req.Headers.TryAddWithoutValidation(
                        "X-Attendance-Timestamp", timestamp.ToString(CultureInfo.InvariantCulture));
                    req.Headers.TryAddWithoutValidation(
                        "X-Attendance-Signature",
                        AuthenticatedEventGateway.Sign(
                            "http-test-device", timestamp, body, signingSecret));

                    using var reply = await client.SendAsync(req);
                    return reply.StatusCode;
                }

                Check(await SubmitAsync(xml, secret) == HttpStatusCode.Created,
                    "signed HTTP insert acknowledged after commit");
                Check(await SubmitAsync(xml, secret) == HttpStatusCode.OK,
                    "duplicate replay acknowledged without new record");
                Check(await SubmitAsync(xml, RandomNumberGenerator.GetBytes(32))
                      == HttpStatusCode.Unauthorized, "invalid HTTP signature rejected");
                Check(await SubmitAsync("<TimeLog>", secret)
                      == HttpStatusCode.UnprocessableEntity, "malformed HTTP XML rejected");
                Check(await SubmitAsync(xml, secret, "text/plain")
                      == HttpStatusCode.UnsupportedMediaType, "incorrect content type rejected");

                using var db = DatabaseSetup.Open(path);
                using var count = db.CreateCommand();
                count.CommandText = "SELECT COUNT(*) FROM AttendanceEvents;";
                Check(Convert.ToInt32(count.ExecuteScalar(), CultureInfo.InvariantCulture) == 1,
                    "only one acknowledged synthetic HTTP event was persisted");

                Console.WriteLine("HTTP loopback smoke passed: 201/200/401/422/415 and one durable event.");
            }
            finally
            {
                await app.StopAsync();
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void Check(bool success, string scenario)
    {
        if (!success)
            throw new InvalidOperationException("Bridge HTTP smoke failed: " + scenario);
    }
}
