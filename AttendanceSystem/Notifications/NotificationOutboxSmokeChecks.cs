using System.Data.SQLite;
using System.Globalization;
using AttendanceSystem.Database;

namespace AttendanceSystem.Notifications;

/// <summary>
/// Offline-only disposable SQLite integration checks. No names, telephone
/// numbers, Twilio calls or real consent records are created.
/// </summary>
public static class NotificationOutboxSmokeChecks
{
    public static void Run()
    {
        var dir = Path.Combine(Path.GetTempPath(), "attendance-outbox-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var now = new DateTimeOffset(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);
        try
        {
            var path = Path.Combine(dir, "synthetic.db");
            DatabaseSetup.InitializeDatabaseAtPath(path);
            using (var db = DatabaseSetup.Open(path))
            using (var seed = db.CreateCommand())
            {
                seed.CommandText = """
                    INSERT INTO Devices(DeviceID,Enabled,RegisteredAtUtc)
                    VALUES('mock-device',1,'2026-11-09T10:00:00Z');
                    INSERT INTO AttendanceEvents
                        (DeviceID,EventKey,StudentID,OccurredAtUtc,ReceivedAtUtc)
                    VALUES
                        ('mock-device','e1','synthetic-student-1','2026-11-09T08:00:00.0000000+00:00','2026-11-09T08:00:00.0000000+00:00'),
                        ('mock-device','e2','synthetic-student-2','2026-11-09T08:05:00.0000000+00:00','2026-11-09T08:05:00.0000000+00:00'),
                        ('mock-device','e3','synthetic-student-3','2026-11-09T08:10:00.0000000+00:00','2026-11-09T08:10:00.0000000+00:00'),
                        ('mock-device','e4','synthetic-student-4','2026-11-09T08:15:00.0000000+00:00','2026-11-09T08:15:00.0000000+00:00'),
                        ('mock-device','e5','synthetic-student-5','2026-11-09T08:20:00.0000000+00:00','2026-11-09T08:20:00.0000000+00:00'),
                        ('mock-device','e6','synthetic-student-6','2026-11-09T08:25:00.0000000+00:00','2026-11-09T08:25:00.0000000+00:00'),
                        ('mock-device','e7','synthetic-student-7','2026-11-09T08:30:00.0000000+00:00','2026-11-09T08:30:00.0000000+00:00');
                    INSERT INTO AttendanceDecisions
                        (EventID,RuleVersion,Status,EvaluatedAtUtc)
                    SELECT EventID,'synthetic-rule-v1','Late','2026-11-09T09:00:00Z'
                    FROM AttendanceEvents;
                    """;
                seed.ExecuteNonQuery();
            }

            var outbox = new NotificationOutboxService(path);
            Invalid(() => outbox.EnqueueLate(1,"recipient-1","template-1","synthetic-rule-v1",now),
                    "no consent and template");
            outbox.RegisterTemplate("template-1",true,"synthetic-approval-evidence",now);
            Invalid(() => outbox.EnqueueLate(1,"recipient-1","template-1","synthetic-rule-v1",now),
                    "no consent");
            outbox.RecordConsent("recipient-1",true,"synthetic-opt-in-evidence",now);

            Check(outbox.EnqueueLate(1,"recipient-1","template-1","synthetic-rule-v1",now)
                  == EnqueueResult.Queued,"first queue");
            Check(outbox.EnqueueLate(1,"recipient-1","template-1","synthetic-rule-v1",now)
                  == EnqueueResult.Existing,"deduplicate queue");
            Check(Count(path,"NotificationOutbox") == 1,"one logical outbox row");
            Invalid(() => outbox.EnqueueLate(2,"recipient-1","template-1","synthetic-rule-v1",now),
                    "24 hour recipient rate cap");
            Invalid(() => outbox.EnqueueLate(2,"recipient-2","template-1","synthetic-rule-v1",now),
                    "no consent for recipient 2");
            Invalid(() => outbox.EnqueueLate(99,"recipient-1","template-1","synthetic-rule-v1",
                                             now.AddHours(25)),
                    "event must exist and be classified Late");

            var first = outbox.DispatchDryRunOnce(DryRunProviderResult.Simulated,now);
            Check(first.Status == "Simulated" && first.AttemptCount == 1,"dry-run simulated only");
            Check(outbox.DispatchDryRunOnce(DryRunProviderResult.Simulated,now).Status == "NothingDue",
                  "simulation never resends");
            Check(Count(path,"NotificationAttempts")==1,"only one attempt ledger entry");
            Check(Count(path,"AttendanceEvents")==7,"no attendance rollback");

            outbox.RecordConsent("recipient-2",true,"synthetic-consent-2",now);
            Check(outbox.EnqueueLate(2,"recipient-2","template-1","synthetic-rule-v1",now)
                  == EnqueueResult.Queued,"recipient 2 queued");
            outbox.RecordConsent("recipient-2",false,"synthetic-opt-out-2",now);
            Check(outbox.DispatchDryRunOnce(DryRunProviderResult.Simulated,now).Status=="NothingDue",
                  "opt-out cancels queued item");
            Check(StatusCount(path,"Suppressed")==1,"revoked recipient suppressed");
            Invalid(() => outbox.EnqueueLate(3,"recipient-2","template-1","synthetic-rule-v1",now),
                    "revoked consent cannot enqueue");

            outbox.RecordConsent("recipient-3",true,"synthetic-consent-3",now);
            Check(outbox.EnqueueLate(3,"recipient-3","template-1","synthetic-rule-v1",now)
                  == EnqueueResult.Queued,"third recipient queued");
            var retry1=outbox.DispatchDryRunOnce(DryRunProviderResult.FailureBeforeSend,now);
            Check(retry1.Status=="Retry" && retry1.AttemptCount==1,"first retry scheduled");
            Check(outbox.DispatchDryRunOnce(DryRunProviderResult.Simulated,now).Status=="NothingDue",
                  "not sent before retry deadline");
            var retry2=outbox.DispatchDryRunOnce(DryRunProviderResult.FailureBeforeSend,
                                                now.AddMinutes(1));
            Check(retry2.Status=="Retry" && retry2.AttemptCount==2,"second backoff");
            Check(outbox.DispatchDryRunOnce(DryRunProviderResult.Simulated,
                                            now.AddMinutes(2)).Status=="NothingDue",
                  "exponential backoff enforced");
            var retry3=outbox.DispatchDryRunOnce(DryRunProviderResult.FailureBeforeSend,
                                                now.AddMinutes(3));
            Check(retry3.Status=="DeadLetter" && retry3.AttemptCount==3,"max retries deadletter");
            Check(Count(path,"NotificationAttempts")==4,"three attempts plus first simulated");

            outbox.RecordConsent("recipient-4",true,"synthetic-consent-4",now);
            Check(outbox.EnqueueLate(4,"recipient-4","template-1","synthetic-rule-v1",now)
                  == EnqueueResult.Queued,"fourth recipient queued");
            Check(outbox.DispatchDryRunOnce(DryRunProviderResult.UnknownAfterAttempt,now).Status
                  == "Uncertain","ambiguous result held for manual review");
            Check(outbox.DispatchDryRunOnce(DryRunProviderResult.Simulated,now.AddDays(10)).Status
                  == "NothingDue","unknown outcome never automatically retried");

            outbox.RecordConsent("recipient-5",true,"synthetic-consent-5",now);
            Check(outbox.EnqueueLate(5,"recipient-5","template-1","synthetic-rule-v1",now)
                  == EnqueueResult.Queued,"fifth recipient queued");
            using (var db = DatabaseSetup.Open(path))
            using (var cmd = db.CreateCommand())
            {
                cmd.CommandText = """
                    UPDATE AttendanceDecisions SET Status='OnTime'
                    WHERE EventID=5;
                    """;
                cmd.ExecuteNonQuery();
            }
            Check(outbox.DispatchDryRunOnce(DryRunProviderResult.Simulated,now).Status
                  == "Suppressed","reclassified decision cancels delivery");
            Check(StatusCount(path,"Suppressed")==2,"both revocation and policy change suppressed");

            outbox.RecordConsent("recipient-6",true,"synthetic-consent-6",now);
            Check(outbox.EnqueueLate(6,"recipient-6","template-1","synthetic-rule-v1",now)
                  == EnqueueResult.Queued,"sixth recipient queued");
            outbox.RegisterTemplate("template-1",false,"synthetic-disabled-evidence",now);
            Check(StatusCount(path,"Suppressed")==3,"template revoke suppressed queued");
            Check(outbox.DispatchDryRunOnce(DryRunProviderResult.Simulated,now).Status
                  == "NothingDue","no outstanding delivery without template");
            Invalid(() => outbox.EnqueueLate(7,"recipient-6","template-1","synthetic-rule-v1",now),
                    "disabled template cannot enqueue");

            Check(outbox.GetStatusCounts().TryGetValue("Simulated",out var simulated) &&
                  simulated==1,"reports distinguish simulation from actual delivery");
            Check(Count(path,"NotificationOutbox")==6,"all logical events persisted");
            Check(Count(path,"AttendanceEvents")==7,"failures never roll back attendance");

            Console.WriteLine("Offline WhatsApp outbox checks passed: consent, no-send dry-run, " +
                              "dedup, opt-out, 24h cap, backoff, deadletter, ambiguous hold, " +
                              "template disable and decision revision.");
        }
        finally
        {
            Directory.Delete(dir,recursive:true);
        }
    }

    private static long Count(string path,string table)
    {
        // This method is only used with the fixed static schema table names below.
        if (table != "NotificationOutbox" && table != "NotificationAttempts" &&
            table != "AttendanceEvents")
            throw new ArgumentException("Unknown synthetic test table.");
        using var db=DatabaseSetup.Open(path);
        using var cmd=db.CreateCommand();
        cmd.CommandText="SELECT COUNT(*) FROM "+table+";";
        return Convert.ToInt64(cmd.ExecuteScalar(),CultureInfo.InvariantCulture);
    }

    private static long StatusCount(string path,string status)
    {
        using var db=DatabaseSetup.Open(path);
        using var cmd=db.CreateCommand();
        cmd.CommandText="SELECT COUNT(*) FROM NotificationOutbox WHERE Status=@status;";
        cmd.Parameters.AddWithValue("@status",status);
        return Convert.ToInt64(cmd.ExecuteScalar(),CultureInfo.InvariantCulture);
    }

    private static void Check(bool condition,string scenario)
    {
        if(!condition)throw new InvalidOperationException("Outbox smoke check failed: "+scenario);
    }

    private static void Invalid(Action action,string scenario)
    {
        try { action(); }
        catch(InvalidOperationException){ return; }
        throw new InvalidOperationException("Outbox accepted forbidden case: "+scenario);
    }
}
