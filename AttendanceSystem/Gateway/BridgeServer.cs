using System.Data.SQLite;
using System.Globalization;
using System.Text;
using AttendanceSystem.Database;
using AttendanceSystem.Operations;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace AttendanceSystem.Gateway;

/// <summary>
/// Localhost-only HTTP transport for an independently authorized device bridge.
/// Realand firmware has not been verified to support this protocol. Do not expose
/// this HTTP service on a LAN; it is NOT a scanner-facing production endpoint.
/// </summary>
public static class BridgeServer
{
    private const int BodyLimit = AuthenticatedEventGateway.MaxXmlBytes;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static async Task RunAsync()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("ATTENDANCE_BRIDGE_ENABLED"),
                           "true", StringComparison.Ordinal))
            throw new InvalidOperationException("Bridge startup is disabled by default.");

        var deviceId = Environment.GetEnvironmentVariable("ATTENDANCE_BRIDGE_DEVICE_ID");
        var rawSecret = Environment.GetEnvironmentVariable("ATTENDANCE_BRIDGE_SECRET_BASE64");
        var databasePath = Environment.GetEnvironmentVariable("ATTENDANCE_DB_PATH");
        var portText = Environment.GetEnvironmentVariable("ATTENDANCE_BRIDGE_PORT");

        if (string.IsNullOrWhiteSpace(deviceId) ||
            string.IsNullOrWhiteSpace(rawSecret) ||
            string.IsNullOrWhiteSpace(databasePath) ||
            !File.Exists(databasePath))
            throw new InvalidOperationException("Bridge requires an existing approved DB and external credentials.");

        var port = 21812;
        if (portText is not null &&
            (!int.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, out port) ||
             port < 1024 || port > 65535))
            throw new InvalidOperationException("Bridge port must be 1024–65535.");

        byte[] secret;
        try
        {
            secret = Convert.FromBase64String(rawSecret);
        }
        catch (FormatException)
        {
            throw new InvalidOperationException("Invalid bridge key configuration.");
        }
        if (secret.Length < 32)
            throw new InvalidOperationException("Bridge HMAC key must contain at least 32 random bytes.");

        // Never initialize or migrate a DB on network service start.
        using (var db = DatabaseSetup.Open(databasePath))
        {
            if (DatabaseSetup.ReadVersion(db) != DatabaseSetup.CurrentSchemaVersion)
                throw new InvalidOperationException("Bridge database schema requires an approved migration.");
            using var command = db.CreateCommand();
            command.CommandText = "SELECT Enabled FROM Devices WHERE DeviceID=@id;";
            command.Parameters.AddWithValue("@id", deviceId);
            var enabled = command.ExecuteScalar();
            if (enabled is null ||
                Convert.ToInt32(enabled, CultureInfo.InvariantCulture) != 1)
                throw new InvalidOperationException("Bridge identity is not enabled in the database.");
        }

        var gateway = new AuthenticatedEventGateway(
            new AttendanceEventStore(databasePath), deviceId, secret);
        await using var app = CreateApp(gateway, port);
        Console.WriteLine("Authorized bridge listening on loopback only. No Realand hardware support.");
        await app.RunAsync();
    }

    internal static WebApplication CreateApp(AuthenticatedEventGateway gateway, int port)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = Array.Empty<string>() });
        builder.Logging.ClearProviders(); // No private event content in framework request logs.
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Listen(System.Net.IPAddress.Loopback, port);
            options.Limits.MaxRequestBodySize = BodyLimit;
        });

        var app = builder.Build();
        app.MapGet("/health/live", () => Results.Ok(new { alive = true }));

        app.MapPost("/v1/events", async (HttpContext context) =>
        {
            // Exact payload signed by the authorized localhost bridge.
            // Never accept device identity from the event XML itself.
            var mediaType = context.Request.ContentType?.Split(';', 2)[0].Trim();
            if (!string.Equals(mediaType, "application/xml", StringComparison.OrdinalIgnoreCase) ||
                (context.Request.ContentType!.Contains("charset=", StringComparison.OrdinalIgnoreCase) &&
                 !context.Request.ContentType.Contains("charset=utf-8", StringComparison.OrdinalIgnoreCase)))
                return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);

            if (!long.TryParse(context.Request.Headers["X-Attendance-Timestamp"].ToString(),
                               NumberStyles.None, CultureInfo.InvariantCulture, out var timestamp))
                return Results.StatusCode(StatusCodes.Status401Unauthorized);

            byte[] bytes;
            try
            {
                using var limited = new MemoryStream();
                var chunk = new byte[2048];
                int count;
                while ((count = await context.Request.Body.ReadAsync(
                            chunk.AsMemory(0, chunk.Length), context.RequestAborted)) > 0)
                {
                    if (limited.Length + count > BodyLimit)
                        return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
                    limited.Write(chunk, 0, count);
                }
                bytes = limited.ToArray();
            }
            catch (BadHttpRequestException)
            {
                return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
            }

            string xml;
            try
            {
                xml = StrictUtf8.GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                return Results.StatusCode(StatusCodes.Status422UnprocessableEntity);
            }

            try
            {
                var outcome = gateway.Accept(
                    context.Request.Headers["X-Attendance-Device"].ToString(),
                    timestamp,
                    context.Request.Headers["X-Attendance-Signature"].ToString(),
                    xml);
                return outcome == GatewayOutcome.Inserted
                    ? Results.Json(new { status = "inserted" }, statusCode: 201)
                    : Results.Json(new { status = "duplicate" }, statusCode: 200);
            }
            catch (GatewayRejection error)
            {
                OperationalMetrics.RecordRejected(error.StatusCode);
                return Results.StatusCode(error.StatusCode);
            }
            catch (SQLiteException)
            {
                // SQLite errors cannot yield successful ACKs. No private SQL
                // details are returned in the response or metric labels.
                OperationalMetrics.RecordDatabaseError();
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            }
        });

        return app;
    }
}
