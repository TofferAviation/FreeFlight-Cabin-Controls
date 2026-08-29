using FreeFlight.CabinControl.Core.Configuration;
using FreeFlight.CabinControl.Core.Integration;

namespace FreeFlight.CabinControl.App.Services;

public sealed class AutomaticSimulatorBridgeService : ISimulatorBridge
{
    private readonly AppSettings _settings;
    private readonly ISimulatorBridge _xPlane;
    private readonly ISimulatorBridge _msfs;
    private BridgeStatus _currentStatus = BridgeStatus.Offline;
    private string? _activeSimulator;
    private bool _disposed;

    public AutomaticSimulatorBridgeService(AppSettings settings, FileLogService log)
    {
        _settings = settings;
        _xPlane = new XPlaneWebApiBridgeService(settings, log);
        _msfs = new Msfs2024SimConnectBridgeService(settings, log);
        _xPlane.StatusChanged += status => HandleStatus("X-Plane", status);
        _msfs.StatusChanged += status => HandleStatus("MSFS", status);
        _xPlane.TelemetryReceived += snapshot => ForwardTelemetry("X-Plane", snapshot);
        _msfs.TelemetryReceived += snapshot => ForwardTelemetry("MSFS", snapshot);
    }

    public BridgeStatus CurrentStatus => _currentStatus;

    public TimeSpan? LastFrameAge
    {
        get
        {
            var ages = new[] { _xPlane.LastFrameAge, _msfs.LastFrameAge }.Where(age => age is not null).Select(age => age!.Value).ToArray();
            return ages.Length == 0 ? null : ages.Min();
        }
    }

    public event Action<BridgeStatus>? StatusChanged;
    public event Action<CabinTelemetrySnapshot>? TelemetryReceived;

    public void Start()
    {
        _xPlane.Start();
        _msfs.Start();
    }

    public void RequestReconnect()
    {
        _xPlane.RequestReconnect();
        _msfs.RequestReconnect();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _xPlane.Dispose();
        _msfs.Dispose();
        GC.SuppressFinalize(this);
    }

    private void HandleStatus(string source, BridgeStatus status)
    {
        if (status.State == BridgeConnectionState.Connected)
        {
            if (_activeSimulator is null || IsPreferred(source))
            {
                _activeSimulator = source;
                PublishStatus(status);
            }
            return;
        }

        if (string.Equals(_activeSimulator, source, StringComparison.Ordinal))
        {
            _activeSimulator = null;
        }

        if (_activeSimulator is null)
        {
            var other = source == "X-Plane" ? _msfs.CurrentStatus : _xPlane.CurrentStatus;
            PublishStatus(other.State == BridgeConnectionState.Connected ? other : BuildIdleStatus());
        }
    }

    private void ForwardTelemetry(string source, CabinTelemetrySnapshot snapshot)
    {
        if (_activeSimulator is null || IsPreferred(source))
        {
            _activeSimulator = source;
            PublishStatus(source == "X-Plane" ? _xPlane.CurrentStatus : _msfs.CurrentStatus);
        }

        if (string.Equals(_activeSimulator, source, StringComparison.Ordinal))
        {
            TelemetryReceived?.Invoke(snapshot);
        }
    }

    private bool IsPreferred(string source) => _settings.PreferredSimulator switch
    {
        "X-Plane" => source == "X-Plane",
        "MSFS 2024" => source == "MSFS",
        _ => _activeSimulator is null || string.Equals(_activeSimulator, source, StringComparison.Ordinal)
    };

    private BridgeStatus BuildIdleStatus()
    {
        if (_settings.PreferredSimulator == "X-Plane")
        {
            return _xPlane.CurrentStatus;
        }

        if (_settings.PreferredSimulator == "MSFS 2024")
        {
            return _msfs.CurrentStatus;
        }

        var statuses = new[] { _xPlane.CurrentStatus, _msfs.CurrentStatus };
        if (statuses.Any(item => item.State == BridgeConnectionState.Connecting))
        {
            return new BridgeStatus(
                BridgeConnectionState.Connecting,
                "Searching for simulator",
                "Detecting aircraft",
                "Checking X-Plane Web API and MSFS 2024 SimConnect.");
        }

        var setupNeeded = statuses.FirstOrDefault(item => item.State == BridgeConnectionState.Incompatible);
        return setupNeeded ?? BridgeStatus.Offline;
    }

    private void PublishStatus(BridgeStatus status)
    {
        if (status == _currentStatus) return;
        _currentStatus = status;
        StatusChanged?.Invoke(status);
    }
}
