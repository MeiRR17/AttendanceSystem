using System.Data.SQLite;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AttendanceSystem.Database;

/// <summary>
/// Reads disposable databases produced by an isolated SQLite.Core 1.0.119
/// process, verifies the fixture receipt and checks v2 read + explicit schema
/// migration without rewriting historical records. Never accepts arbitrary
/// user database paths or runs from normal application startup.
/// </summary>
internal static class LegacyProviderCompatibilityChecks
{
    internal static void Run()
    {
        if (Environment.GetEnvironmentVariable("ATTENDANCE_SYNTHETIC_FIXTURES") != "true")
            throw new InvalidOperationException("Legacy test requires explicit synthetic-only flag.");

        var supplied = Environment.GetEnvironmentVariable("ATTENDANCE_LEGACY_FIXTURE_DIR");
        if (string.IsNullOrWhiteSpace(supplied))
            throw new InvalidOperationException("Legacy fixture directory was not supplied.");

        var directory = Path.GetFullPath(supplied);
        var temp = Path.GetFullPath(Path.GetTempPath())
            .TrimEnd(Path.DirectorySeparatorChar);
        if (!directory.StartsWith(temp + Path.DirectorySeparatorChar,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase :
                    StringComparison.Ordinal) ||
            !Path.GetFileName(directory).StartsWith("attendance-legacy-fixture-",
                                                    StringComparison.Ordinal))
            throw new InvalidOperationException("Refusing non-temporary legacy fixture path.");
        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException("Synthetic fixture directory missing.");

        using var document = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(directory, "fixture-manifest.json")));
        var root = document.RootElement;
        Check(root.GetProperty("format").GetString() ==
              "attendance-legacy-provider-fixtures-v1" &&
              root.GetProperty("syntheticOnly").GetBoolean() &&
              root.GetProperty("package").GetString() ==
              "System.Data.SQLite.Core/1.0.119" &&
              root.GetProperty("providerAssembly").GetString() == "System.Data.SQLite" &&
              root.GetProperty("providerAssemblyVersion").GetString()?.StartsWith(
                  "1.", StringComparison.Ordinal) == true,
              "fixture must originate from 1.x test provider");

        var entries = root.GetProperty("files").EnumerateArray().ToArray();
        Check(entries.Length == 2, "expected exactly two separate old-schema fixtures");
        var receipts = new List<MigrationRehearsalReceipt>();
        foreach (var entry in entries)
        {
            var name = entry.GetProperty("file").GetString();
            var schema = entry.GetProperty("schemaVersion").GetInt32();
            Check((schema == 0 && name == "legacy-unversioned.db") ||
                  (schema == 1 && name == "legacy-schema-v1.db"),
                  "only known legacy fixture filenames allowed");
            var path = Path.Combine(directory, name!);
            Check(File.Exists(path), "source file must exist");
            var sha = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
            Check(sha == entry.GetProperty("sha256").GetString(),
                  "legacy file must match 1.x generator receipt BEFORE opening");

            // A recovery rehearsal must use a separate clone; it must never
            // modify the original 1.x fixture or authorize an actual upgrade.
            using var testReviewers = new SyntheticMigrationApprovalTestSigners();
            SyntheticMigrationApprovalSmokeChecks.Run(sha, schema, testReviewers, path);
            var review = testReviewers.Sign(
                testReviewers.NewRequest(sha, schema, DateTimeOffset.UtcNow));
            var oneTimeRehearsalLedger =
                new SyntheticDurableReviewNonceLedger(path);

            var rejectedReceipt = false;
            try
            {
                SyntheticMigrationRehearsal.Run(
                    path, new string('0', 64), review,
                    testReviewers.TrustPolicy, oneTimeRehearsalLedger);
            }
            catch (InvalidOperationException) { rejectedReceipt = true; }
            Check(rejectedReceipt, "wrong fixture digest rejects before any clone");
            Check(Directory.GetFiles(directory, "rehearsal-*.db").Length == 0,
                  "invalid fixture receipt created no clone");

            var recovery = SyntheticMigrationRehearsal.Run(
                path, sha, review, testReviewers.TrustPolicy,
                oneTimeRehearsalLedger);
            receipts.Add(recovery);

            var replayDenied = false;
            try
            {
                SyntheticMigrationRehearsal.Run(
                    path, sha, review, testReviewers.TrustPolicy,
                    new SyntheticDurableReviewNonceLedger(path));
            }
            catch (InvalidOperationException) { replayDenied = true; }
            Check(replayDenied,
                  "signed approval replay blocked after persistent ledger reopen");
            Check(recovery.Format == "attendance-synthetic-migration-rehearsal-v1" &&
                  recovery.OriginalSchema == schema &&
                  recovery.TargetSchema == DatabaseSetup.CurrentSchemaVersion &&
                  recovery.StudentCount == 4 &&
                  recovery.AttendanceCount == 3 &&
                  recovery.HolidayCount == 2 &&
                  recovery.CloneOnly &&
                  recovery.OriginalUnchanged &&
                  recovery.PreMigrationBackupVerified &&
                  recovery.RollbackToOriginalSchemaVerified &&
                  recovery.SyntheticDualReviewVerified &&
                  recovery.DurableReviewLedgerUsed &&
                  !recovery.RealDataUsed &&
                  !recovery.ProductionMigrationAuthorized &&
                  recovery.Decision == "NO-GO",
                  "clone-only migration, backup and rollback are verified but NO-GO");
            Check(File.Exists(Path.Combine(directory, "synthetic-review-nonces.db")),
                  "durable synthetic replay ledger persisted during test");
            Check(Directory.GetFiles(directory, "rehearsal-*").Length == 0,
                  "rehearsal cleans up its disposable plaintext artifacts");
            using (var originalAfterRehearsal = DatabaseSetup.Open(path))
                Check(DatabaseSetup.ReadVersion(originalAfterRehearsal) == schema,
                      "source 1.x fixture remains unupgraded after rehearsal");

            LegacyRows original;
            using (var db = DatabaseSetup.Open(path))
            {
                Check(DatabaseSetup.ReadVersion(db) == schema,
                      "old schema version as claimed by generator");
                original = Snapshot(db);
                var preflight = LegacyMigrationPreflight.Inspect(db);
                Check(preflight.StructurallyCompatible &&
                      preflight.Blockers.Count == 0 &&
                      !preflight.MigrationAuthorized &&
                      !preflight.ProductionDataApproved,
                      "supported old schema passes structural check but is NOT approved");
            }

            // Negative controls run only on SQLite-online-backup CLONES,
            // never on the originals generated by the old 1.x process.
            VerifyUnsafeSchemaVariants(directory, path, name!, schema);

            Check(original.Students == entry.GetProperty("expectedStudents").GetInt32() &&
                  original.Attendance == entry.GetProperty("expectedAttendance").GetInt32() &&
                  original.Holidays == entry.GetProperty("expectedHolidays").GetInt32() &&
                  original.Students == 4 && original.Attendance == 3 &&
                  original.Holidays == 2,
                  "every fictional source row can be read through native v2 provider");

            // Default must fail closed even though the fixture is synthetic.
            var refused = false;
            try { DatabaseSetup.InitializeDatabaseAtPath(path); }
            catch (InvalidOperationException) { refused = true; }
            Check(refused, "existing unapproved legacy schema may not auto-upgrade");
            using (var db = DatabaseSetup.Open(path))
                Check(Snapshot(db) == original, "rejected upgrade preserved every legacy field");

            // Public boolean cannot authorize any legacy upgrade, even when
            // a caller sets it true. Only fixture-scoped internal API proceeds.
            var bypassDenied = false;
            try { DatabaseSetup.InitializeDatabaseAtPath(path, allowExistingUpgrade: true); }
            catch (InvalidOperationException) { bypassDenied = true; }
            Check(bypassDenied, "public upgrade boolean must be denied");
            Check(Directory.GetFiles(directory,
                      name + ".pre-migration-*.bak").Length == 0,
                  "public bypass created no backup");

            DatabaseSetup.InitializeSyntheticFixtureAtPath(path);
            using (var db = DatabaseSetup.Open(path))
            {
                Check(DatabaseSetup.ReadVersion(db) == DatabaseSetup.CurrentSchemaVersion,
                      "old 1.x file became current v4 schema via v2 provider");
                Check(Snapshot(db) == original,
                      "legacy rows, text, NULLs and Hebrew preserved by migration");
                using var cmd = db.CreateCommand();
                cmd.CommandText = "PRAGMA foreign_key_check;";
                using var rows = cmd.ExecuteReader();
                Check(!rows.Read(), "no migration foreign-key violations");
            }

            var backups = Directory.GetFiles(directory,
                name + ".pre-migration-*.bak");
            Check(backups.Length == 1, "one pre-migration online backup created");
            using (var db = DatabaseSetup.Open(backups[0]))
            {
                Check(DatabaseSetup.ReadVersion(db) == schema,
                      "backup retains legacy schema, not rewritten schema");
                Check(Snapshot(db) == original,
                      "SQLite online backup has all pre-migration fields intact");
            }

            // Verify that an upgraded DB can accept *new* event model records
            // while preserving all legacy tables unchanged.
            using (var db = DatabaseSetup.Open(path))
            using (var cmd = db.CreateCommand())
            {
                cmd.CommandText = """
                    INSERT INTO Devices(DeviceID, Enabled, RegisteredAtUtc)
                    VALUES('synthetic-rc3-device', 1, '2026-11-09T00:00:00Z');
                    """;
                cmd.ExecuteNonQuery();
            }
            var store = new AttendanceEventStore(path);
            var incoming = new AttendanceEventInput(
                "synthetic-rc3-device", "synthetic-rc3-event",
                "SYNTHETIC-RC3-01",
                new DateTimeOffset(2026, 11, 9, 8, 4, 0, TimeSpan.Zero));
            Check(store.Save(incoming) == AttendanceWriteResult.Inserted &&
                  store.Save(incoming) == AttendanceWriteResult.Duplicate,
                  "v2 upgraded DB inserts once and deduplicates replays");

            DatabaseSetup.InitializeDatabaseAtPath(path);
            using (var db = DatabaseSetup.Open(path))
            {
                Check(Snapshot(db) == original,
                      "original three legacy tables remain byte-equivalent by values");
                using var cmd = db.CreateCommand();
                cmd.CommandText =
                    "SELECT COUNT(*) FROM AttendanceEvents WHERE EventKey='synthetic-rc3-event';";
                Check(Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture) == 1,
                      "reopened v4 file retains one committed new event");
            }
        }

        var evidenceDirectory =
            Environment.GetEnvironmentVariable("ATTENDANCE_MIGRATION_EVIDENCE_DIR");
        if (!string.IsNullOrWhiteSpace(evidenceDirectory))
        {
            SyntheticMigrationEvidence.Write(receipts, evidenceDirectory);
            Console.WriteLine("SYNTHETIC_RESTORE_EVIDENCE=NO-GO");
        }

        Console.WriteLine("SQLite v1.x -> v2 provider compatibility PASS: 2 real v1.x-generated " +
                          "synthetic DBs (schema 0/1), 18 historical rows preserved; " +
                          "clone-only upgrade, verified pre-migration backup, isolated rollback, " +
                          "wrong-receipt denial and cleanup passed; NO real data, NO-GO.");
    }

    private static void VerifyUnsafeSchemaVariants(
        string directory, string sourcePath, string sourceName, int originalVersion)
    {
        RejectUnsafeClone(directory, sourcePath, sourceName, "unknown-column",
            "ALTER TABLE Students ADD COLUMN UnreviewedCustomField TEXT;",
            "column_layout_mismatch", originalVersion);

        RejectUnsafeClone(directory, sourcePath, sourceName, "unexpected-table",
            "CREATE TABLE InternalUnknownData (SecretColumn TEXT);",
            "unexpected_schema_object", originalVersion);

        RejectUnsafeClone(directory, sourcePath, sourceName, "trigger",
            """
            CREATE TRIGGER unreviewed_after_insert AFTER INSERT ON Students
            BEGIN INSERT INTO Holidays(Date) VALUES('2099-12-31'); END;
            """,
            "unexpected_schema_object", originalVersion);

        RejectUnsafeClone(directory, sourcePath, sourceName, "orphan",
            """
            UPDATE Attendance SET StudentID='UNMATCHED-SYNTHETIC-ID'
            WHERE StudentID='SYNTHETIC-RC3-01';
            """,
            "attendance_reference_unmatched", originalVersion);

        RejectUnsafeClone(directory, sourcePath, sourceName, "application-id",
            "PRAGMA application_id = 12031997;",
            "unexpected_application_id", originalVersion);
    }

    private static void RejectUnsafeClone(
        string directory, string originalPath, string originalName,
        string scenario, string mutationSql, string expectedBlocker,
        int originalVersion)
    {
        // BackupDatabase copies the source correctly even if it had a WAL
        // journal. File.Copy(source.db) would not provide that guarantee.
        var clonePath = Path.Combine(directory, originalName + "." + scenario + ".db");
        using (var source = DatabaseSetup.Open(originalPath))
        using (var destination = DatabaseSetup.Open(clonePath, failIfMissing: false))
        {
            source.BackupDatabase(destination, "main", "main", -1, null, 0);
        }

        using (var clone = DatabaseSetup.Open(clonePath))
        using (var mutate = clone.CreateCommand())
        {
            mutate.CommandText = mutationSql;
            mutate.ExecuteNonQuery();
        }

        using (var clone = DatabaseSetup.Open(clonePath))
        {
            var report = LegacyMigrationPreflight.Inspect(clone);
            Check(!report.StructurallyCompatible &&
                  report.Blockers.Contains(expectedBlocker) &&
                  !report.MigrationAuthorized &&
                  !report.ProductionDataApproved,
                  "unexpected synthetic layout rejected: " + scenario);
        }

        // Explicit authorization must NOT bypass structural safety.
        var failedClosed = false;
        try { DatabaseSetup.InitializeSyntheticFixtureAtPath(clonePath); }
        catch (InvalidOperationException error)
        {
            failedClosed = true;
            Check(!error.Message.Contains("SYNTHETIC-RC3-01", StringComparison.Ordinal) &&
                  !error.Message.Contains("FAKE-NO-PHONE", StringComparison.Ordinal),
                  "rejection never includes source record values");
        }
        Check(failedClosed, "explicit approval cannot override bad layout: " + scenario);
        Check(Directory.GetFiles(directory, Path.GetFileName(clonePath) +
                  ".pre-migration-*.bak").Length == 0,
              "blocked layout created no plaintext backup: " + scenario);
        using var verify = DatabaseSetup.Open(clonePath);
        Check(DatabaseSetup.ReadVersion(verify) == originalVersion,
              "rejected structural change did not migrate version: " + scenario);
    }

    private static LegacyRows Snapshot(SQLiteConnection db)
    {
        using (var integrity = db.CreateCommand())
        {
            integrity.CommandText = "PRAGMA integrity_check;";
            Check(string.Equals(integrity.ExecuteScalar()?.ToString(), "ok",
                                StringComparison.OrdinalIgnoreCase),
                  "source and migrated SQLite integrity");
        }

        var canonical = new StringBuilder();
        var counts = new int[3];
        var queries = new[]
        {
            "SELECT StudentID, Name, PhoneNumber FROM Students ORDER BY StudentID;",
            "SELECT StudentID, Date, Time, FirstLessonStatus, SecondLessonStatus " +
                "FROM Attendance ORDER BY StudentID, Date, Time;",
            "SELECT Date FROM Holidays ORDER BY Date;"
        };
        for (var table = 0; table < queries.Length; table++)
        {
            using var command = db.CreateCommand();
            command.CommandText = queries[table];
            using var reader = command.ExecuteReader();
            canonical.Append("TABLE").Append(table).Append('\n');
            while (reader.Read())
            {
                counts[table]++;
                for (var column = 0; column < reader.FieldCount; column++)
                {
                    var text = reader.IsDBNull(column) ? null :
                        Convert.ToString(reader.GetValue(column), CultureInfo.InvariantCulture);
                    var bytes = text is null ? "NULL" :
                        Convert.ToHexString(Encoding.UTF8.GetBytes(text));
                    canonical.Append(bytes.Length).Append(':').Append(bytes).Append('|');
                }
                canonical.Append('\n');
            }
        }

        // The fixed fake marker is mandatory; reject arbitrary DBs even if a
        // crafted JSON manifest somehow points to them.
        using (var marker = db.CreateCommand())
        {
            marker.CommandText = """
                SELECT COUNT(*) FROM Students
                WHERE StudentID LIKE 'SYNTHETIC-RC3-%';
                """;
            Check(Convert.ToInt32(marker.ExecuteScalar(), CultureInfo.InvariantCulture) == 4,
                  "only the known synthetic fixture is supported");
        }
        return new LegacyRows(counts[0], counts[1], counts[2],
            Convert.ToHexString(SHA256.HashData(
                Encoding.UTF8.GetBytes(canonical.ToString()))));
    }

    private static void Check(bool value, string label)
    {
        if (!value)
            throw new InvalidOperationException("SQLite legacy compatibility failed: " + label);
    }

    private sealed record LegacyRows(int Students, int Attendance,
                                     int Holidays, string CanonicalSha256);
}
