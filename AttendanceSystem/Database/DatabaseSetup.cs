using System;
using System.Data.SQLite;

public static class DatabaseSetup
{
    public static void InitializeDatabase()
    {
        using (SQLiteConnection conn = new SQLiteConnection("Data Source=attendance.db;Version=3;"))
        {
            conn.Open();

            string createStudentsTable = @"
            CREATE TABLE IF NOT EXISTS Students (
                StudentID TEXT PRIMARY KEY,
                Name TEXT,
                PhoneNumber TEXT
            );";

            string createAttendanceTable = @"
            CREATE TABLE IF NOT EXISTS Attendance (
                StudentID TEXT,
                Date TEXT,
                Time TEXT,
                FirstLessonStatus TEXT,
                SecondLessonStatus TEXT
            );";

            string createHolidaysTable = @"
            CREATE TABLE IF NOT EXISTS Holidays (
                Date TEXT PRIMARY KEY
            );";

            using (SQLiteCommand command = new SQLiteCommand(createStudentsTable, conn))
            {
                command.ExecuteNonQuery();
            }
            using (SQLiteCommand command = new SQLiteCommand(createAttendanceTable, conn))
            {
                command.ExecuteNonQuery();
            }
            using (SQLiteCommand command = new SQLiteCommand(createHolidaysTable, conn))
            {
                command.ExecuteNonQuery();
            }
        }
    }
}
