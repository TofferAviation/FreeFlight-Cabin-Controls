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
        "X-Plane not connected",
        "No aircraft detected",
        "The native bridge has not been installed yet.");
}

public sealed record CabinTelemetrySnapshot(
    DateTimeOffset Timestamp,
    string FlightPhase,
    double AltitudeFeet,
    bool OnGround,
    bool SeatbeltSignOn,
    IReadOnlyDictionary<string, double> Signals);
