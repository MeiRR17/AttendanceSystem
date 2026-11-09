using System.Data.SQLite;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AttendanceSystem.Database;

namespace AttendanceSystem.AdminApi;

/// <summary>
/// SQLite access for versioned attendance views and manual revisions. No PII
/// projection: even Staff only see opaque event IDs, UTC times and statuses.
/// No method mutates a source AttendanceEvents or AttendanceDecisions row.
/// </summary>
public sealed class AttendanceAdminStore
{
    private readonly string _path;
    private static readonly HashSet<string> AllowedStatuses =
        new(StringComparer.Ordinal) { "OnTime", "Late", "Excused", "Invalid", "Unclassified" };
    private static readonly HashSet<string> AllowedReasons =
        new(StringComparer.Ordinal)
        {
            "clock-skew", "approved-excuse", "duplicate-scan",
            "manual-verification", "administrative-correction"
        };

    public AttendanceAdminStore(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
            throw new ArgumentException("Existing database path required.", nameof(databasePath));
        _path = Path.GetFullPath(databasePath);
    }

    public IReadOnlyList<AttendanceEventView> ListEvents(long afterId, int limit)
    {
        if (afterId < 0 || limit < 1 || limit > 100)
            throw new ArgumentOutOfRangeException(nameof(limit), "Cursor >= 0 and limit 1..100 required.");
        using var db = Open();
        using var command = db.CreateCommand();
        command.CommandText = """
            SELECT e.EventID, e.OccurredAtUtc,
                   COALESCE((
                       SELECT c.AfterStatus FROM AttendanceCorrections c
                       WHERE c.EventID=e.EventID ORDER BY c.Revision DESC LIMIT 1
                   ),d.Status),
                   COALESCE((
                       SELECT MAX(c.Revision) FROM AttendanceCorrections c
                       WHERE c.EventID=e.EventID
                   ),0)
            FROM AttendanceEvents e JOIN AttendanceDecisions d ON d.EventID=e.EventID
            WHERE e.EventID>@cursor ORDER BY e.EventID LIMIT @limit;
            """;
        command.Parameters.AddWithValue("@cursor", afterId);
        command.Parameters.AddWithValue("@limit", limit);
        var rows = new List<AttendanceEventView>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
            rows.Add(new AttendanceEventView(
                reader.GetInt64(0), reader.GetString(1),
                reader.GetString(2), reader.GetInt64(3)));
        return rows;
    }

    public AttendanceEventView? FindEvent(long eventId)
    {
        if (eventId <= 0) return null;
        using var db = Open();
        return FindEvent(db, null, eventId);
    }

    public IReadOnlyDictionary<string, long> Overview()
    {
        using var db = Open();
        using var command = db.CreateCommand();
        command.CommandText = """
            SELECT EffectiveStatus, COUNT(*) FROM (
                SELECT COALESCE((
                    SELECT c.AfterStatus FROM AttendanceCorrections c
                    WHERE c.EventID=e.EventID ORDER BY c.Revision DESC LIMIT 1
                ),d.Status) AS EffectiveStatus
                FROM AttendanceEvents e JOIN AttendanceDecisions d ON d.EventID=e.EventID
            ) GROUP BY EffectiveStatus;
            """;
        var result = new Dictionary<string, long>(StringComparer.Ordinal);
        using var reader = command.ExecuteReader();
        while (reader.Read()) result.Add(reader.GetString(0), reader.GetInt64(1));
        return result;
    }

    /// <summary>
    /// Role is a validated, trusted JWT claim from AdminApiServer. The store
    /// repeats the role check as defense in depth for other future callers.
    /// </summary>
    public AttendanceCorrectionView Correct(
        long eventId, long expectedRevision, string newStatus,
        string reasonCode, string actorSubject, string actorRole)
    {
        if (actorRole is not ("Administrator" or "Staff") ||
            string.IsNullOrWhiteSpace(actorSubject) || actorSubject.Length > 256)
            throw new UnauthorizedAccessException("Authorized staff identity required.");
        if (eventId <= 0 || expectedRevision < 0)
            throw new ArgumentException("Event/revision must be nonnegative.");
        if (!AllowedStatuses.Contains(newStatus) || !AllowedReasons.Contains(reasonCode))
            throw new ArgumentException("Unsupported correction status or controlled reason code.");

        using var db = Open();
        using var tx = db.BeginTransaction();
        var before = FindEvent(db, tx, eventId)
                     ?? throw new KeyNotFoundException("Event with a derived decision not found.");
        if (before.Revision != expectedRevision)
            throw new AdminConflictException("Revision conflict: refresh before correction.");
        if (before.Status == newStatus)
            throw new AdminConflictException("An identical status cannot create a new correction.");

        var revision = expectedRevision + 1;
        var actorHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(actorSubject)))[..32];
        var now = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);

        using (var insert = db.CreateCommand())
        {
            insert.Transaction = tx;
            insert.CommandText = """
                INSERT INTO AttendanceCorrections
                    (EventID,Revision,BeforeStatus,AfterStatus,ReasonCode,
                     ActorHash,RecordedAtUtc)
                VALUES(@event,@revision,@before,@after,@reason,@actor,@now);
                """;
            insert.Parameters.AddWithValue("@event", eventId);
            insert.Parameters.AddWithValue("@revision", revision);
            insert.Parameters.AddWithValue("@before", before.Status);
            insert.Parameters.AddWithValue("@after", newStatus);
            insert.Parameters.AddWithValue("@reason", reasonCode);
            insert.Parameters.AddWithValue("@actor", actorHash);
            insert.Parameters.AddWithValue("@now", now);
            insert.ExecuteNonQuery();
        }

        using (var audit = db.CreateCommand())
        {
            audit.Transaction = tx;
            audit.CommandText = """
                INSERT INTO AuditEvents(Action,EntityKey,ActorKey,CreatedAtUtc)
                VALUES('attendance.correction.created',@event,@actor,@now);
                """;
            audit.Parameters.AddWithValue("@event", eventId.ToString(CultureInfo.InvariantCulture));
            audit.Parameters.AddWithValue("@actor", actorHash);
            audit.Parameters.AddWithValue("@now", now);
            audit.ExecuteNonQuery();
        }

        tx.Commit();
        return new AttendanceCorrectionView(
            eventId, revision, before.Status, newStatus, reasonCode, actorHash, now);
    }

    public IReadOnlyList<AttendanceCorrectionView> Corrections(long eventId, int limit = 50)
    {
        if (eventId <= 0 || limit < 1 || limit > 100)
            throw new ArgumentException("Invalid event or correction page length.");
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            SELECT EventID,Revision,BeforeStatus,AfterStatus,ReasonCode,
                   ActorHash,RecordedAtUtc
            FROM AttendanceCorrections
            WHERE EventID=@event ORDER BY Revision DESC LIMIT @limit;
            """;
        cmd.Parameters.AddWithValue("@event", eventId);
        cmd.Parameters.AddWithValue("@limit", limit);
        var result = new List<AttendanceCorrectionView>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            result.Add(new AttendanceCorrectionView(
                reader.GetInt64(0), reader.GetInt64(1),
                reader.GetString(2), reader.GetString(3), reader.GetString(4),
                reader.GetString(5), reader.GetString(6)));
        return result;
    }

    private SQLiteConnection Open()
    {
        var db = DatabaseSetup.Open(_path);
        if (DatabaseSetup.ReadVersion(db) != DatabaseSetup.CurrentSchemaVersion)
        {
            db.Dispose();
            throw new InvalidOperationException("Authorized schema v4 is required.");
        }
        return db;
    }

    private static AttendanceEventView? FindEvent(
        SQLiteConnection db, SQLiteTransaction? tx, long eventId)
    {
        using var cmd = db.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            SELECT e.EventID, e.OccurredAtUtc,
                   COALESCE((
                       SELECT c.AfterStatus FROM AttendanceCorrections c
                       WHERE c.EventID=e.EventID ORDER BY c.Revision DESC LIMIT 1
                   ),d.Status),
                   COALESCE((
                       SELECT MAX(c.Revision) FROM AttendanceCorrections c
                       WHERE c.EventID=e.EventID
                   ),0)
            FROM AttendanceEvents e JOIN AttendanceDecisions d ON d.EventID=e.EventID
            WHERE e.EventID=@event;
            """;
        cmd.Parameters.AddWithValue("@event", eventId);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        return new AttendanceEventView(
            reader.GetInt64(0), reader.GetString(1),
            reader.GetString(2), reader.GetInt64(3));
    }
}

public sealed record AttendanceEventView(
    long EventId, string OccurredAtUtc, string Status, long Revision);
public sealed record AttendanceCorrectionView(
    long EventId, long Revision, string BeforeStatus, string AfterStatus,
    string ReasonCode, string ActorHash, string RecordedAtUtc);
public sealed record AttendanceCorrectionRequest(
    long ExpectedRevision, string NewStatus, string ReasonCode);

public sealed class AdminConflictException(string message) : Exception(message);
