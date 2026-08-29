namespace FreeFlight.CabinControl.Core.Integration;

public enum BridgeConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Incompatible
}

public sealed record BridgeStatus(
    BridgeConnectionState State,
    string Simulator,
    string Aircraft,
    string Detail)
{
    public static BridgeStatus Offline { get; } = new(
        BridgeConnectionState.Disconnected,
        "No simulator connected",
        "No aircraft detected",
        "Searching for X-Plane Web API and MSFS 2024 SimConnect.");

    public static BridgeStatus XPlaneOffline { get; } = new(
        BridgeConnectionState.Disconnected,
        "X-Plane not connected",
        "No aircraft detected",
        "Waiting for the local X-Plane Web API.");
}

public sealed record CabinTelemetrySnapshot(
    DateTimeOffset Timestamp,
    string FlightPhase,
    double AltitudeFeet,
    bool OnGround,
    bool SeatbeltSignOn,
    IReadOnlyDictionary<string, double> Signals);

public interface ISimulatorBridge : IDisposable
{
    BridgeStatus CurrentStatus { get; }

    TimeSpan? LastFrameAge { get; }

    event Action<BridgeStatus>? StatusChanged;

    event Action<CabinTelemetrySnapshot>? TelemetryReceived;

    void Start();

    void RequestReconnect();
}
