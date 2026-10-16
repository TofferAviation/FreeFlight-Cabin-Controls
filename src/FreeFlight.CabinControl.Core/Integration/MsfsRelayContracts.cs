namespace FreeFlight.CabinControl.Core.Integration;

/// <summary>
/// Wire contracts for Ember's opt-in MSFS relay. The relay runs only on the
/// Windows computer hosting Microsoft Flight Simulator; desktop clients pair
/// with it over a certificate-pinned HTTPS connection on the pilot's LAN.
/// </summary>
public static class MsfsRelayProtocol
{
    public const string CurrentVersion = "v1";
    public const string PairPath = "/v1/pair";
    public const string StatePath = "/v1/state";
    public const string InfoPath = "/v1/info";
}

public sealed record RemoteMsfsRelayInfo(
    string ProtocolVersion,
    string RelayName,
    string CertificateFingerprint,
    DateTimeOffset StartedAt);

public sealed record RemoteMsfsRelayPairRequest(string PairingCode, string ClientName);

public sealed record RemoteMsfsRelayPairResponse(string AccessToken, DateTimeOffset ExpiresAt);

public sealed record RemoteMsfsRelayState(
    BridgeStatus Status,
    CabinTelemetrySnapshot? Telemetry,
    DateTimeOffset UpdatedAt);
