using FreeFlight.CabinControl.App.Infrastructure;
using FreeFlight.CabinControl.Core.Integration;

namespace FreeFlight.CabinControl.App.ViewModels;

public sealed class SharedStatusViewModel : ObservableObject
{
    private bool _isConnected;
    private string _connectionLabel = "NO SIMULATOR CONNECTED";
    private string _connectionSource = "Auto-detecting simulators";
    private string _connectionDetail = "Searching for X-Plane Web API and MSFS 2024 SimConnect.";
    private string _connectionColor = "#FF9F43";
    private string _simulatorName = "No simulator";
    private string _telemetrySourceLabel = "Simulator telemetry";
    private string _flightPhase = "No live telemetry";
    private DateTimeOffset? _lastTelemetryAt;

    public bool IsConnected
    {
        get => _isConnected;
        set => SetProperty(ref _isConnected, value);
    }

    public string ConnectionLabel
    {
        get => _connectionLabel;
        set => SetProperty(ref _connectionLabel, value);
    }

    public string ConnectionDetail
    {
        get => _connectionDetail;
        set => SetProperty(ref _connectionDetail, value);
    }

    public string ConnectionSource
    {
        get => _connectionSource;
        private set => SetProperty(ref _connectionSource, value);
    }

    public string SimulatorName
    {
        get => _simulatorName;
        private set => SetProperty(ref _simulatorName, value);
    }

    public string TelemetrySourceLabel
    {
        get => _telemetrySourceLabel;
        private set => SetProperty(ref _telemetrySourceLabel, value);
    }

    public string ConnectionColor
    {
        get => _connectionColor;
        private set => SetProperty(ref _connectionColor, value);
    }

    public string FlightPhase
    {
        get => _flightPhase;
        private set => SetProperty(ref _flightPhase, value);
    }

    public DateTimeOffset? LastTelemetryAt
    {
        get => _lastTelemetryAt;
        private set => SetProperty(ref _lastTelemetryAt, value);
    }

    public void ApplyBridgeStatus(BridgeStatus status)
    {
        var source = ResolveSource(status.Simulator);
        var isXPlane = source == "X-Plane";
        var isMsfs = source == "MSFS 2024";
        var simulatorLabel = isXPlane ? "X-PLANE" : isMsfs ? "MSFS 2024" : "SIMULATOR";

        IsConnected = status.State == BridgeConnectionState.Connected;
        ConnectionLabel = status.State switch
        {
            BridgeConnectionState.Connected => $"{simulatorLabel} CONNECTED",
            BridgeConnectionState.Connecting when isXPlane || isMsfs => $"{simulatorLabel} CONNECTING",
            BridgeConnectionState.Connecting => "SEARCHING FOR SIMULATOR",
            BridgeConnectionState.Incompatible when isXPlane || isMsfs => $"{simulatorLabel} SETUP NEEDED",
            BridgeConnectionState.Incompatible => "SIMULATOR SETUP NEEDED",
            _ when isXPlane || isMsfs => $"{simulatorLabel} DISCONNECTED",
            _ => "NO SIMULATOR CONNECTED"
        };
        ConnectionColor = status.State switch
        {
            BridgeConnectionState.Connected => "#58E68A",
            BridgeConnectionState.Connecting => "#49A5FF",
            BridgeConnectionState.Incompatible => "#FFCC4D",
            _ => "#FF9F43"
        };
        SimulatorName = source;
        ConnectionSource = isXPlane
            ? "X-Plane Web API"
            : isMsfs
                ? "MSFS 2024 · SimConnect"
                : "Auto-detecting simulators";
        TelemetrySourceLabel = isXPlane
            ? "X-Plane telemetry"
            : isMsfs
                ? "MSFS telemetry"
                : "Simulator telemetry";
        ConnectionDetail = status.State == BridgeConnectionState.Connected
            ? $"{status.Aircraft} · {status.Detail}"
            : status.Detail;
    }

    public void ApplyTelemetry(CabinTelemetrySnapshot snapshot)
    {
        FlightPhase = snapshot.FlightPhase;
        LastTelemetryAt = snapshot.Timestamp;
    }

    private static string ResolveSource(string simulator)
    {
        if (simulator.Contains("X-Plane", StringComparison.OrdinalIgnoreCase))
        {
            return "X-Plane";
        }

        if (simulator.Contains("MSFS", StringComparison.OrdinalIgnoreCase) ||
            simulator.Contains("Microsoft Flight Simulator", StringComparison.OrdinalIgnoreCase))
        {
            return "MSFS 2024";
        }

        return "No simulator";
    }
}
