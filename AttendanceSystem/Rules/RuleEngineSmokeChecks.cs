using System.Data.SQLite;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using AttendanceSystem.Database;

namespace AttendanceSystem.Rules;

public static class RuleEngineSmokeChecks
{
    public static async Task RunAsync()
    {
        const string original = """
        {
          "RuleVersion": "v5-01",
          "TimeZoneId": "Asia/Jerusalem",
          "StudyDays": ["Sunday","Monday","Tuesday","Wednesday","Thursday"],
          "Sessions": [
            {"Day":"Sunday","Name":"Morning","StartLocal":"08:00:00","CutoffLocal":"09:30:00","GraceMinutes":10,"EarlyArrivalMinutes":20},
            {"Day":"Monday","Name":"Morning","StartLocal":"08:00:00","CutoffLocal":"09:30:00","GraceMinutes":10,"EarlyArrivalMinutes":20},
            {"Day":"Tuesday","Name":"Morning","StartLocal":"08:00:00","CutoffLocal":"09:30:00","GraceMinutes":10,"EarlyArrivalMinutes":20},
            {"Day":"Wednesday","Name":"Morning","StartLocal":"08:00:00","CutoffLocal":"09:30:00","GraceMinutes":10,"EarlyArrivalMinutes":20},
            {"Day":"Thursday","Name":"Morning","StartLocal":"08:00:00","CutoffLocal":"09:30:00","GraceMinutes":10,"EarlyArrivalMinutes":20},
            {"Day":"Thursday","Name":"Night","StartLocal":"23:30:00","CutoffLocal":"01:00:00","GraceMinutes":5,"EarlyArrivalMinutes":10},
            {"Day":"Friday","Name":"Morning","StartLocal":"08:00:00","CutoffLocal":"09:30:00","GraceMinutes":10,"EarlyArrivalMinutes":20}
          ],
          "Overrides": [
            {"Date":"2026-11-13","Mode":"Study","Reason":"synthetic special study day"},
            {"Date":"2026-11-15","Mode":"Excused","Reason":"synthetic holiday closure"},
            {"Date":"2026-11-22","Mode":"NoStudy","Reason":"synthetic optional study break"}
          ]
        }
        """;

        var policy = InstitutionAttendancePolicy.FromJson(original);
        var engine = new AttendanceRuleEngine(policy);
        static DateTimeOffset Utc(int year, int month, int day, int hour, int minute) =>
            new(year, month, day, hour, minute, 0, TimeSpan.Zero);
        static void Status(AttendanceDecision item, AttendanceState expected, string label)
        {
            if (item.Status != expected)
                throw new InvalidOperationException("Rules smoke failed (" + label +
                                                    "): got " + item.Status);
        }

        Status(engine.EvaluateEvent(Utc(2026, 11, 8, 6, 5)),
               AttendanceState.OnTime, "Sunday is configurable study day");
        Status(engine.EvaluateEvent(Utc(2026, 11, 8, 6, 10)),
               AttendanceState.OnTime, "grace minute inclusive");
        Status(engine.EvaluateEvent(Utc(2026, 11, 8, 6, 25)),
               AttendanceState.Late, "late arrival");
        Status(engine.EvaluateEvent(Utc(2026, 11, 8, 7, 35)),
               AttendanceState.Unclassified, "after cutoff");
        Status(engine.EvaluateEvent(Utc(2026, 11, 14, 6, 5)),
               AttendanceState.Unclassified, "Saturday is off by POLICY");
        Status(engine.EvaluateEvent(Utc(2026, 11, 13, 6, 5)),
               AttendanceState.OnTime, "Friday override opens class");
        Status(engine.EvaluateEvent(Utc(2026, 11, 15, 6, 5)),
               AttendanceState.Excused, "explicit closure excused");
        Status(engine.EvaluateEvent(Utc(2026, 11, 22, 6, 5)),
               AttendanceState.Unclassified, "explicit nonstudy day");
        Status(engine.EvaluateEvent(Utc(2026, 6, 21, 5, 5)),
               AttendanceState.OnTime, "Israel daylight saving conversion");
        Status(engine.EvaluateEvent(Utc(2026, 11, 12, 21, 45)),
               AttendanceState.Late, "Thursday night session");
        var overnight = engine.EvaluateEvent(Utc(2026, 11, 12, 22, 15));
        Status(overnight, AttendanceState.Late, "overnight arrival on Friday morning");
        Check(overnight.SessionDate == new DateOnly(2026, 11, 12),
              "overnight belongs to Thursday");
        Status(engine.EvaluateMissing(new DateOnly(2026, 11, 8), "Morning",
                                      Utc(2026, 11, 8, 7, 29)),
               AttendanceState.Unclassified, "missing prior to cutoff");
        Status(engine.EvaluateMissing(new DateOnly(2026, 11, 8), "Morning",
                                      Utc(2026, 11, 8, 7, 31)),
               AttendanceState.Absent, "missing after cutoff");
        Status(engine.EvaluateMissing(new DateOnly(2026, 11, 15), "Morning",
                                      Utc(2026, 11, 15, 8, 0)),
               AttendanceState.Excused, "excused not absent");
        Status(engine.EvaluateMissing(new DateOnly(2026, 11, 14), "Morning",
                                      Utc(2026, 11, 14, 8, 0)),
               AttendanceState.Unclassified, "weekend not absent");
        ExpectInvalid(() => InstitutionAttendancePolicy.FromJson(
            original.Replace("\"v5-01\"", "\"v5-01\", \"bad\": true")),
            "unknown policy fields");
        ExpectInvalid(() => InstitutionAttendancePolicy.FromJson(
            original.Replace("\"StartLocal\":\"08:00:00\",\"CutoffLocal\":\"09:30:00\"",
                             "\"StartLocal\":\"08:00:00\",\"CutoffLocal\":\"08:00:00\"")),
            "invalid session window");
        ExpectInvalid(() => engine.EvaluateEvent(
            new DateTimeOffset(2026, 11, 8, 8, 0, 0, TimeSpan.FromHours(2))),
            "non-UTC event rejected");

        var root = Path.Combine(Path.GetTempPath(), "attendance-rules-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "synthetic-rules.db");
            DatabaseSetup.InitializeDatabaseAtPath(path);
            using (var db = DatabaseSetup.Open(path))
            using (var cmd = db.CreateCommand())
            {
                cmd.CommandText = """
                    INSERT INTO Devices(DeviceID, Enabled, RegisteredAtUtc)
                    VALUES ('synthetic-device',1,'2026-11-09T00:00:00Z');
                    """;
                cmd.ExecuteNonQuery();
            }
            var store = new AttendanceEventStore(path);
            Check(store.Save(new AttendanceEventInput("synthetic-device","event-001",
                  "synthetic-student", Utc(2026,11,8,6,5))) == AttendanceWriteResult.Inserted,
                  "synthetic event persisted");
            var writer = new AttendanceDecisionWriter(path);
            Check(writer.Materialize(1,engine).Result == DecisionWriteResult.Created,
                  "derived status created");
            Check(writer.Materialize(1,engine).Result == DecisionWriteResult.Unchanged,
                  "idempotent same-rule evaluation");
            var version2 = new AttendanceRuleEngine(
                InstitutionAttendancePolicy.FromJson(original.Replace("v5-01","v5-02")));
            Check(writer.Materialize(1,version2).Result == DecisionWriteResult.Revised,
                  "versioned rule reevaluation tracked");
            var policyChange = new AttendanceRuleEngine(
                InstitutionAttendancePolicy.FromJson(
                    original.Replace("v5-01","v5-03").Replace("\"GraceMinutes\":10",
                                                                       "\"GraceMinutes\":0")));
            var changed = writer.Materialize(1,policyChange);
            Check(changed.Result == DecisionWriteResult.Revised &&
                  changed.Decision.Status == AttendanceState.Late,
                  "changed policy revises derived status");
            using (var db = DatabaseSetup.Open(path))
            {
                Check(Scalar(db,"SELECT COUNT(*) FROM AuditEvents;") == 3,
                      "audit created plus two policy revisions");
                Check(Scalar(db,"SELECT COUNT(*) FROM AttendanceEvents;") == 1,
                      "event remains immutable after reclassification");
                Check(Scalar(db,"SELECT COUNT(*) FROM AttendanceDecisions WHERE Status='Late';") == 1,
                      "single materialized decision");
            }
        }
        finally
        {
            Directory.Delete(root, recursive:true);
        }

        using var handler = new HebcalFakeHandler();
        using var http = new HttpClient(handler);
        var clock = new RuleTestClock(Utc(2026,11,9,9,0));
        var observations = new HebcalObservationClient(http,clock);
        var observed = await observations.FetchYearAsync(2026, israel:true);
        Check(observed.Items.Count == 2 && !observed.FromCache && !observed.IsStale,
              "Hebcal synthetic fixture");
        Check((await observations.FetchYearAsync(2026,true)).FromCache && handler.Calls==1,
              "fresh feed cached");
        clock.Advance(TimeSpan.FromHours(13));
        handler.Fail = true;
        var stale = await observations.FetchYearAsync(2026,true);
        Check(stale.FromCache && stale.IsStale,
              "calendar outage uses bounded stale observation cache");
        Status(engine.EvaluateEvent(Utc(2026,11,8,6,5)),AttendanceState.OnTime,
               "calendar service cannot change policy automatically");
        Check(handler.LastRequest?.Contains("i=on",StringComparison.Ordinal)==true,
              "Hebcal Israel jurisdiction explicit");

        Console.WriteLine("Rule engine smoke passed: study days, holidays, DST, overnight, " +
                          "absence cutoff, decision audit, optional cached Hebcal observations.");
    }

    private static long Scalar(SQLiteConnection db, string sql)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static void Check(bool condition, string label)
    {
        if (!condition)
            throw new InvalidOperationException("Rules smoke check failed: "+label);
    }

    private static void ExpectInvalid(Action operation, string label)
    {
        try
        {
            operation();
        }
        catch (ArgumentException)
        {
            return;
        }
        throw new InvalidOperationException("Expected invalid rules rejection: "+label);
    }

    private sealed class RuleTestClock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now=now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan span) => _now += span;
    }

    private sealed class HebcalFakeHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public bool Fail { get; set; }
        public string? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            LastRequest = request.RequestUri?.ToString();
            var response = new HttpResponseMessage(
                Fail ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"items":[{"date":"2026-11-09","title":"Synthetic festival","category":"holiday","yomtov":true},{"date":"2026-11-10","title":"Synthetic new moon","category":"roshchodesh"}]}""",
                    Encoding.UTF8,"application/json")
            };
            return Task.FromResult(response);
        }
    }
}
