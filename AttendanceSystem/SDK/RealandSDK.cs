using System;
using System.Xml;
using SuperSocket.WebSocket;
using SuperSocket.SocketBase;
using System.Collections.Generic;
using SuperSocket.SocketBase.Config;
using System.Data.SQLite;

public class RealandSDK
{
    private WebSocketServer wsServer;

    public void InitializeWebSocketServer(string port)
    {
        wsServer = new WebSocketServer();

        var serverConfig = new ServerConfig
        {
            Port = Convert.ToInt32(port),
            MaxConnectionNumber = 50,
            SyncSend = true
        };

        wsServer.Setup(serverConfig);
        wsServer.Start();
        wsServer.NewMessageReceived += OnMessageReceived;

        Console.WriteLine("WebSocket server initialized and started on port " + port);
    }

    private void OnMessageReceived(WebSocketSession session, string message)
    {
        var xmlDoc = new XmlDocument();
        xmlDoc.LoadXml(message);
        var root = xmlDoc.DocumentElement;

        switch (root.Name)
        {
            case "TimeLog":
                HandleTimeLog(root);
                break;
            case "AdminLog":
                HandleAdminLog(root);
                break;
        }
    }

    private void HandleTimeLog(XmlElement root)
    {
        var studentID = root.SelectSingleNode("StudentID")?.InnerText;
        var studentName = root.SelectSingleNode("Name")?.InnerText;
        var studentPhoneNumber = root.SelectSingleNode("PhoneNumber")?.InnerText;
        var timestamp = DateTime.Now;

        if (!string.IsNullOrEmpty(studentID) && !string.IsNullOrEmpty(studentName) && !string.IsNullOrEmpty(studentPhoneNumber))
        {
            Console.WriteLine($"Handling TimeLog for StudentID: {studentID} at {timestamp}");
            StoreAttendanceData(studentID, studentName, studentPhoneNumber, timestamp);
        }
        else
        {
            Console.WriteLine("Error: StudentID, Name, or PhoneNumber is missing in the TimeLog message.");
        }
    }

    private void HandleAdminLog(XmlElement root)
    {
        // Handle admin log messages
        Console.WriteLine("AdminLog received.");
    }

    private async void StoreAttendanceData(string studentID, string name, string phoneNumber, DateTime timestamp)
    {
        await HolidayChecker.InitializeHolidays();

        if (HolidayChecker.IsWeekendOrHoliday(timestamp))
        {
            Console.WriteLine("Today is a weekend or holiday. No attendance recorded.");
            return;
        }

        string status = timestamp.TimeOfDay <= new TimeSpan(8, 0, 0) ? "On Time" : "Late";

        using (SQLiteConnection conn = new SQLiteConnection("Data Source=attendance.db;Version=3;"))
        {
            conn.Open();
            string sql = "INSERT OR REPLACE INTO Attendance (StudentID, Name, PhoneNumber, Date, Time, Status) " +
                         "VALUES (@StudentID, @Name, @PhoneNumber, @Date, @Time, @Status)";
            using (SQLiteCommand command = new SQLiteCommand(sql, conn))
            {
                command.Parameters.AddWithValue("@StudentID", studentID);
                command.Parameters.AddWithValue("@Name", name);
                command.Parameters.AddWithValue("@PhoneNumber", phoneNumber);
                command.Parameters.AddWithValue("@Date", timestamp.Date);
                command.Parameters.AddWithValue("@Time", timestamp.TimeOfDay);
                command.Parameters.AddWithValue("@Status", status);
                command.ExecuteNonQuery();
            }
        }
        Console.WriteLine("Attendance data stored successfully for StudentID: " + studentID);
    }

    public void SyncUserData(List<UserData> users)
    {
        foreach (var user in users)
        {
            string userXml = GenerateUserXml(user);
            wsServer.GetAllSessions().FirstOrDefault()?.Send(userXml);
        }
    }

    private string GenerateUserXml(UserData user)
    {
        var xmlDoc = new XmlDocument();
        var userElement = xmlDoc.CreateElement("User");

        var idElement = xmlDoc.CreateElement("ID");
        idElement.InnerText = user.ID;
        userElement.AppendChild(idElement);

        var nameElement = xmlDoc.CreateElement("Name");
        nameElement.InnerText = user.Name;
        userElement.AppendChild(nameElement);

        var enabledElement = xmlDoc.CreateElement("Enabled");
        enabledElement.InnerText = user.Enabled ? "Yes" : "No";
        userElement.AppendChild(enabledElement);

        xmlDoc.AppendChild(userElement);
        return xmlDoc.OuterXml;
    }
}

public class UserData
{
    public string ID { get; set; }
    public string Name { get; set; }
    public bool Enabled { get; set; }
}
