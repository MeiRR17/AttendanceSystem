using System.Data.SQLite;
using System.Security.Cryptography;
using System.Text.Json;

namespace AttendanceSystem.LegacyFixtureGenerator;

/// <summary>
/// TEST ONLY: writes fully fictional SQLite files using the actual 1.x ADO.NET
/// provider. Not part of AttendanceSystem.sln and cannot access production DBs.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length != 1 ||
            Environment.GetEnvironmentVariable("ATTENDANCE_SYNTHETIC_FIXTURES") != "true")
        {
            Console.Error.WriteLine("Expected output path and ATTENDANCE_SYNTHETIC_FIXTURES=true.");
            return 2;
        }

        var directory = Path.GetFullPath(args[0]);
        var temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);
        if (!directory.StartsWith(temp + Path.DirectorySeparatorChar,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase :
                    StringComparison.Ordinal) ||
            !Path.GetFileName(directory).StartsWith("attendance-legacy-fixture-",
                                                    StringComparison.Ordinal) ||
            !Directory.Exists(directory) || Directory.EnumerateFileSystemEntries(directory).Any())
            throw new InvalidOperationException("Only an empty dedicated temporary fixture folder is allowed.");

        var assembly = typeof(SQLiteConnection).Assembly.GetName();
        if (assembly.Version?.Major != 1)
            throw new InvalidOperationException("Fixture must use SQLite ADO.NET provider 1.x.");

        var fixtures = new List<object>();
        foreach (var (file, schemaVersion) in new[]
        {
            ("legacy-unversioned.db", 0),
            ("legacy-schema-v1.db", 1)
        })
        {
            var path = Path.Combine(directory, file);
            if (File.Exists(path))
                throw new IOException("Never overwrite a test fixture.");
            using (var db = new SQLiteConnection(new SQLiteConnectionStringBuilder
                   {
                       DataSource = path, Version = 3, FailIfMissing = false
                   }.ToString()))
            {
                db.Open();
                using var cmd = db.CreateCommand();
                cmd.CommandText = """
                    PRAGMA journal_mode=WAL;
                    CREATE TABLE Students (
                        StudentID TEXT PRIMARY KEY,
                        Name TEXT,
                        PhoneNumber TEXT
                    );
                    CREATE TABLE Attendance (
                        StudentID TEXT,
                        Date TEXT,
                        Time TEXT,
                        FirstLessonStatus TEXT,
                        SecondLessonStatus TEXT
                    );
                    CREATE TABLE Holidays (Date TEXT PRIMARY KEY);
                    INSERT INTO Students(StudentID,Name,PhoneNumber)
                    VALUES
                      ('SYNTHETIC-RC3-01','בדיקה סינתטית','FAKE-NO-PHONE'),
                      ('SYNTHETIC-RC3-02','Test '' Quote',''),
                      ('SYNTHETIC-RC3-03','Synthetic № 3',NULL),
                      ('SYNTHETIC-RC3-04',NULL,'FAKE-REDACTED');
                    INSERT INTO Attendance(StudentID,Date,Time,FirstLessonStatus,SecondLessonStatus)
                    VALUES
                      ('SYNTHETIC-RC3-01','2026-11-09','08:00','Present','Absent'),
                      ('SYNTHETIC-RC3-02','2026-11-09','09:01','Late','Present'),
                      ('SYNTHETIC-RC3-03','2026-11-10','07:58',NULL,'Present');
                    INSERT INTO Holidays(Date)
                    VALUES('2026-11-13'),('2026-11-14');
                    """;
                cmd.ExecuteNonQuery();
                if (schemaVersion == 1)
                {
                    cmd.CommandText = """
                        CREATE TABLE SchemaMigrations (
                            Version INTEGER PRIMARY KEY,
                            AppliedAtUtc TEXT NOT NULL
                        );
                        INSERT INTO SchemaMigrations(Version,AppliedAtUtc)
                        VALUES(1,'2026-11-09T00:00:00Z');
                        """;
                    cmd.ExecuteNonQuery();
                }
                cmd.CommandText = "PRAGMA integrity_check;";
                if (!string.Equals(cmd.ExecuteScalar()?.ToString(), "ok",
                                   StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Old provider generated invalid SQLite file.");
            }

            // File digest binds the source file to the 1.x tool, without
            // exposing the fixture contents in logs or release artifacts.
            fixtures.Add(new
            {
                file,
                schemaVersion,
                sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))),
                expectedStudents = 4,
                expectedAttendance = 3,
                expectedHolidays = 2
            });
        }

        var manifest = new
        {
            format = "attendance-legacy-provider-fixtures-v1",
            syntheticOnly = true,
            package = "System.Data.SQLite.Core/1.0.119",
            providerAssembly = assembly.Name,
            providerAssemblyVersion = assembly.Version?.ToString(),
            files = fixtures
        };
        File.WriteAllText(Path.Combine(directory, "fixture-manifest.json"),
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("LEGACY_PROVIDER_1X_FIXTURES=PASS; count=2; no real data.");
        return 0;
    }
}
