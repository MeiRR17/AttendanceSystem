using System.Data.SQLite;
using System.Globalization;
using System.Security.Cryptography;
using AttendanceSystem.Database;

namespace AttendanceSystem.Gateway;

/// <summary>
/// 1,000-event simulator + negative controls. Disposable DB and a generated key;
/// no real scanners, biometric data, student names, phone numbers or messages.
/// </summary>
public static class BridgeSmokeChecks
{
    public static void Run()
    {
        var folder = Path.Combine(Path.GetTempPath(),
            "attendance-bridge-smoke-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);

        try
        {
            var databasePath = Path.Combine(folder, "synthetic.db");
            DatabaseSetup.InitializeDatabaseAtPath(databasePath);
            using (var db = DatabaseSetup.Open(databasePath))
            using (var enroll = db.CreateCommand())
            {
                enroll.CommandText = """
                    INSERT INTO Devices (DeviceID, Enabled, RegisteredAtUtc)
                    VALUES ('test-device', 1, '2026-11-09T00:00:00Z');
                    """;
                enroll.ExecuteNonQuery();
            }

            var key = RandomNumberGenerator.GetBytes(32);
            var store = new AttendanceEventStore(databasePath);
            var gateway = new AuthenticatedEventGateway(store, "test-device", key);
            var now = DateTimeOffset.UtcNow;
            var signedAt = now.ToUnixTimeSeconds();
            var occurred = now.AddMinutes(-2).ToString("O", CultureInfo.InvariantCulture);

            static string Xml(string eventId, string studentId, string when) =>
                "<TimeLog><EventKey>" + eventId + "</EventKey><StudentID>" + studentId +
                "</StudentID><OccurredAtUtc>" + when + "</OccurredAtUtc></TimeLog>";

            GatewayOutcome Send(string message, long? timestamp = null, byte[]? signingKey = null,
                                string? device = null)
            {
                var ts = timestamp ?? signedAt;
                var bridgeDevice = device ?? "test-device";
                return gateway.Accept(bridgeDevice, ts,
                    AuthenticatedEventGateway.Sign(bridgeDevice, ts, message, signingKey ?? key), message);
            }

            // Exactly-once logical storage over 1,000 events, then replay all 1,000.
            for (var i = 0; i < 1000; i++)
            {
                var msg = Xml("event-" + i.ToString("D4", CultureInfo.InvariantCulture),
                              "student-" + (i % 40).ToString("D2", CultureInfo.InvariantCulture), occurred);
                Check(Send(msg) == GatewayOutcome.Inserted, "new synthetic event");
            }

            for (var i = 0; i < 1000; i++)
            {
                var msg = Xml("event-" + i.ToString("D4", CultureInfo.InvariantCulture),
                              "student-" + (i % 40).ToString("D2", CultureInfo.InvariantCulture), occurred);
                Check(Send(msg) == GatewayOutcome.Duplicate, "replayed synthetic event");
            }
            Check(Count(databasePath) == 1000, "1,000 events persist exactly once");

            var valid = Xml("standalone", "student-88", occurred);
            ExpectCode(401, () => Send(valid, signingKey: RandomNumberGenerator.GetBytes(32)),
                       "invalid signature");
            ExpectCode(401, () => Send(valid, device: "another-device"),
                       "unrecognized device identity");
            ExpectCode(401, () => Send(valid, timestamp: signedAt - 3600),
                       "stale signed envelope");
            ExpectCode(422, () => Send("<!DOCTYPE TimeLog [<!ENTITY x SYSTEM 'file:///c:/bad'>]>" +
                                     Xml("xxe", "student-99", occurred)), "XXE/DTD");
            ExpectCode(422, () => Send("<TimeLog><EventKey>x</EventKey>"), "truncated XML");
            ExpectCode(422, () => Send(Xml("unexpected", "student-1", occurred)
                                     .Replace("</TimeLog>", "<Name>private</Name></TimeLog>")),
                       "unexpected personal data field");
            ExpectCode(413, () => Send(new string('x', 8193)), "oversized XML");
            ExpectCode(422, () => Send(Xml("future", "student-1",
                now.AddDays(1).ToString("O", CultureInfo.InvariantCulture))), "scanner future clock");

            var first = Xml("unique-key", "student-1", occurred);
            Check(Send(first) == GatewayOutcome.Inserted, "first stable key");
            ExpectCode(409, () => Send(Xml("unique-key", "student-2", occurred)),
                       "conflicting replay key");
            Check(Count(databasePath) == 1001, "no silently accepted conflicting replay");

            using (var db = DatabaseSetup.Open(databasePath))
            using (var disabled = db.CreateCommand())
            {
                disabled.CommandText = "UPDATE Devices SET Enabled=0 WHERE DeviceID='test-device';";
                disabled.ExecuteNonQuery();
            }
            ExpectCode(403, () => Send(Xml("disabled", "student-1", occurred)),
                       "disabled device cannot persist");
            Check(Count(databasePath) == 1001, "disabled device did not write");

            Console.WriteLine(
                "Authenticated bridge smoke checks passed: 1,000 inserts, 1,000 exact replays, " +
                "signed-message and XML negative controls, and device-disable gate.");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static int Count(string path)
    {
        using var db = DatabaseSetup.Open(path);
        using var command = db.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM AttendanceEvents;";
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static void Check(bool condition, string scenario)
    {
        if (!condition)
            throw new InvalidOperationException("Bridge smoke check failure: " + scenario);
    }

    private static void ExpectCode(int code, Action action, string scenario)
    {
        try
        {
            action();
        }
        catch (GatewayRejection exception) when (exception.StatusCode == code)
        {
            return;
        }
        throw new InvalidOperationException("Bridge did not reject " + scenario + " as expected.");
    }
}
