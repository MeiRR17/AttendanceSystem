using AttendanceSystem.Runtime;
using System.Globalization;

namespace AttendanceSystem;

internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 1 && args[0] == "--self-test")
            {
                RuntimeOptions.RunSmokeChecks();
                return 0;
            }

            if (args.Length > 0 && (args.Length != 1 || args[0] != "--check-config"))
            {
                Console.Error.WriteLine("Usage: AttendanceSystem [--check-config|--self-test]");
                return 2;
            }

            var options = RuntimeOptions.Load();
            if (args.Length == 1)
            {
                Console.WriteLine("Runtime configuration valid; legacy gateway enabled: "
                                  + options.EnableLegacyGateway.ToString(CultureInfo.InvariantCulture)
                                  + "; port: " + options.LegacyPort.ToString(CultureInfo.InvariantCulture));
                return 0;
            }

            if (!options.EnableLegacyGateway)
            {
                Console.WriteLine("Safe mode: legacy scanner gateway disabled. No network listener, database write or notification.");
                Console.WriteLine("Use --self-test or --check-config for offline verification.");
                return 0;
            }

            // Explicitly opted-in legacy bridge. For loopback-only experiments.
            // Never attach real devices, production data or public-facing ports yet.
            DatabaseSetup.InitializeDatabase();
            var gateway = new RealandSDK();
            using var shutdownSignal = new ManualResetEventSlim(false);
            ConsoleCancelEventHandler stopOnCtrlC = (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                shutdownSignal.Set();
            };

            Console.CancelKeyPress += stopOnCtrlC;
            try
            {
                gateway.InitializeWebSocketServer(options.LegacyPort.ToString(CultureInfo.InvariantCulture));
                Console.WriteLine("Legacy gateway: loopback only, unverified SDK. Ctrl+C to stop.");
                shutdownSignal.Wait();
            }
            finally
            {
                gateway.StopServer();
                Console.CancelKeyPress -= stopOnCtrlC;
            }

            return 0;
        }
        catch (Exception ex)
        {
            // Error type only. Never echo scanner payload, identity or secrets.
            Console.Error.WriteLine("Startup failed: " + ex.GetType().Name);
            return 1;
        }
    }
}
