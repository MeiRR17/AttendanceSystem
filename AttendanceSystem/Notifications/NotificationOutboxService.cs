using System.Data.SQLite;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AttendanceSystem.Database;

namespace AttendanceSystem.Notifications;

/// <summary>
/// Consent-checked, transactional notification outbox and offline delivery-state
/// simulator. No phone number, message body, Twilio credential or real provider
/// call exists in this class. Real sending is intentionally NOT implemented.
/// </summary>
public sealed class NotificationOutboxService
{
    public const string LatePurpose = "attendance-late";
    public const int MaxAttempts = 3;
    private readonly string _databasePath;

    public NotificationOutboxService(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
            throw new ArgumentException("An existing database path is required.", nameof(databasePath));
        _databasePath = Path.GetFullPath(databasePath);
    }

    /// <summary>
    /// Trusted administrative integration only. v0.7.0 must add authorization.
    /// EvidenceRef must be a non-PII reference to externally retained consent.
    /// </summary>
    public void RecordConsent(
        string recipientKey, bool granted, string evidenceRef,
        DateTimeOffset? at = null)
    {
        ValidKey(recipientKey, nameof(recipientKey), 128);
        ValidKey(evidenceRef, nameof(evidenceRef), 160);
        var now = Utc(at);
        using var db = OpenV3();
        using var tx = db.BeginTransaction();
        Execute(db, tx, """
            INSERT INTO NotificationConsents
                (RecipientKey,Purpose,IsGranted,EvidenceRef,ChangedAtUtc)
            VALUES (@recipient,@purpose,@granted,@evidence,@now)
            ON CONFLICT(RecipientKey,Purpose) DO UPDATE SET
                IsGranted=excluded.IsGranted,
                EvidenceRef=excluded.EvidenceRef,
                ChangedAtUtc=excluded.ChangedAtUtc;
            """, ("@recipient", recipientKey), ("@purpose", LatePurpose),
                  ("@granted", granted ? 1 : 0), ("@evidence", evidenceRef),
                  ("@now", Stamp(now)));
        if (!granted)
        {
            Execute(db, tx, """
                UPDATE NotificationOutbox SET Status='Suppressed',
                    LastResultCode='consent_revoked',UpdatedAtUtc=@now,
                    LeaseToken=NULL,LeaseUntilUtc=NULL
                WHERE RecipientKey=@recipient AND Purpose=@purpose AND
                    Status IN ('Queued','Retry','Leased');
                """, ("@recipient", recipientKey), ("@purpose", LatePurpose),
                      ("@now", Stamp(now)));
        }
        Audit(db, tx, granted ? "notifications.consent.granted" :
              "notifications.consent.revoked", Opaque(recipientKey), now);
        tx.Commit();
    }

    /// <summary>
    /// Test-only model of an approval registry. ApprovalEvidenceRef is not
    /// proof of actual Twilio/Meta template acceptance or legal authorization.
    /// </summary>
    public void RegisterTemplate(
        string templateKey, bool enabled, string approvalEvidenceRef,
        DateTimeOffset? at = null)
    {
        ValidKey(templateKey, nameof(templateKey), 100);
        ValidKey(approvalEvidenceRef, nameof(approvalEvidenceRef), 160);
        var now = Utc(at);
        using var db = OpenV3();
        using var tx = db.BeginTransaction();
        Execute(db, tx, """
            INSERT INTO NotificationTemplates
                (TemplateKey,Purpose,ApprovalEvidenceRef,IsEnabled)
            VALUES (@template,@purpose,@evidence,@enabled)
            ON CONFLICT(TemplateKey) DO UPDATE SET
                ApprovalEvidenceRef=excluded.ApprovalEvidenceRef,
                IsEnabled=excluded.IsEnabled;
            """, ("@template", templateKey), ("@purpose", LatePurpose),
                  ("@evidence", approvalEvidenceRef), ("@enabled", enabled ? 1 : 0));
        if (!enabled)
            Execute(db, tx, """
                UPDATE NotificationOutbox SET Status='Suppressed',
                    LastResultCode='template_disabled', UpdatedAtUtc=@now,
                    LeaseToken=NULL,LeaseUntilUtc=NULL
                WHERE TemplateKey=@template AND Status IN ('Queued','Retry','Leased');
                """, ("@template", templateKey), ("@now", Stamp(now)));
        Audit(db, tx, enabled ? "notifications.template.enabled" :
              "notifications.template.disabled", Opaque(templateKey), now);
        tx.Commit();
    }

    public EnqueueResult EnqueueLate(
        long eventId, string recipientKey, string templateKey,
        string expectedRuleVersion, DateTimeOffset? at = null)
    {
        if (eventId <= 0)
            throw new ArgumentOutOfRangeException(nameof(eventId));
        ValidKey(recipientKey, nameof(recipientKey), 128);
        ValidKey(templateKey, nameof(templateKey), 100);
        ValidKey(expectedRuleVersion, nameof(expectedRuleVersion), 64);
        var now = Utc(at);
        using var db = OpenV3();
        using var tx = db.BeginTransaction();
        if (!ConsentGranted(db, tx, recipientKey) || !TemplateEnabled(db, tx, templateKey))
            throw new InvalidOperationException("Consent or template approval is missing.");
        if (!MatchingLateDecision(db, tx, eventId, expectedRuleVersion))
            throw new InvalidOperationException("No matching approved Late attendance decision.");

        // Suppress notifications more often than once per 24 hours per
        // recipient/purpose; prevent event reclassification from bypassing cap.
        using (var limit = db.CreateCommand())
        {
            limit.Transaction = tx;
            limit.CommandText = """
                SELECT COUNT(*) FROM NotificationOutbox
                WHERE RecipientKey=@recipient AND Purpose=@purpose AND
                      CreatedAtUtc>@cutoff AND Status!='Suppressed';
                """;
            limit.Parameters.AddWithValue("@recipient", recipientKey);
            limit.Parameters.AddWithValue("@purpose", LatePurpose);
            limit.Parameters.AddWithValue("@cutoff", Stamp(now.AddHours(-24)));
            var count = Convert.ToInt32(limit.ExecuteScalar(), CultureInfo.InvariantCulture);
            if (count > 0)
            {
                // Return Existing only for exact logical key; otherwise rate-limited.
                using var existing = db.CreateCommand();
                existing.Transaction = tx;
                existing.CommandText = """
                    SELECT COUNT(*) FROM NotificationOutbox
                    WHERE EventID=@event AND RecipientKey=@recipient AND Purpose=@purpose;
                    """;
                existing.Parameters.AddWithValue("@event", eventId);
                existing.Parameters.AddWithValue("@recipient", recipientKey);
                existing.Parameters.AddWithValue("@purpose", LatePurpose);
                if (Convert.ToInt32(existing.ExecuteScalar(), CultureInfo.InvariantCulture) > 0)
                    return EnqueueResult.Existing;
                throw new InvalidOperationException("Recipient 24-hour notification cap reached.");
            }
        }

        var inserted = Execute(db, tx, """
            INSERT INTO NotificationOutbox
                (EventID,RecipientKey,Purpose,TemplateKey,RuleVersion,Status,
                 NextAttemptUtc,CreatedAtUtc,UpdatedAtUtc)
            VALUES (@event,@recipient,@purpose,@template,@rule,'Queued',
                    @now,@now,@now)
            ON CONFLICT(EventID,RecipientKey,Purpose) DO NOTHING;
            """, ("@event", eventId), ("@recipient", recipientKey),
                  ("@purpose", LatePurpose), ("@template", templateKey),
                  ("@rule", expectedRuleVersion), ("@now", Stamp(now)));
        if (inserted == 1)
            Audit(db, tx, "notifications.queued", eventId.ToString(CultureInfo.InvariantCulture), now);
        tx.Commit();
        return inserted == 1 ? EnqueueResult.Queued : EnqueueResult.Existing;
    }

    /// <summary>
    /// Only simulates a provider result. There is intentionally no Twilio send
    /// code or external transport in v0.6.0. Retried attempts are limited to
    /// failures known to have occurred BEFORE any provider transmission.
    /// </summary>
    public DryRunDispatchResult DispatchDryRunOnce(
        DryRunProviderResult simulatedProviderResult, DateTimeOffset? at = null)
    {
        var now = Utc(at);
        using var db = OpenV3();
        using var tx = db.BeginTransaction();
        long id;
        int attempts;
        string recipient;
        string template;
        long eventId;
        string ruleVersion;
        using (var select = db.CreateCommand())
        {
            select.Transaction = tx;
            select.CommandText = """
                SELECT OutboxID,AttemptCount,RecipientKey,TemplateKey,EventID,RuleVersion
                FROM NotificationOutbox
                WHERE Status IN ('Queued','Retry') AND NextAttemptUtc<=@now
                ORDER BY OutboxID LIMIT 1;
                """;
            select.Parameters.AddWithValue("@now", Stamp(now));
            using var reader = select.ExecuteReader();
            if (!reader.Read())
                return new DryRunDispatchResult(0, "NothingDue", 0);
            id = reader.GetInt64(0);
            attempts = reader.GetInt32(1);
            recipient = reader.GetString(2);
            template = reader.GetString(3);
            eventId = reader.GetInt64(4);
            ruleVersion = reader.GetString(5);
        }

        // Check opt-out and decision revision immediately before claiming.
        if (!ConsentGranted(db, tx, recipient) || !TemplateEnabled(db, tx, template) ||
            !MatchingLateDecision(db, tx, eventId, ruleVersion))
        {
            Execute(db, tx, """
                UPDATE NotificationOutbox SET Status='Suppressed',
                    LastResultCode='policy_or_consent_changed',UpdatedAtUtc=@now
                WHERE OutboxID=@id;
                """, ("@id", id), ("@now", Stamp(now)));
            tx.Commit();
            return new DryRunDispatchResult(id, "Suppressed", attempts);
        }

        attempts++;
        var state = simulatedProviderResult switch
        {
            DryRunProviderResult.Simulated => "Simulated",
            DryRunProviderResult.PermanentFailure => "DeadLetter",
            DryRunProviderResult.UnknownAfterAttempt => "Uncertain",
            DryRunProviderResult.FailureBeforeSend when attempts >= MaxAttempts => "DeadLetter",
            DryRunProviderResult.FailureBeforeSend => "Retry",
            _ => throw new ArgumentOutOfRangeException(nameof(simulatedProviderResult))
        };
        var next = state == "Retry" ? now.AddMinutes(Math.Pow(2, attempts - 1)) : now;
        var resultCode = simulatedProviderResult.ToString();
        Execute(db, tx, """
            UPDATE NotificationOutbox SET
                Status=@status, AttemptCount=@attempts, LastResultCode=@result,
                NextAttemptUtc=@next, LeaseToken=NULL,LeaseUntilUtc=NULL,
                UpdatedAtUtc=@now
            WHERE OutboxID=@id;
            """, ("@id", id), ("@status", state), ("@attempts", attempts),
                  ("@result", resultCode), ("@next", Stamp(next)), ("@now", Stamp(now)));
        Execute(db, tx, """
            INSERT INTO NotificationAttempts(OutboxID,AttemptNo,ResultCode,RecordedAtUtc)
            VALUES(@id,@attempts,@result,@now);
            """, ("@id", id), ("@attempts", attempts), ("@result", resultCode),
                  ("@now", Stamp(now)));
        tx.Commit();
        return new DryRunDispatchResult(id, state, attempts);
    }

    public IReadOnlyDictionary<string, long> GetStatusCounts()
    {
        using var db = OpenV3();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT Status, COUNT(*) FROM NotificationOutbox GROUP BY Status;";
        using var reader = cmd.ExecuteReader();
        var summary = new Dictionary<string, long>(StringComparer.Ordinal);
        while (reader.Read())
            summary[reader.GetString(0)] = reader.GetInt64(1);
        return summary;
    }

    private SQLiteConnection OpenV3()
    {
        var db = DatabaseSetup.Open(_databasePath);
        if (DatabaseSetup.ReadVersion(db) != DatabaseSetup.CurrentSchemaVersion)
        {
            db.Dispose();
            throw new InvalidOperationException("Outbox requires the approved SQLite schema migration.");
        }
        return db;
    }

    private static bool ConsentGranted(SQLiteConnection db, SQLiteTransaction tx, string recipient)
    {
        using var cmd = db.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            SELECT IsGranted FROM NotificationConsents
            WHERE RecipientKey=@recipient AND Purpose=@purpose;
            """;
        cmd.Parameters.AddWithValue("@recipient", recipient);
        cmd.Parameters.AddWithValue("@purpose", LatePurpose);
        return Convert.ToString(cmd.ExecuteScalar(), CultureInfo.InvariantCulture) == "1";
    }

    private static bool TemplateEnabled(SQLiteConnection db, SQLiteTransaction tx, string template)
    {
        using var cmd = db.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            SELECT IsEnabled FROM NotificationTemplates
            WHERE TemplateKey=@template AND Purpose=@purpose;
            """;
        cmd.Parameters.AddWithValue("@template", template);
        cmd.Parameters.AddWithValue("@purpose", LatePurpose);
        return Convert.ToString(cmd.ExecuteScalar(), CultureInfo.InvariantCulture) == "1";
    }

    private static bool MatchingLateDecision(
        SQLiteConnection db, SQLiteTransaction tx, long eventId, string ruleVersion)
    {
        using var cmd = db.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            SELECT COUNT(*) FROM AttendanceDecisions
            WHERE EventID=@event AND RuleVersion=@rule AND Status='Late';
            """;
        cmd.Parameters.AddWithValue("@event", eventId);
        cmd.Parameters.AddWithValue("@rule", ruleVersion);
        return Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture) == 1;
    }

    private static int Execute(
        SQLiteConnection db, SQLiteTransaction tx, string sql,
        params (string key, object value)[] arguments)
    {
        using var cmd = db.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        foreach (var (key, value) in arguments)
            cmd.Parameters.AddWithValue(key, value);
        return cmd.ExecuteNonQuery();
    }

    private static void Audit(
        SQLiteConnection db, SQLiteTransaction tx, string action,
        string opaqueEntityKey, DateTimeOffset now) =>
        Execute(db, tx, """
            INSERT INTO AuditEvents(Action,EntityKey,ActorKey,CreatedAtUtc)
            VALUES(@action,@entity,'notifications-engine',@now);
            """, ("@action", action), ("@entity", opaqueEntityKey),
                  ("@now", Stamp(now)));

    private static string Opaque(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..16];

    private static DateTimeOffset Utc(DateTimeOffset? at)
    {
        var now = at ?? DateTimeOffset.UtcNow;
        if (now.Offset != TimeSpan.Zero)
            throw new ArgumentException("Outbox timestamps must be UTC.");
        return now;
    }

    private static string Stamp(DateTimeOffset value) =>
        value.ToString("O", CultureInfo.InvariantCulture);

    private static void ValidKey(string value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maxLength ||
            !value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or ':' or '.'))
            throw new ArgumentException(name + " must be a bounded opaque identifier.", name);
    }
}

public enum EnqueueResult { Queued, Existing }
public enum DryRunProviderResult { Simulated, FailureBeforeSend, PermanentFailure, UnknownAfterAttempt }
public sealed record DryRunDispatchResult(long OutboxId, string Status, int AttemptCount);
