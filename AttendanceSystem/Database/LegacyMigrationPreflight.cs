using System.Data.SQLite;
using System.Globalization;

namespace AttendanceSystem.Database;

/// <summary>
/// Strict structural preflight for an existing legacy (schema 0 or 1) SQLite
/// database. Does not return, export, hash or log individual student records.
/// Must NEVER be confused with authorizing production use of a student DB.
/// </summary>
internal static class LegacyMigrationPreflight
{
    private static readonly IReadOnlyDictionary<string, ColumnSpec[]> ExpectedTables =
        new Dictionary<string, ColumnSpec[]>(StringComparer.Ordinal)
        {
            ["Students"] =
            [
                new("StudentID", "TEXT", 1),
                new("Name", "TEXT", 0),
                new("PhoneNumber", "TEXT", 0)
            ],
            ["Attendance"] =
            [
                new("StudentID", "TEXT", 0),
                new("Date", "TEXT", 0),
                new("Time", "TEXT", 0),
                new("FirstLessonStatus", "TEXT", 0),
                new("SecondLessonStatus", "TEXT", 0)
            ],
            ["Holidays"] = [new("Date", "TEXT", 1)],
            ["SchemaMigrations"] =
            [
                new("Version", "INTEGER", 1),
                new("AppliedAtUtc", "TEXT", 0)
            ]
        };

    /// <summary>
    /// Operates on an already-open connection. Caller chooses and authorizes the
    /// database; this method itself is NOT a replacement for privacy approval,
    /// safe key custody, offsite backups, or a real source-data review.
    /// </summary>
    internal static LegacyPreflightReport Inspect(SQLiteConnection db)
    {
        ArgumentNullException.ThrowIfNull(db);
        var reasons = new SortedSet<string>(StringComparer.Ordinal);
        int version;
        try
        {
            version = DatabaseSetup.ReadVersion(db);
            if (version is < 0 or > 1)
                reasons.Add("unsupported_legacy_schema_version");
        }
        catch (SQLiteException)
        {
            version = -1;
            reasons.Add("schema_version_unreadable");
        }

        var allowedTables = new HashSet<string>(
            new[] { "Students", "Attendance", "Holidays" },
            StringComparer.Ordinal);
        if (version == 1)
            allowedTables.Add("SchemaMigrations");

        try
        {
            // Unknown tables/views/triggers/custom indexes could modify records
            // or invalidate the controlled additive-migration assumptions.
            using (var schema = db.CreateCommand())
            {
                schema.CommandText = """
                    SELECT type, name FROM sqlite_master
                    WHERE type IN ('table','view','trigger','index')
                      AND name NOT LIKE 'sqlite_%';
                    """;
                using var reader = schema.ExecuteReader();
                var foundTables = new HashSet<string>(StringComparer.Ordinal);
                while (reader.Read())
                {
                    var type = reader.GetString(0);
                    var name = reader.GetString(1);
                    if (type != "table" || !allowedTables.Contains(name))
                        reasons.Add("unexpected_schema_object");
                    else
                        foundTables.Add(name);
                }
                if (!foundTables.SetEquals(allowedTables))
                    reasons.Add("missing_or_unexpected_table");
            }

            foreach (var table in allowedTables)
            {
                var actual = new List<ColumnSpec>();
                using var columns = db.CreateCommand();
                // Identifiers come ONLY from compile-time fixed expected table
                // names. No path, SQL identifier or input from a user is interpolated.
                columns.CommandText = "PRAGMA table_info('" + table + "');";
                using var reader = columns.ExecuteReader();
                while (reader.Read())
                {
                    actual.Add(new ColumnSpec(
                        reader.GetString(1),
                        reader.GetString(2).ToUpperInvariant(),
                        Convert.ToInt32(reader.GetValue(5), CultureInfo.InvariantCulture)));
                }
                var expected = ExpectedTables[table];
                if (!actual.SequenceEqual(expected))
                    reasons.Add("column_layout_mismatch");
            }

            using (var check = db.CreateCommand())
            {
                check.CommandText = "PRAGMA integrity_check;";
                using var rows = check.ExecuteReader();
                // Never include the details of damaged SQLite pages in reports.
                if (!rows.Read() ||
                    !string.Equals(rows.GetString(0), "ok", StringComparison.OrdinalIgnoreCase) ||
                    rows.Read())
                    reasons.Add("sqlite_integrity_failed");
            }
            using (var fk = db.CreateCommand())
            {
                fk.CommandText = "PRAGMA foreign_key_check;";
                using var rows = fk.ExecuteReader();
                if (rows.Read()) reasons.Add("foreign_key_integrity_failed");
            }
            using (var applicationId = db.CreateCommand())
            {
                applicationId.CommandText = "PRAGMA application_id;";
                var value = Convert.ToInt64(
                    applicationId.ExecuteScalar(), CultureInfo.InvariantCulture);
                if (value != 0)
                    reasons.Add("unexpected_application_id");
            }
            using (var userVersion = db.CreateCommand())
            {
                userVersion.CommandText = "PRAGMA user_version;";
                var value = Convert.ToInt64(
                    userVersion.ExecuteScalar(), CultureInfo.InvariantCulture);
                if (value != 0)
                    reasons.Add("unexpected_pragma_user_version");
            }

            if (version == 1 && !reasons.Contains("missing_or_unexpected_table") &&
                !reasons.Contains("column_layout_mismatch"))
            {
                using var migration = db.CreateCommand();
                migration.CommandText = """
                    SELECT COUNT(*) FROM SchemaMigrations
                    WHERE Version <> 1;
                    """;
                if (Convert.ToInt64(migration.ExecuteScalar(),
                                    CultureInfo.InvariantCulture) != 0)
                    reasons.Add("unknown_migration_history");
            }

            // Aggregate reference-consistency check, not a record export.
            // Only run if the expected columns/tables exist. Never expose IDs.
            if (!reasons.Contains("column_layout_mismatch") &&
                !reasons.Contains("missing_or_unexpected_table") &&
                !reasons.Contains("unexpected_schema_object"))
            {
                using var orphan = db.CreateCommand();
                orphan.CommandText = """
                    SELECT COUNT(*) FROM Attendance AS a
                    WHERE a.StudentID IS NULL
                       OR NOT EXISTS (
                           SELECT 1 FROM Students AS s
                           WHERE s.StudentID=a.StudentID
                       );
                    """;
                if (Convert.ToInt64(orphan.ExecuteScalar(),
                                    CultureInfo.InvariantCulture) != 0)
                    reasons.Add("attendance_reference_unmatched");
            }
        }
        catch (SQLiteException)
        {
            reasons.Add("schema_inspection_failed");
        }

        return new LegacyPreflightReport(
            version, reasons.Count == 0, reasons.ToArray(),
            // Structural compatibility is distinct from release authorization.
            MigrationAuthorized: false,
            ProductionDataApproved: false);
    }

    private sealed record ColumnSpec(string Name, string Type, int PrimaryKey);
}

internal sealed record LegacyPreflightReport(
    int SourceSchemaVersion,
    bool StructurallyCompatible,
    IReadOnlyList<string> Blockers,
    bool MigrationAuthorized,
    bool ProductionDataApproved);
