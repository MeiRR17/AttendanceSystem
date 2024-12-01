using System.Data.SQLite;

public static class DatabaseSetup
{
    public static void CreateDatabase()
    {
        using (SQLiteConnection conn = new SQLiteConnection("Data Source=attendance.db;Version=3;"))
        {
            conn.Open();

            string sql = "CREATE TABLE IF NOT EXISTS Attendance (" +
                         "StudentID TEXT PRIMARY KEY," +
                         "Name TEXT," +
                         "PhoneNumber TEXT," +
                         "Date TEXT," +
                         "Time TEXT," +
                         "Status TEXT)";
            SQLiteCommand command = new SQLiteCommand(sql, conn);
            command.ExecuteNonQuery();
        }
    }
}
