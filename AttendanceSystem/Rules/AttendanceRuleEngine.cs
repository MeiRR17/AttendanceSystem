using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AttendanceSystem.Rules;

/// <summary>
/// Deterministic, policy-driven attendance evaluator. No HTTP, database writes,
/// personal information, or implicit school-wide absence inference.
/// All event inputs must come from the authenticated gateway.
/// </summary>
public sealed class AttendanceRuleEngine
{
    private readonly InstitutionAttendancePolicy _policy;
    private readonly TimeZoneInfo _zone;
    private readonly Dictionary<DateOnly, DayOverride> _overrides;

    public AttendanceRuleEngine(InstitutionAttendancePolicy policy)
    {
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _policy.Validate();
        _zone = ResolveIsraelZone();
        _overrides = _policy.Overrides.ToDictionary(o => o.Date);
    }

    public string RuleVersion => _policy.RuleVersion;

    public AttendanceDecision EvaluateEvent(DateTimeOffset occurredAtUtc)
    {
        RequireUtc(occurredAtUtc);
        var local = TimeZoneInfo.ConvertTime(occurredAtUtc, _zone).DateTime;
        var eventDate = DateOnly.FromDateTime(local);

        // Overnight sessions belong to the calendar date when they START.
        // Compare local times after converting the actual UTC event; this handles
        // DST folds without making ambiguous local timestamps into fake UTC instants.
        foreach (var date in new[] { eventDate.AddDays(-1), eventDate })
        {
            if (!IsStudyDay(date))
                continue;

            var weekday = date.DayOfWeek;
            foreach (var slot in _policy.Sessions.Where(s => s.Day == weekday))
            {
                var start = date.ToDateTime(slot.StartLocal);
                var cutoff = date.ToDateTime(slot.CutoffLocal);
                if (cutoff <= start)
                    cutoff = cutoff.AddDays(1);

                if (local < start.AddMinutes(-slot.EarlyArrivalMinutes) || local > cutoff)
                    continue;

                var onTimeCutoff = start.AddMinutes(slot.GraceMinutes);
                return new AttendanceDecision(
                    local <= onTimeCutoff ? AttendanceState.OnTime : AttendanceState.Late,
                    date, slot.Name, RuleVersion);
            }
        }

        // Exceptions are institution-approved, NOT inferred from a generic
        // external Jewish-calendar event (minor observances != no-class days).
        if (_overrides.TryGetValue(eventDate, out var exception) &&
            exception.Mode == DayMode.Excused)
            return new AttendanceDecision(AttendanceState.Excused, eventDate, null, RuleVersion);

        return new AttendanceDecision(AttendanceState.Unclassified, eventDate, null, RuleVersion);
    }

    public AttendanceDecision EvaluateMissing(
        DateOnly sessionDate, string sessionName, DateTimeOffset asOfUtc)
    {
        RequireUtc(asOfUtc);
        if (string.IsNullOrWhiteSpace(sessionName))
            throw new ArgumentException("A configured class/session is required.", nameof(sessionName));

        if (_overrides.TryGetValue(sessionDate, out var exception) &&
            exception.Mode == DayMode.Excused)
            return new AttendanceDecision(AttendanceState.Excused, sessionDate, sessionName, RuleVersion);

        if (!IsStudyDay(sessionDate))
            return new AttendanceDecision(AttendanceState.Unclassified, sessionDate, sessionName, RuleVersion);

        var slot = _policy.Sessions.SingleOrDefault(s =>
            s.Day == sessionDate.DayOfWeek && s.Name == sessionName);
        if (slot is null)
            return new AttendanceDecision(AttendanceState.Unclassified, sessionDate, sessionName, RuleVersion);

        var end = sessionDate.ToDateTime(slot.CutoffLocal);
        if (slot.CutoffLocal <= slot.StartLocal)
            end = end.AddDays(1);

        // Never declare absence before the end of the entire permitted window.
        var nowLocal = TimeZoneInfo.ConvertTime(asOfUtc, _zone).DateTime;
        return new AttendanceDecision(
            nowLocal > end ? AttendanceState.Absent : AttendanceState.Unclassified,
            sessionDate, sessionName, RuleVersion);
    }

    private bool IsStudyDay(DateOnly date)
    {
        if (_overrides.TryGetValue(date, out var exception))
            return exception.Mode == DayMode.Study;
        return _policy.StudyDays.Contains(date.DayOfWeek);
    }

    private static void RequireUtc(DateTimeOffset timestamp)
    {
        if (timestamp.Offset != TimeSpan.Zero)
            throw new ArgumentException("Timestamp must be UTC, not an unspecified local wall time.");
    }

    private static TimeZoneInfo ResolveIsraelZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Asia/Jerusalem");
        }
        catch (TimeZoneNotFoundException)
        {
            // Windows NLS installations may not provide IANA IDs.
            return TimeZoneInfo.FindSystemTimeZoneById("Israel Standard Time");
        }
    }
}

public sealed class InstitutionAttendancePolicy
{
    public string RuleVersion { get; init; } = "";
    public string TimeZoneId { get; init; } = "Asia/Jerusalem";
    public List<DayOfWeek> StudyDays { get; init; } = [];
    public List<SessionRule> Sessions { get; init; } = [];
    public List<DayOverride> Overrides { get; init; } = [];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        Converters = { new JsonStringEnumConverter() },
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static InstitutionAttendancePolicy FromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > 65536)
            throw new ArgumentException("Policy JSON must be nonempty and at most 64KiB.", nameof(json));
        var policy = JsonSerializer.Deserialize<InstitutionAttendancePolicy>(json, JsonOptions)
                     ?? throw new ArgumentException("Empty policy JSON.", nameof(json));
        policy.Validate();
        return policy;
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(RuleVersion) || RuleVersion.Length > 64 ||
            !RuleVersion.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '.' or '_'))
            throw new ArgumentException("RuleVersion must be a stable opaque version string.");
        if (TimeZoneId != "Asia/Jerusalem" && TimeZoneId != "Israel Standard Time")
            throw new ArgumentException("Only the configured Israel timezone is supported in v0.5.");
        if (StudyDays is null || Sessions is null || Overrides is null ||
            StudyDays.Count > 7 || StudyDays.Distinct().Count() != StudyDays.Count)
            throw new ArgumentException("Duplicate/invalid study weekdays.");
        if (Sessions.Count == 0 || Sessions.Count > 50 || Overrides.Count > 1000)
            throw new ArgumentException("Set a finite, explicit session schedule.");
        if (Overrides.GroupBy(o => o.Date).Any(g => g.Count() > 1))
            throw new ArgumentException("Conflicting overrides on the same date.");

        foreach (var rule in Sessions)
        {
            if (string.IsNullOrWhiteSpace(rule.Name) || rule.Name.Length > 80 ||
                rule.GraceMinutes < 0 || rule.GraceMinutes > 120 ||
                rule.EarlyArrivalMinutes < 0 || rule.EarlyArrivalMinutes > 120 ||
                rule.StartLocal == rule.CutoffLocal)
                throw new ArgumentException("Invalid session name, cutoff, grace or early window.");
        }
        if (Sessions.GroupBy(s => new { s.Day, s.Name }).Any(g => g.Count() > 1))
            throw new ArgumentException("Duplicate session names for a weekday.");

        // Ambiguous overlapping windows could label the same event twice: reject.
        foreach (var weekday in Enum.GetValues<DayOfWeek>())
        {
            var windows = Sessions.Where(s => s.Day == weekday)
                .Select(s => new
                {
                    Start = (int)s.StartLocal.ToTimeSpan().TotalMinutes - s.EarlyArrivalMinutes,
                    End = (int)s.CutoffLocal.ToTimeSpan().TotalMinutes +
                          (s.CutoffLocal <= s.StartLocal ? 1440 : 0)
                }).OrderBy(s => s.Start).ToList();
            for (var i = 1; i < windows.Count; i++)
                if (windows[i].Start <= windows[i - 1].End)
                    throw new ArgumentException("Overlapping session windows.");
        }
        // Also prevent weekday and previous day overnight overlap.
        foreach (var weekday in Enum.GetValues<DayOfWeek>())
        {
            var preceding = (DayOfWeek)(((int)weekday + 6) % 7);
            var yesterdayEnds = Sessions.Where(s => s.Day == preceding &&
                s.CutoffLocal <= s.StartLocal)
                .Select(s => (int)s.CutoffLocal.ToTimeSpan().TotalMinutes)
                .DefaultIfEmpty(-1).Max();
            var earliestToday = Sessions.Where(s => s.Day == weekday)
                .Select(s => (int)s.StartLocal.ToTimeSpan().TotalMinutes - s.EarlyArrivalMinutes)
                .DefaultIfEmpty(1441).Min();
            if (yesterdayEnds >= earliestToday)
                throw new ArgumentException("Overnight slot overlaps the following day.");
        }
    }
}

public sealed class SessionRule
{
    public DayOfWeek Day { get; init; }
    public string Name { get; init; } = "";
    public TimeOnly StartLocal { get; init; }
    public TimeOnly CutoffLocal { get; init; }
    public int GraceMinutes { get; init; }
    public int EarlyArrivalMinutes { get; init; } = 20;
}

public sealed class DayOverride
{
    public DateOnly Date { get; init; }
    public DayMode Mode { get; init; }
    public string Reason { get; init; } = "";
}

public enum DayMode { Study, NoStudy, Excused }

public enum AttendanceState { OnTime, Late, Absent, Excused, Unclassified, Invalid }

public sealed record AttendanceDecision(
    AttendanceState Status, DateOnly SessionDate, string? SessionName, string RuleVersion);
