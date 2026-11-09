namespace AttendanceSystem.Services;

/// <summary>
/// Retired unsafe prototype service: Sunday and arbitrary Hebcal observances
/// were incorrectly classified as non-study days. Use policy versioning and
/// explicit authorized date overrides through AttendanceRuleEngine.
/// </summary>
[Obsolete("Use AttendanceSystem.Rules.AttendanceRuleEngine with approved policy.")]
public static class HolidayChecker
{
    public static Task InitializeHolidays() => Task.CompletedTask;

    public static bool IsWeekendOrHoliday(DateTime date) =>
        throw new NotSupportedException(
            "Legacy holiday logic was removed: study days must be configured by the institution.");
}
