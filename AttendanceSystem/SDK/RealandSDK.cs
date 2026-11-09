using System;
using System.Xml;
using System.IO;
using SuperSocket.WebSocket;
using SuperSocket.SocketBase;
using System.Collections.Generic;
using SuperSocket.SocketBase.Config;

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
        // Unauthenticated and no vendor-verified stable event key.
        // Never persist these messages until a trusted v0.4.0 adapter is installed.
        Console.WriteLine("Legacy scanner event rejected: trusted ingest is not enabled.");
    }

    private void HandleAdminLog(XmlElement root)
    {
        Console.WriteLine("Legacy administrative message ignored.");
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
