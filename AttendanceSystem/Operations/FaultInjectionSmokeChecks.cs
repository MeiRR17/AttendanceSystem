using System.Data.SQLite;
using System.Globalization;
using System.Security.Cryptography;
using AttendanceSystem.Database;
using AttendanceSystem.Gateway;

namespace AttendanceSystem.Operations;

/// <summary>
/// Synthetic fault campaign, entirely offline. Demonstrates SQLite rollback
/// before acknowledgement, blocked writes, replay after recovery, corruption
/// readiness, operational warning thresholds and privacy-safe metric labels.
/// </summary>
public static class FaultInjectionSmokeChecks
{
    public static void Run()
    {
        var folder = Path.Combine(Path.GetTempPath(),
            "attendance-fault-campaign-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var missing = Path.Combine(folder, "missing.db");
            var checkMissing = OperationalHealth.Check(missing);
            Require(!checkMissing.Ready && checkMissing.Code == "database_missing" &&
                    !File.Exists(missing), "readiness must not create a database");

            var corrupt = Path.Combine(folder, "damaged.db");
            File.WriteAllBytes(corrupt, RandomNumberGenerator.GetBytes(256));
            Require(!OperationalHealth.Check(corrupt).Ready,
                    "damaged SQLite cannot report ready");

            var path = Path.Combine(folder, "synthetic.db");
            DatabaseSetup.InitializeDatabaseAtPath(path);
            using (var conn = DatabaseSetup.Open(path))
            using (var command = conn.CreateCommand())
            {
                command.CommandText = """
                    INSERT INTO Devices(DeviceID,Enabled,RegisteredAtUtc)
                    VALUES('fault-device',1,'2026-11-09T09:00:00Z');
                    """;
                command.ExecuteNonQuery();
            }
            Require(OperationalHealth.Check(path).Ready, "healthy DB is ready");

            // Simulate process abort before a transaction commits. A rollback
            // MUST leave no event, and the same stable event key must be safe
            // to retry following restart.
            using (var db = DatabaseSetup.Open(path))
            {
                using (var tx = db.BeginTransaction())
                using (var cmd = db.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = """
                        INSERT INTO AttendanceEvents
                            (DeviceID,EventKey,StudentID,OccurredAtUtc,ReceivedAtUtc)
                        VALUES ('fault-device','aborted-key','synthetic-student',
                                '2026-11-09T09:05:00.0000000+00:00',
                                '2026-11-09T09:05:00.0000000+00:00');
                        """;
                    cmd.ExecuteNonQuery();
                    // No Commit: Dispose => rollback as if the worker died.
                }
            }
            Require(CountEvents(path, "aborted-key") == 0,
                    "uncommitted event must not be acknowledged or retained");

            var now = DateTimeOffset.UtcNow;
            var key = RandomNumberGenerator.GetBytes(32);
            var gateway = new AuthenticatedEventGateway(
                new AttendanceEventStore(path), "fault-device", key);
            var xml = "<TimeLog><EventKey>aborted-key</EventKey>" +
                      "<StudentID>synthetic-student</StudentID><OccurredAtUtc>" +
                      now.AddMinutes(-1).ToString("O", CultureInfo.InvariantCulture) +
                      "</OccurredAtUtc></TimeLog>";
            var signed = now.ToUnixTimeSeconds();
            var sig = AuthenticatedEventGateway.Sign("fault-device", signed, xml, key);
            var before = OperationalMetrics.Snapshot();
            Require(gateway.Accept("fault-device", signed, sig, xml) == GatewayOutcome.Inserted,
                    "retry after rollback must insert once");
            Require(gateway.Accept("fault-device", signed, sig, xml) == GatewayOutcome.Duplicate,
                    "identical packet replay after recovery must deduplicate");
            Require(CountEvents(path, "aborted-key") == 1,
                    "one durable logical event after replay");
            var after = OperationalMetrics.Snapshot();
            Require(after.InsertedEvents >= before.InsertedEvents + 1 &&
                    after.DuplicateReplays >= before.DuplicateReplays + 1 &&
                    after.PersistP95Milliseconds is not null,
                    "telemetry must record successful inserts and replays");

            // Real database lock contention, not a mocked exception.
            // Default busy timeout is bounded to 5s, and a blocked write is
            // not acknowledged. After release the same stable event can retry.
            var lockXml = xml.Replace("aborted-key", "locked-key", StringComparison.Ordinal);
            var lockSig = AuthenticatedEventGateway.Sign("fault-device", signed, lockXml, key);
            using (var lockHolder = DatabaseSetup.Open(path))
            using (var tx = lockHolder.BeginTransaction())
            {
                using var command = lockHolder.CreateCommand();
                command.Transaction = tx;
                command.CommandText =
                    "UPDATE Devices SET Enabled=Enabled WHERE DeviceID='fault-device';";
                command.ExecuteNonQuery();

                var failed = false;
                try
                {
                    gateway.Accept("fault-device", signed, lockSig, lockXml);
                }
                catch (SQLiteException)
                {
                    OperationalMetrics.RecordDatabaseError();
                    failed = true;
                }
                Require(failed, "SQLite lock must fail closed, never return success ACK");
                tx.Rollback();
            }
            Require(CountEvents(path, "locked-key") == 0,
                    "blocked write did not persist");
            Require(gateway.Accept("fault-device", signed, lockSig, lockXml) ==
                    GatewayOutcome.Inserted, "write succeeds after lock release");
            Require(CountEvents(path, "locked-key") == 1,
                    "one event after lock recovery");

            // No real student/notification content. Construct 30 synthetic
            // queued jobs to demonstrate redacted warning thresholds.
            using (var db = DatabaseSetup.Open(path))
            using (var tx = db.BeginTransaction())
            {
                using var template = db.CreateCommand();
                template.Transaction = tx;
                template.CommandText = """
                    INSERT INTO NotificationTemplates
                        (TemplateKey,Purpose,ApprovalEvidenceRef,IsEnabled)
                    VALUES ('fixture-template','attendance-late','synthetic-only',1);
                    """;
                template.ExecuteNonQuery();

                for (var i = 1; i <= 30; i++)
                {
                    using var insert = db.CreateCommand();
                    insert.Transaction = tx;
                    insert.CommandText = """
                        INSERT INTO AttendanceEvents
                            (DeviceID,EventKey,StudentID,OccurredAtUtc,ReceivedAtUtc)
                        VALUES('fault-device',@key,'synthetic-student',
                               '2026-11-09T09:05:00.0000000+00:00',
                               '2026-11-09T09:05:00.0000000+00:00');
                        INSERT INTO NotificationOutbox
                            (EventID,RecipientKey,Purpose,TemplateKey,RuleVersion,Status,
                             NextAttemptUtc,CreatedAtUtc,UpdatedAtUtc)
                        VALUES(last_insert_rowid(),@recipient,'attendance-late',
                               'fixture-template','fixture-v1','Queued',
                               '2026-11-09T09:05:00Z','2026-11-09T09:05:00Z',
                               '2026-11-09T09:05:00Z');
                        """;
                    insert.Parameters.AddWithValue("@key", "queue-" + i);
                    insert.Parameters.AddWithValue("@recipient", "synthetic-recipient-" + i);
                    insert.ExecuteNonQuery();
                }
                tx.Commit();
            }
            var overloaded = OperationalHealth.Check(path);
            Require(overloaded.Ready && overloaded.PendingDryRunNotifications == 30 &&
                    overloaded.Alerts.Contains("dry_run_outbox_backlog"),
                    "overfull synthetic outbox triggers warning, not a false outage");

            using (var db = DatabaseSetup.Open(path))
            using (var cmd = db.CreateCommand())
            {
                cmd.CommandText = """
                    UPDATE NotificationOutbox SET Status='Uncertain'
                    WHERE OutboxID=(SELECT MIN(OutboxID) FROM NotificationOutbox);
                    UPDATE NotificationOutbox SET Status='DeadLetter'
                    WHERE OutboxID=(SELECT MAX(OutboxID) FROM NotificationOutbox);
                    """;
                cmd.ExecuteNonQuery();
            }
            var alerts = OperationalHealth.Check(path);
            Require(alerts.Ready && alerts.PendingDryRunNotifications == 28 &&
                    alerts.UncertainDryRunNotifications == 1 &&
                    alerts.DeadLetterDryRunNotifications == 1 &&
                    alerts.Alerts.Contains("ambiguous_delivery_requires_manual_review") &&
                    alerts.Alerts.Contains("dead_letter_requires_manual_review"),
                    "unknown outcomes and dead-letter explicitly require review");

            var telemetry = alerts.Metrics;
            Require(telemetry.DatabaseWriteErrors > 0 &&
                    telemetry.Scope == "in_process_since_start" &&
                    alerts.DeviceConnectivity == "not_verified",
                    "metrics are in-process, device status unverified");
            Console.WriteLine(
                "Offline fault campaign passed: missing/corrupt DB, rollback, " +
                "5s SQLite lock with no ACK, replay after recovery, 30-job backlog, " +
                "uncertain/deadletter warnings, no PII or network.");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static long CountEvents(string path, string key)
    {
        using var db = DatabaseSetup.Open(path);
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(*) FROM AttendanceEvents
            WHERE DeviceID='fault-device' AND EventKey=@key;
            """;
        cmd.Parameters.AddWithValue("@key", key);
        return Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static void Require(bool value, string description)
    {
        if (!value)
            throw new InvalidOperationException("Fault campaign failed: " + description);
    }
}
