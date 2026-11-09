using System.Data.SQLite;
using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Text.Json;
using AttendanceSystem.Database;
using AttendanceSystem.AdminUi;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace AttendanceSystem.AdminApi;

/// <summary>
/// OIDC bearer API, localhost only. This is not Windows-integrated SSO:
/// an external trusted HTTPS issuer must sign JWTs for this exact audience,
/// including a controlled 'role' claim. All sensitive routes deny by default.
/// </summary>
public static class AdminApiServer
{
    public static async Task RunAsync()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("ATTENDANCE_ADMIN_API_ENABLED"),
                           "true", StringComparison.Ordinal))
            throw new InvalidOperationException("Admin API is disabled by default.");

        var authority = Environment.GetEnvironmentVariable("ATTENDANCE_API_OIDC_AUTHORITY");
        var audience = Environment.GetEnvironmentVariable("ATTENDANCE_API_OIDC_AUDIENCE");
        var path = Environment.GetEnvironmentVariable("ATTENDANCE_DB_PATH");
        var portText = Environment.GetEnvironmentVariable("ATTENDANCE_API_PORT");
        if (!IsSecureAuthority(authority) ||
            string.IsNullOrWhiteSpace(audience) || audience.Length > 256 ||
            audience.Any(char.IsWhiteSpace) ||
            string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            throw new InvalidOperationException(
                "Admin API requires a HTTPS OIDC authority, audience and approved existing DB.");

        var port = 21813;
        if (portText is not null &&
            (!int.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, out port) ||
             port is < 1024 or > 65535))
            throw new InvalidOperationException("Admin API port must be 1024..65535.");

        // Never create or migrate a student DB when the service starts.
        using (var db = DatabaseSetup.Open(path))
            if (DatabaseSetup.ReadVersion(db) != DatabaseSetup.CurrentSchemaVersion)
                throw new InvalidOperationException("Approved SQLite schema v4 is required.");

        await using var app = CreateApp(path, port, authority!, audience);
        Console.WriteLine("Admin API on localhost only, OIDC required. Not production-ready.");
        await app.RunAsync();
    }

    internal static WebApplication CreateApp(
        string path, int port, string authority, string audience,
        TokenValidationParameters? testParameters = null)
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { Args = Array.Empty<string>() });
        builder.Logging.ClearProviders(); // No request paths, identifiers or PII in logs.
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Listen(IPAddress.Loopback, port);
            options.Limits.MaxRequestBodySize = 4096;
        });

        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;
                options.SaveToken = false;
                options.IncludeErrorDetails = false;
                options.RequireHttpsMetadata = true;
                options.TokenValidationParameters = testParameters ??
                    new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidateAudience = true,
                        ValidateLifetime = true,
                        ValidateIssuerSigningKey = true,
                        RequireSignedTokens = true,
                        RequireExpirationTime = true,
                        ClockSkew = TimeSpan.FromMinutes(1),
                        NameClaimType = "sub",
                        RoleClaimType = "role"
                    };
                if (testParameters is null)
                {
                    options.Authority = authority;
                    options.Audience = audience;
                }
            });
        builder.Services.AddAuthorization(options =>
        {
            options.FallbackPolicy = new AuthorizationPolicyBuilder(
                JwtBearerDefaults.AuthenticationScheme).RequireAuthenticatedUser().Build();
            options.AddPolicy("Viewer", p => p.RequireAuthenticatedUser()
                .RequireRole("Administrator", "Staff", "Viewer"));
            options.AddPolicy("Staff", p => p.RequireAuthenticatedUser()
                .RequireRole("Administrator", "Staff"));
            options.AddPolicy("Administrator", p => p.RequireAuthenticatedUser()
                .RequireRole("Administrator"));
        });

        var store = new AttendanceAdminStore(path);
        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            context.Response.Headers["Cache-Control"] = "no-store";
            // Static UI shell is public but has no records or embedded credentials.
            // All API data endpoints remain protected by server JWT policies.
            if (context.Request.Path.StartsWithSegments("/app"))
                AdminUiAssets.ApplySecurityHeaders(context);
            else
                context.Response.Headers["Content-Security-Policy"] = "default-src 'none'";
            await next();
        });
        app.UseAuthentication();
        app.UseAuthorization();
        AdminUiAssets.MapRoutes(app);
        app.MapGet("/api/v1/health/live", () => Results.Ok(new { alive = true }))
            .AllowAnonymous();

        // Anonymous visitors see no status counts, names or attendance identifiers.
        app.MapGet("/api/v1/overview", () =>
            Results.Ok(new { counts = store.Overview() }))
            .RequireAuthorization("Viewer");

        app.MapGet("/api/v1/me", (ClaimsPrincipal identity) =>
            Results.Ok(new
            {
                roles = identity.Claims
                    .Where(c => c.Type == "role" && c.Value is
                        ("Administrator" or "Staff" or "Viewer"))
                    .Select(c => c.Value).Distinct().Order().ToArray()
            }))
            .RequireAuthorization("Viewer");

        app.MapGet("/api/v1/events", (HttpRequest request) =>
        {
            var afterText = request.Query["afterId"].ToString();
            var limitText = request.Query["limit"].ToString();
            long after = 0;
            var limit = 20;
            if ((afterText.Length > 0 && (!long.TryParse(afterText, NumberStyles.None,
                    CultureInfo.InvariantCulture, out after) || after < 0)) ||
                (limitText.Length > 0 && (!int.TryParse(limitText, NumberStyles.None,
                    CultureInfo.InvariantCulture, out limit) || limit is < 1 or > 100)))
                return Results.BadRequest(new { error = "invalid_pagination" });
            var rows = store.ListEvents(after, limit);
            return Results.Ok(new
            {
                items = rows,
                nextAfterId = rows.Count == limit ? rows[^1].EventId : (long?)null
            });
        }).RequireAuthorization("Staff");

        app.MapGet("/api/v1/events/{eventId:long}", (long eventId) =>
        {
            var result = store.FindEvent(eventId);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }).RequireAuthorization("Staff");

        app.MapPost("/api/v1/events/{eventId:long}/corrections",
            async (HttpContext context, long eventId) =>
        {
            if (!string.Equals(
                    context.Request.ContentType?.Split(';', 2)[0].Trim(),
                    "application/json", StringComparison.OrdinalIgnoreCase))
                return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
            AttendanceCorrectionRequest? request;
            try
            {
                request = await context.Request.ReadFromJsonAsync<AttendanceCorrectionRequest>(
                    cancellationToken: context.RequestAborted);
            }
            catch (JsonException)
            {
                return Results.BadRequest(new { error = "invalid_json" });
            }
            catch (BadHttpRequestException)
            {
                return Results.BadRequest(new { error = "invalid_body" });
            }
            if (request is null || eventId <= 0)
                return Results.BadRequest(new { error = "invalid_correction" });

            var subject = context.User.FindFirstValue("sub");
            var role = context.User.IsInRole("Administrator")
                ? "Administrator" : context.User.IsInRole("Staff") ? "Staff" : "";
            try
            {
                var correction = store.Correct(
                    eventId, request.ExpectedRevision, request.NewStatus,
                    request.ReasonCode, subject ?? "", role);
                return Results.Json(correction, statusCode: StatusCodes.Status201Created);
            }
            catch (UnauthorizedAccessException)
            {
                return Results.Forbid();
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
            catch (AdminConflictException)
            {
                return Results.Conflict(new { error = "revision_or_state_conflict" });
            }
            catch (ArgumentException)
            {
                return Results.UnprocessableEntity(new { error = "invalid_correction" });
            }
            catch (SQLiteException)
            {
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            }
        }).RequireAuthorization("Staff");

        app.MapGet("/api/v1/corrections", (HttpRequest request) =>
        {
            var raw = request.Query["limit"].ToString();
            var limit = 50;
            if (raw.Length > 0 &&
                (!int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture,
                               out limit) || limit is < 1 or > 100))
                return Results.BadRequest(new { error = "invalid_pagination" });
            return Results.Ok(new { items = store.RecentCorrections(limit) });
        }).RequireAuthorization("Administrator");

        app.MapGet("/api/v1/events/{eventId:long}/corrections",
            (long eventId) =>
                eventId <= 0 ? Results.BadRequest() :
                Results.Ok(new { items = store.Corrections(eventId) }))
            .RequireAuthorization("Administrator");

        // Exports are intentionally not available until redaction, retention,
        // subject access, tenant and privacy controls are approved.
        app.MapGet("/api/v1/exports", () =>
            Results.StatusCode(StatusCodes.Status501NotImplemented))
            .RequireAuthorization("Administrator");

        return app;
    }

    private static bool IsSecureAuthority(string? authority) =>
        Uri.TryCreate(authority, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps &&
        string.IsNullOrWhiteSpace(uri.UserInfo) &&
        string.IsNullOrWhiteSpace(uri.Query) &&
        string.IsNullOrWhiteSpace(uri.Fragment);
}
