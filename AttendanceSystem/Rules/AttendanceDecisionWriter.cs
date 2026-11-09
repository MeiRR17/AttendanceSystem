using System.Data.SQLite;
using System.Globalization;
using AttendanceSystem.Database;

namespace AttendanceSystem.Rules;

/// <summary>
/// Explicit, offline materialization only. The gateway never automatically
/// assigns a status or sends a notification. Re-evaluation is fully audited.
/// </summary>
public sealed class AttendanceDecisionWriter
{
    private readonly string _databasePath;

    public AttendanceDecisionWriter(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
            throw new ArgumentException("Database path required.", nameof(databasePath));
        _databasePath = Path.GetFullPath(databasePath);
    }

    public DecisionMaterialization Materialize(long eventId, AttendanceRuleEngine engine)
    {
        if (eventId <= 0)
            throw new ArgumentOutOfRangeException(nameof(eventId));
        ArgumentNullException.ThrowIfNull(engine);

        using var connection = DatabaseSetup.Open(_databasePath);
        if (DatabaseSetup.ReadVersion(connection) != DatabaseSetup.CurrentSchemaVersion)
            throw new InvalidOperationException("Unapproved or incompatible DB schema.");

        using var tx = connection.BeginTransaction();
        DateTimeOffset occurredAt;
        using (var query = connection.CreateCommand())
        {
            query.Transaction = tx;
            query.CommandText =
                "SELECT OccurredAtUtc FROM AttendanceEvents WHERE EventID=@event;";
            query.Parameters.AddWithValue("@event", eventId);
            var stored = query.ExecuteScalar() as string;
            if (stored is null ||
                !DateTimeOffset.TryParseExact(stored, "O", CultureInfo.InvariantCulture,
                                               DateTimeStyles.None, out occurredAt) ||
                occurredAt.Offset != TimeSpan.Zero)
                throw new InvalidOperationException("No valid UTC event with this identifier.");
        }

        var decision = engine.EvaluateEvent(occurredAt);
        var status = decision.Status.ToString();

        string? previousStatus = null;
        string? previousVersion = null;
        using (var read = connection.CreateCommand())
        {
            read.Transaction = tx;
            read.CommandText =
                "SELECT Status, RuleVersion FROM AttendanceDecisions WHERE EventID=@event;";
            read.Parameters.AddWithValue("@event", eventId);
            using var rows = read.ExecuteReader();
            if (rows.Read())
            {
                previousStatus = rows.GetString(0);
                previousVersion = rows.GetString(1);
            }
        }

        if (previousStatus == status && previousVersion == decision.RuleVersion)
        {
            tx.Commit();
            return new DecisionMaterialization(DecisionWriteResult.Unchanged, decision);
        }

        using (var update = connection.CreateCommand())
        {
            update.Transaction = tx;
            update.CommandText = """
                INSERT INTO AttendanceDecisions
                    (EventID, RuleVersion, Status, EvaluatedAtUtc)
                VALUES (@event, @version, @status, @evaluated)
                ON CONFLICT(EventID) DO UPDATE SET
                    RuleVersion=excluded.RuleVersion,
                    Status=excluded.Status,
                    EvaluatedAtUtc=excluded.EvaluatedAtUtc;
                """;
            update.Parameters.AddWithValue("@event", eventId);
            update.Parameters.AddWithValue("@version", decision.RuleVersion);
            update.Parameters.AddWithValue("@status", status);
            update.Parameters.AddWithValue("@evaluated",
                DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            update.ExecuteNonQuery();
        }

        // Audit stores opaque numeric event ID, never student name/phone/message.
        using (var audit = connection.CreateCommand())
        {
            audit.Transaction = tx;
            audit.CommandText = """
                INSERT INTO AuditEvents(Action, EntityKey, ActorKey, CreatedAtUtc)
                VALUES (@action, @key, 'rules-engine', @now);
                """;
            audit.Parameters.AddWithValue("@action",
                previousStatus is null
                    ? "attendance.decision.created"
                    : "attendance.decision.revised");
            audit.Parameters.AddWithValue("@key", eventId.ToString(CultureInfo.InvariantCulture));
            audit.Parameters.AddWithValue("@now",
                DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            audit.ExecuteNonQuery();
        }

        tx.Commit();
        return new DecisionMaterialization(
            previousStatus is null ? DecisionWriteResult.Created : DecisionWriteResult.Revised,
            decision);
    }
}

public enum DecisionWriteResult { Created, Unchanged, Revised }
public sealed record DecisionMaterialization(DecisionWriteResult Result, AttendanceDecision Decision);
