using System;
using SuperSocket.WebSocket;
using SuperSocket.SocketBase;
using SuperSocket.SocketBase.Config;
using System.Xml;
using System.Data.SQLite;

public class WebSocketServerSetup
{
    private WebSocketServer wsServer;

    public string WebSocketServer_Start(string port)
    {
        string operationMsg = "";
        bool bool_status = false;

        if (string.IsNullOrEmpty(port))
        {
            return "Please input Port!";
        }

        int int_port = Convert.ToInt32(port);

        ServerConfig serverConfig = new ServerConfig
        {
            Port = int_port,
            MaxConnectionNumber = 50,
            SyncSend = true,
            MaxRequestLength = 40000 + 996 * 10 + 2048,
            SendBufferSize = 40000 + 996 * 10 + 2048,
            ReceiveBufferSize = 40000 + 996 * 10 + 2048
        };

        wsServer = new WebSocketServer();

        if (!wsServer.Setup(serverConfig))
        {
            return "Failed to setup!";
        }

        if (!wsServer.Start())
        {
            return "Failed to start!";
        }

        wsServer.NewMessageReceived += new SessionHandler<WebSocketSession, string>(OnMessageReceived);

        bool_status = true;
        return "WebSocket server started.";
    }

    private void OnMessageReceived(WebSocketSession session, string message)
    {
        // Print received message to the console
        Console.WriteLine("Raw message received: " + message);

        try
        {
            var xmlDoc = new XmlDocument();
            xmlDoc.LoadXml(message);
            var root = xmlDoc.DocumentElement;

            if (root != null)
            {
                Console.WriteLine("Parsed XML Document: " + root.OuterXml);
                Console.WriteLine("Message type: " + root.Name);
                if (root.Name == "TimeLog")
                {
                    HandleTimeLog(root);
                }
                else if (root.Name == "AdminLog")
                {
                    HandleAdminLog(root);
                }
                else
                {
                    Console.WriteLine("Unknown message type received: " + root.Name);
                }
            }
            else
            {
                Console.WriteLine("Error: Received message is not a valid XML document.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("Exception while processing message: " + ex.Message);
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
            Console.WriteLine($"Handling TimeLog for StudentID: {studentID}, Name: {studentName}, PhoneNumber: {studentPhoneNumber} at {timestamp}");
            StoreAttendanceData(studentID, studentName, studentPhoneNumber, timestamp);
        }
        else
        {
            Console.WriteLine("Error: StudentID, Name, or PhoneNumber is missing in the TimeLog message.");
        }
    }

    private void HandleAdminLog(XmlElement root)
    {
        Console.WriteLine("Handling AdminLog");
    }

    private void StoreAttendanceData(string studentID, string name, string phoneNumber, DateTime timestamp)
    {
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
}
