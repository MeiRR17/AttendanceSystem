/// <summary>
/// The historical second SuperSocket listener bypassed device authentication and
/// the new idempotent persistence layer. It is disabled. The controlled
/// loopback RealandSDK is the only experimental legacy entry point.
/// </summary>
[Obsolete("Duplicate unauthenticated WebSocket server disabled; see v0.4.0 gateway roadmap.")]
public sealed class WebSocketServerSetup
{
    public string WebSocketServer_Start(string port) =>
        "Disabled: unauthenticated duplicate legacy gateway. Use the controlled loopback adapter.";
}
