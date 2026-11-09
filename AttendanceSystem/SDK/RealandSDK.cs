using System;
using System.Xml;
using System.IO;
using System.Threading.Tasks;
using SuperSocket.WebSocket;
using SuperSocket.SocketBase;
using System.Collections.Generic;
using SuperSocket.SocketBase.Config;
using System.Data.SQLite;
using AttendanceSystem.Services; // Ensure this line is included

public class RealandSDK
{
    private WebSocketServer? wsServer;

    public void InitializeWebSocketServer(string port)
    {
        wsServer = new WebSocketServer();

        var serverConfig = new ServerConfig
        {
            Ip = "127.0.0.1", // Unauthenticated legacy gateway: loopback only.
            Port = Convert.ToInt32(port),
            MaxConnectionNumber = 10,
            MaxRequestLength = 65536,
            SyncSend = true
        };

        if (!wsServer.Setup(serverConfig))
            throw new InvalidOperationException("Legacy WebSocket setup failed.");
        wsServer.NewMessageReceived += OnMessageReceived;
        if (!wsServer.Start())
        {
            wsServer.NewMessageReceived -= OnMessageReceived;
            throw new InvalidOperationException("Legacy WebSocket start failed.");
        }
        Console.WriteLine("Legacy loopback WebSocket listening on port " + port);
    }

    public void StopServer()
    {
        if (wsServer is null)
            return;
        wsServer.NewMessageReceived -= OnMessageReceived;
        wsServer.Stop();
        wsServer = null;
    }

    private void OnMessageReceived(WebSocketSession session, string message)
    {
        if (string.IsNullOrEmpty(message) || message.Length > 65536)
        {
            Console.Error.WriteLine("Rejected scanner message: invalid size.");
            return;
        }
        try
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = 65536
            };
            using var input = new StringReader(message);
            using var reader = XmlReader.Create(input, settings);
            var xmlDoc = new XmlDocument { XmlResolver = null };
            xmlDoc.Load(reader);
            var root = xmlDoc.DocumentElement;
            if (root is null)
                return;
            switch (root.Name)
            {
                case "TimeLog":
                    HandleTimeLog(root);
                    break;
                case "AdminLog":
                    HandleAdminLog(root);
                    break;
                default:
                    Console.Error.WriteLine("Rejected scanner message: unknown type.");
                    break;
            }
        }
        catch (XmlException)
        {
            Console.Error.WriteLine("Rejected malformed scanner XML.");
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
            _ = StoreAttendanceWithErrorLoggingAsync(studentID, studentName, studentPhoneNumber, timestamp);
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

    private async Task StoreAttendanceWithErrorLoggingAsync(string studentID, string name, string phoneNumber, DateTime timestamp)
    {
        try
        {
            await StoreAttendanceDataAsync(studentID, name, phoneNumber, timestamp);
        }
        catch (Exception ex)
        {
            // Legacy pathway has no durable retry or ACK. Do not report success on error.
            Console.Error.WriteLine("Legacy attendance write failed: " + ex.GetType().Name);
        }
    }

    private async Task StoreAttendanceDataAsync(string studentID, string name, string phoneNumber, DateTime timestamp)
    {
        await HolidayChecker.InitializeHolidays(); // Correct namespace reference

        if (HolidayChecker.IsWeekendOrHoliday(timestamp)) // Correct namespace reference
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
        Console.WriteLine("Legacy attendance write completed.");
    }

    public void SyncUserData(List<UserData> users)
    {
        foreach (var user in users)
        {
            string userXml = GenerateUserXml(user);
            wsServer?.GetAllSessions().FirstOrDefault()?.Send(userXml);
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
    public string ID { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool Enabled { get; set; }
}
