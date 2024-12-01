using AttendanceSystem.Services;
using System;

namespace AttendanceSystem
{
    class Program
    {
        static void Main(string[] args)
        {
            DatabaseSetup.CreateDatabase();

            var realandSdk = new RealandSDK();
            realandSdk.InitializeWebSocketServer("2012"); // Example port

            var notificationService = new NotificationService();
            notificationService.SendLateNotification("Meir Ben Zion Dvir", "12345678", "+972535321275", "12/12/2024");

            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
        }
    }
}
