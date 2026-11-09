namespace AttendanceSystem.Services;

/// <summary>
/// Legacy method intentionally fails closed. Calendar events are observations,
/// NOT automatic class-closure rules. Use HebcalObservationClient explicitly.
/// </summary>
[Obsolete("Use AttendanceSystem.Rules.HebcalObservationClient for opt-in, cached observations.")]
public static class HolidayFetcher
{
    public static Task<List<DateTime>> GetHebrewHolidays(int year) =>
        throw new NotSupportedException(
            "Unclassified Hebcal observances cannot be interpreted as automatic no-study dates.");
}
