using System;

namespace AttendanceSystem
{
    class Program
    {
        static void Main(string[] args)
        {
            DatabaseSetup.InitializeDatabase();

            var realandSdk = new RealandSDK();
            realandSdk.InitializeWebSocketServer("2012");

            // Do not send demonstration WhatsApp messages at startup.
            // Notifications will be driven by validated attendance decisions
            // after consent and delivery safeguards have been implemented.
            Console.WriteLine("Attendance prototype started. Press any key to exit...");
            Console.ReadKey();
        }
    }
}
