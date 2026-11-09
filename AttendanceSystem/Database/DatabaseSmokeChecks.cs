using System.Data.SQLite;
using System.Globalization;

namespace AttendanceSystem.Database;

/// <summary>
/// Self-contained integration checks using synthetic identifiers and disposable SQLite files.
/// No device, Twilio, Hebcal, existing production DB or network connection is used.
/// </summary>
public static class DatabaseSmokeChecks
{
    public static void Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "attendance-db-smoke-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var clean = Path.Combine(root, "fresh.db");
            DatabaseSetup.InitializeDatabaseAtPath(clean);
            using (var db = DatabaseSetup.Open(clean))
            {
                Check(DatabaseSetup.ReadVersion(db) == DatabaseSetup.CurrentSchemaVersion, "fresh schema version");
                using var cmd = db.CreateCommand();
                cmd.CommandText = """
                    INSERT INTO Devices (DeviceID, Enabled, RegisteredAtUtc)
                    VALUES ('synthetic-device', 1, '2026-11-09T00:00:00Z');
                    """;
                cmd.ExecuteNonQuery();
            }

            var store = new AttendanceEventStore(clean);
            var recordedAt = new DateTimeOffset(2026, 11, 9, 8, 0, 0, TimeSpan.Zero);
            var first = new AttendanceEventInput("synthetic-device", "synthetic-event-1",
                                                  "synthetic-student-1", recordedAt);
            Check(store.Save(first) == AttendanceWriteResult.Inserted, "first event");
            Check(store.Save(first) == AttendanceWriteResult.Duplicate, "duplicate event");
            Check(CountEvents(clean) == 1, "unique event count");

            ExpectInvalidOperation(
                () => store.Save(first with { StudentId = "synthetic-student-2" }),
                "key collision with different student");
            ExpectInvalidOperation(
                () => store.Save(first with { DeviceId = "unknown-device", EventKey = "event-2" }),
                "device not enrolled");
            Check(CountEvents(clean) == 1, "rejected events do not persist");
            DatabaseSetup.InitializeDatabaseAtPath(clean);
            Check(CountEvents(clean) == 1, "migration idempotence");

            var legacy = Path.Combine(root, "legacy.db");
            using (var db = DatabaseSetup.Open(legacy, failIfMissing: false))
            using (var cmd = db.CreateCommand())
            {
                cmd.CommandText = """
                    CREATE TABLE Students(StudentID TEXT PRIMARY KEY, Name TEXT, PhoneNumber TEXT);
                    INSERT INTO Students(StudentID, Name) VALUES ('synthetic-legacy-id', 'SYNTHETIC');
                    CREATE TABLE Attendance(StudentID TEXT, Date TEXT, Time TEXT,
                                            FirstLessonStatus TEXT, SecondLessonStatus TEXT);
                    CREATE TABLE Holidays(Date TEXT PRIMARY KEY);
                    """;
                cmd.ExecuteNonQuery();
            }

            ExpectInvalidOperation(
                () => DatabaseSetup.InitializeDatabaseAtPath(legacy),
                "legacy upgrade requires explicit opt-in");
            DatabaseSetup.InitializeDatabaseAtPath(legacy, allowExistingUpgrade: true);
            using (var db = DatabaseSetup.Open(legacy))
            {
                Check(DatabaseSetup.ReadVersion(db) == DatabaseSetup.CurrentSchemaVersion, "legacy migrated schema");
                using var cmd = db.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM Students WHERE StudentID='synthetic-legacy-id';";
                Check(Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture) == 1,
                      "legacy rows preserved");
            }
            Check(Directory.GetFiles(root, "legacy.db.pre-migration-*.bak").Length == 1,
                  "verified SQLite backup produced");

            Console.WriteLine("SQLite schema and idempotent event smoke checks passed (10 assertions).");
        }
        finally
        {
            // All identifiers above are synthetic. No real student data enters this directory.
            Directory.Delete(root, recursive: true);
        }
    }

    private static int CountEvents(string path)
    {
        using var db = DatabaseSetup.Open(path);
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM AttendanceEvents;";
        return Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static void Check(bool valid, string label)
    {
        if (!valid)
            throw new InvalidOperationException("SQLite smoke check failed: " + label);
    }

    private static void ExpectInvalidOperation(Action action, string label)
    {
        try
        {
            action();
        }
        catch (InvalidOperationException)
        {
            return;
        }
        throw new InvalidOperationException("Expected rejection: " + label);
    }
}
