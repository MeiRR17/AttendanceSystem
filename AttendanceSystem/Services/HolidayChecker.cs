namespace AttendanceSystem.Services
{
    public static class HolidayChecker
    {
        private static List<DateTime> holidays = new List<DateTime>();

        public static async Task InitializeHolidays()
        {
            int currentYear = DateTime.Now.Year;
            holidays = await HolidayFetcher.GetHebrewHolidays(currentYear);
        }

        public static bool IsWeekendOrHoliday(DateTime date)
        {
            if (date.DayOfWeek == DayOfWeek.Friday || date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday)
            {
                return true;
            }
            if (holidays.Contains(date.Date))
            {
                return true;
            }
            return false;
        }
    }
}
