using System.Data.SQLite;
using System.Globalization;
using AttendanceSystem.Database;

namespace AttendanceSystem.Operations;

/// <summary>
/// Read-only readiness and operational snapshot over an EXISTING SQLite v4 DB.
/// No file creation, migration, student identifiers, network probes or sends.
/// A coarse ready flag may be public; details are for Administrator only.
/// </summary>
public static class OperationalHealth
{
    public const int BacklogWarningThreshold = 25;
    public const int DeadLetterWarningThreshold = 1;

    public static OperationalHealthSnapshot Check(string databasePath)
    {
        var metrics = OperationalMetrics.Snapshot();
        if (string.IsNullOrWhiteSpace(databasePath) || !File.Exists(databasePath))
            return Unavailable("database_missing", metrics);

        try
        {
            using var db = DatabaseSetup.Open(databasePath);
            if (DatabaseSetup.ReadVersion(db) != DatabaseSetup.CurrentSchemaVersion)
                return Unavailable("schema_mismatch", metrics);

            using (var integrity = db.CreateCommand())
            {
                integrity.CommandText = "PRAGMA quick_check(1);";
                if (!string.Equals(
                        Convert.ToString(integrity.ExecuteScalar(), CultureInfo.InvariantCulture),
                        "ok", StringComparison.OrdinalIgnoreCase))
                    return Unavailable("integrity_failure", metrics);
            }

            long pending = 0;
            long uncertain = 0;
            long deadLetter = 0;
            using (var count = db.CreateCommand())
            {
                count.CommandText = """
                    SELECT Status, COUNT(*) FROM NotificationOutbox
                    WHERE Status IN ('Queued','Retry','Uncertain','DeadLetter')
                    GROUP BY Status;
                    """;
                using var rows = count.ExecuteReader();
                while (rows.Read())
                {
                    var status = rows.GetString(0);
                    var value = rows.GetInt64(1);
                    if (status is "Queued" or "Retry") pending += value;
                    else if (status == "Uncertain") uncertain += value;
                    else if (status == "DeadLetter") deadLetter += value;
                }
            }

            var alerts = new List<string>();
            if (pending >= BacklogWarningThreshold)
                alerts.Add("dry_run_outbox_backlog");
            if (uncertain > 0)
                alerts.Add("ambiguous_delivery_requires_manual_review");
            if (deadLetter >= DeadLetterWarningThreshold)
                alerts.Add("dead_letter_requires_manual_review");
            if (metrics.DatabaseWriteErrors > 0)
                alerts.Add("database_write_errors_observed");

            return new OperationalHealthSnapshot(
                Ready: true,
                Code: alerts.Count == 0 ? "ready" : "ready_with_warnings",
                Alerts: alerts.AsReadOnly(),
                PendingDryRunNotifications: pending,
                UncertainDryRunNotifications: uncertain,
                DeadLetterDryRunNotifications: deadLetter,
                DeviceConnectivity: "not_verified",
                Metrics: metrics);
        }
        catch (Exception error) when (
            error is SQLiteException or IOException or UnauthorizedAccessException)
        {
            // Never include filesystem paths, SQL errors or private record values.
            return Unavailable("database_unavailable", metrics);
        }
    }

    private static OperationalHealthSnapshot Unavailable(
        string code, OperationalMetricSnapshot metrics) =>
        new(false, code, new[] { "database_not_ready" }, 0, 0, 0,
            "not_verified", metrics);
}

public sealed record OperationalHealthSnapshot(
    bool Ready, string Code, IReadOnlyList<string> Alerts,
    long PendingDryRunNotifications, long UncertainDryRunNotifications,
    long DeadLetterDryRunNotifications,
    string DeviceConnectivity, OperationalMetricSnapshot Metrics);
