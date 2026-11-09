using System.Globalization;

namespace AttendanceSystem.Runtime;

/// <summary>
/// Safe-by-default runtime configuration. The 2018 SuperSocket bridge is a legacy,
/// unauthenticated adapter and is disabled unless explicitly acknowledged.
/// </summary>
public sealed record RuntimeOptions(bool EnableLegacyGateway, int LegacyPort)
{
    private const string EnabledKey = "ATTENDANCE_ENABLE_LEGACY_GATEWAY";
    private const string PortKey = "ATTENDANCE_LEGACY_PORT";
    private const string AcknowledgementKey = "ATTENDANCE_LEGACY_RISK_ACK";
    private const string RequiredAcknowledgement = "I_UNDERSTAND_LEGACY_GATEWAY_IS_UNAUTHENTICATED";

    public static RuntimeOptions Load() => Parse(Environment.GetEnvironmentVariable);

    public static RuntimeOptions Parse(Func<string, string?> get)
    {
        ArgumentNullException.ThrowIfNull(get);

        var enabledValue = (get(EnabledKey) ?? "false").Trim();
        if (!bool.TryParse(enabledValue, out var enabled))
            throw new InvalidOperationException(EnabledKey + " must be true or false.");

        var portValue = (get(PortKey) ?? "2012").Trim();
        if (!int.TryParse(portValue, NumberStyles.None, CultureInfo.InvariantCulture, out var port)
            || port < 1024 || port > 65535)
            throw new InvalidOperationException(PortKey + " must be a TCP port from 1024 to 65535.");

        if (enabled && !string.Equals(get(AcknowledgementKey), RequiredAcknowledgement, StringComparison.Ordinal))
            throw new InvalidOperationException(
                "Legacy gateway is disabled until its unauthenticated protocol risk is explicitly acknowledged. " +
                "See docs/SDK_COMPATIBILITY.md.");

        return new RuntimeOptions(enabled, port);
    }

    /// <summary>
    /// Executable, dependency-free smoke checks for configuration and fail-closed behavior.
    /// This does NOT validate the vendor SDK, database or hardware.
    /// </summary>
    public static void RunSmokeChecks()
    {
        static string? Empty(string _) => null;
        var baseline = Parse(Empty);
        Check(!baseline.EnableLegacyGateway && baseline.LegacyPort == 2012, "default must be disabled");

        var invalidPort = new Dictionary<string, string?>
        {
            [PortKey] = "99999"
        };
        CheckThrows(() => Parse(k => invalidPort.GetValueOrDefault(k)), "invalid port");

        var invalidBoolean = new Dictionary<string, string?>
        {
            [EnabledKey] = "yes"
        };
        CheckThrows(() => Parse(k => invalidBoolean.GetValueOrDefault(k)), "invalid boolean");

        var noRiskAcknowledgement = new Dictionary<string, string?>
        {
            [EnabledKey] = "true"
        };
        CheckThrows(() => Parse(k => noRiskAcknowledgement.GetValueOrDefault(k)), "missing acknowledgement");

        var acknowledged = new Dictionary<string, string?>
        {
            [EnabledKey] = "true",
            [PortKey] = "2013",
            [AcknowledgementKey] = RequiredAcknowledgement
        };
        var optedIn = Parse(k => acknowledged.GetValueOrDefault(k));
        Check(optedIn.EnableLegacyGateway && optedIn.LegacyPort == 2013, "explicit opt-in");

        Console.WriteLine("RuntimeOptions smoke checks passed (5 cases).");
    }

    private static void Check(bool condition, string scenario)
    {
        if (!condition)
            throw new InvalidOperationException("Smoke check failed: " + scenario);
    }

    private static void CheckThrows(Action action, string scenario)
    {
        try
        {
            action();
        }
        catch (InvalidOperationException)
        {
            return;
        }
        throw new InvalidOperationException("Smoke check expected failure: " + scenario);
    }
}
