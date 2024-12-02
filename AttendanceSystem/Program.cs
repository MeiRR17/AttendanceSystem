using AttendanceSystem.Services;
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

            var notificationService = new NotificationService();
            notificationService.SendLateNotification("מאיר בן ציון דביר", "12345678", "+972535321275", DateTime.Now);

            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
        }
    }
}
