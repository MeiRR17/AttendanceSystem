using System.Data.SQLite;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace AttendanceSystem.Database;

/// <summary>
/// Engineering-only recovery rehearsal on old-provider-generated fake SQLite
/// files. Never touches real student data or grants operator authorization.
/// Always creates a separate SQLite-online-backup clone for the migration.
/// </summary>
internal static class SyntheticMigrationRehearsal
{
    private const int MaxFixtureBytes = 4 * 1024 * 1024;

    internal static MigrationRehearsalReceipt Run(
        string sourcePath, string expectedOldProviderFileSha256,
        SyntheticMigrationReviewEnvelope signedSyntheticReview,
        IReadOnlyDictionary<string, byte[]> trustedTestPublicKeys,
        SyntheticReviewNonceLedger usedNonceLedger)
    {
        SyntheticMigrationScope.Demand(sourcePath);
        var sourceName = Path.GetFileName(sourcePath);
        if (sourceName is not ("legacy-unversioned.db" or "legacy-schema-v1.db") ||
            expectedOldProviderFileSha256.Length != 64 ||
            !expectedOldProviderFileSha256.All(Uri.IsHexDigit))
            throw new InvalidOperationException("Unsupported synthetic rehearsal receipt.");

        var file = new FileInfo(sourcePath);
        if (!file.Exists || file.Length < 512 || file.Length > MaxFixtureBytes)
            throw new InvalidOperationException("Synthetic rehearsal source size invalid.");
        // Pin to the actual 1.x producer receipt BEFORE opening SQLite.
        var sourceHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(sourcePath)));
        if (!string.Equals(sourceHash, expectedOldProviderFileSha256,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Synthetic fixture receipt mismatch.");

        var folder = Path.GetDirectoryName(Path.GetFullPath(sourcePath))!;
        var suffix = Guid.NewGuid().ToString("N");
        var clone = Path.Combine(folder, "rehearsal-upgrade-" + suffix + ".db");
        var rollback = Path.Combine(folder, "rehearsal-rollback-" + suffix + ".db");
        var backupPattern = Path.GetFileName(clone) + ".pre-migration-*.bak";
        int oldVersion;
        LegacyRows oldRows;
        try
        {
            // Only the separate clone is ever passed to the upgrade routine.
            using (var db = DatabaseSetup.Open(sourcePath))
            {
                oldVersion = DatabaseSetup.ReadVersion(db);
                if (oldVersion is not (0 or 1))
                    throw new InvalidOperationException(
                        "Rehearsal requires a known pre-v2 source schema.");
                var preflight = LegacyMigrationPreflight.Inspect(db);
                if (!preflight.StructurallyCompatible ||
                    preflight.MigrationAuthorized || preflight.ProductionDataApproved)
                    throw new InvalidOperationException(
                        "Synthetic structural preflight denied rehearsal.");
                oldRows = Fingerprint(db);
                if (oldRows.Students != 4 ||
                    oldRows.Attendance != 3 ||
                    oldRows.Holidays != 2)
                    throw new InvalidOperationException(
                        "Unexpected generated fixture row counts.");
                using var marker = db.CreateCommand();
                marker.CommandText = """
                    SELECT COUNT(*) FROM Students
                    WHERE StudentID LIKE 'SYNTHETIC-RC3-%';
                    """;
                if (Convert.ToInt64(marker.ExecuteScalar(), CultureInfo.InvariantCulture) != 4)
                    throw new InvalidOperationException(
                        "Rehearsal refuses non-generated source identifiers.");

                // Two independent ECDSA P-256 fixture reviewers must sign the
                // exact source hash, schema, nonce, purpose and expiry BEFORE
                // any clone, backup, or database migration is started.
                SyntheticMigrationApprovalGate.VerifyAndConsume(
                    signedSyntheticReview, sourceHash, oldVersion,
                    trustedTestPublicKeys, usedNonceLedger, DateTimeOffset.UtcNow);

                using var output = DatabaseSetup.Open(clone, failIfMissing: false);
                db.BackupDatabase(output, "main", "main", -1, null, 0);
            }

            using (var preUpgrade = DatabaseSetup.Open(clone))
            {
                if (DatabaseSetup.ReadVersion(preUpgrade) != oldVersion ||
                    Fingerprint(preUpgrade) != oldRows)
                    throw new InvalidOperationException(
                        "SQLite online backup changed the synthetic source.");
            }

            // This scoped internal route is NOT callable over the public API.
            // It always makes its OWN verified pre-migration backup.
            DatabaseSetup.InitializeSyntheticFixtureAtPath(clone);

            using (var upgraded = DatabaseSetup.Open(clone))
            {
                if (DatabaseSetup.ReadVersion(upgraded) !=
                        DatabaseSetup.CurrentSchemaVersion ||
                    Fingerprint(upgraded) != oldRows)
                    throw new InvalidOperationException(
                        "Rehearsal migration did not preserve legacy rows.");
                using var fk = upgraded.CreateCommand();
                fk.CommandText = "PRAGMA foreign_key_check;";
                using var rows = fk.ExecuteReader();
                if (rows.Read())
                    throw new InvalidOperationException(
                        "Rehearsal migrated fixture has FK violations.");
            }

            var backups = Directory.GetFiles(folder, backupPattern);
            if (backups.Length != 1)
                throw new InvalidOperationException(
                    "Rehearsal requires exactly one pre-migration backup.");
            using (var backup = DatabaseSetup.Open(backups[0]))
            {
                if (DatabaseSetup.ReadVersion(backup) != oldVersion ||
                    Fingerprint(backup) != oldRows)
                    throw new InvalidOperationException(
                        "Rehearsal pre-migration backup is incomplete.");
                using var restored = DatabaseSetup.Open(
                    rollback, failIfMissing: false);
                backup.BackupDatabase(restored, "main", "main", -1, null, 0);
            }
            using (var restored = DatabaseSetup.Open(rollback))
            {
                if (DatabaseSetup.ReadVersion(restored) != oldVersion ||
                    Fingerprint(restored) != oldRows)
                    throw new InvalidOperationException(
                        "Rehearsal rollback verification failed.");
            }
            using (var original = DatabaseSetup.Open(sourcePath))
            {
                if (DatabaseSetup.ReadVersion(original) != oldVersion ||
                    Fingerprint(original) != oldRows)
                    throw new InvalidOperationException(
                        "Rehearsal changed the original fixture.");
            }

            return new MigrationRehearsalReceipt(
                Format: "attendance-synthetic-migration-rehearsal-v1",
                OriginalSchema: oldVersion,
                TargetSchema: DatabaseSetup.CurrentSchemaVersion,
                StudentCount: oldRows.Students,
                AttendanceCount: oldRows.Attendance,
                HolidayCount: oldRows.Holidays,
                CloneOnly: true,
                OriginalUnchanged: true,
                PreMigrationBackupVerified: true,
                RollbackToOriginalSchemaVerified: true,
                SyntheticDualReviewVerified: true,
                RealDataUsed: false,
                ProductionMigrationAuthorized: false,
                Decision: "NO-GO");
        }
        finally
        {
            // Plaintext fixtures are disposable synthetic samples ONLY.
            // File deletion is not cryptographic erasure; never use real data.
            foreach (var path in new[] { clone, rollback })
                DeleteSqliteSidecars(path);
            foreach (var backup in Directory.GetFiles(folder, backupPattern))
                DeleteSqliteSidecars(backup);
        }
    }

    private static void DeleteSqliteSidecars(string path)
    {
        foreach (var name in new[] { path, path + "-wal", path + "-shm" })
            if (File.Exists(name))
                File.Delete(name);
    }

    private static LegacyRows Fingerprint(SQLiteConnection db)
    {
        using (var check = db.CreateCommand())
        {
            check.CommandText = "PRAGMA integrity_check;";
            if (!string.Equals(
                    Convert.ToString(check.ExecuteScalar(), CultureInfo.InvariantCulture),
                    "ok", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "Synthetic SQLite integrity validation failed.");
        }

        var canonical = new StringBuilder();
        var counts = new int[3];
        var statements = new[]
        {
            "SELECT StudentID, Name, PhoneNumber FROM Students ORDER BY StudentID;",
            "SELECT StudentID, Date, Time, FirstLessonStatus, SecondLessonStatus " +
                "FROM Attendance ORDER BY StudentID, Date, Time;",
            "SELECT Date FROM Holidays ORDER BY Date;"
        };
        for (int table = 0; table < statements.Length; table++)
        {
            using var query = db.CreateCommand();
            query.CommandText = statements[table];
            using var rows = query.ExecuteReader();
            canonical.Append("TABLE:").Append(table).Append('\n');
            while (rows.Read())
            {
                counts[table]++;
                for (int column = 0; column < rows.FieldCount; column++)
                {
                    string? value = rows.IsDBNull(column) ? null :
                        Convert.ToString(rows.GetValue(column), CultureInfo.InvariantCulture);
                    var data = value is null ? "NULL" :
                        Convert.ToHexString(Encoding.UTF8.GetBytes(value));
                    canonical.Append(data.Length).Append(':').Append(data).Append('|');
                }
                canonical.Append('\n');
            }
        }
        // Digest is used strictly inside the test process; never expose hashes
        // of low-entropy real identities in receipts, reports or logs.
        return new LegacyRows(
            counts[0], counts[1], counts[2],
            Convert.ToHexString(SHA256.HashData(
                Encoding.UTF8.GetBytes(canonical.ToString()))));
    }

    private sealed record LegacyRows(
        int Students, int Attendance, int Holidays, string CanonicalDigest);
}

/// <summary>
/// Numeric, non-PII, non-authorizing status only. No file paths, row values,
/// subject keys, manifest secrets, or data-derived hashes escape this record.
/// </summary>
internal sealed record MigrationRehearsalReceipt(
    string Format, int OriginalSchema, int TargetSchema,
    int StudentCount, int AttendanceCount, int HolidayCount,
    bool CloneOnly, bool OriginalUnchanged, bool PreMigrationBackupVerified,
    bool RollbackToOriginalSchemaVerified, bool SyntheticDualReviewVerified,
    bool RealDataUsed,
    bool ProductionMigrationAuthorized, string Decision);
