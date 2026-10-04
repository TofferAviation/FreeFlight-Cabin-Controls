using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Input;
using System.Windows.Threading;
using FreeFlight.CabinControl.App.Infrastructure;

namespace FreeFlight.CabinControl.App.ViewModels;

/// <summary>
/// Presents Ember's cabin connectivity model. It intentionally models the
/// operational effect of passenger connectivity from Ember's live cabin state;
/// it does not claim to query Starlink, a satellite or a passenger device.
/// </summary>
public sealed class WifiViewModel : PageViewModel, IDisposable
{
    private const double StarlinkDownlinkCapacityMbps = 250d;
    private const double StarlinkUplinkCapacityMbps = 35d;

    private readonly PassengerFlowViewModel _passengers;
    private readonly DispatcherTimer _timer;
    private bool _isCabinWifiEnabled = true;
    private DateTimeOffset? _lastLinkCheck;
    private int _linkCheckCount;
    private int _onlinePassengerCount;
    private int _connectedDeviceCount;
    private int _streamingDeviceCount;
    private int _browsingDeviceCount;
    private int _messagingDeviceCount;
    private double _downlinkMbps;
    private double _uplinkMbps;
    private int _latencyMs;
    private double _linkQuality;

    public WifiViewModel(PassengerFlowViewModel passengers)
        : base("Cabin Wi-Fi", "Monitor the onboard Starlink connection and cabin internet demand for this flight.")
    {
        _passengers = passengers;
        ToggleCabinWifiCommand = new RelayCommand(_ => IsCabinWifiEnabled = !IsCabinWifiEnabled);
        RunLinkCheckCommand = new RelayCommand(_ => RunLinkCheck());
        _passengers.PropertyChanged += HandlePassengersPropertyChanged;
        _passengers.PassengerManifest.CollectionChanged += HandleManifestCollectionChanged;
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += HandleTimerTick;
        _timer.Start();
        RefreshTelemetry();
    }

    public ICommand ToggleCabinWifiCommand { get; }

    public ICommand RunLinkCheckCommand { get; }

    public string ProviderName => "Starlink Aviation";

    public string ProviderDescription => "LEO satellite cabin connectivity · Ember standard across all BAV airframes";

    public string AircraftLabel => string.IsNullOrWhiteSpace(_passengers.ImportedAircraftIcao)
        ? _passengers.SelectedCabinLayoutProfile.Name
        : _passengers.ImportedAircraftIcao;

    public string FlightLabel => string.IsNullOrWhiteSpace(_passengers.ImportedFlightNumber)
        ? "Awaiting website flight"
        : _passengers.ImportedFlightNumber;

    public string FlightPhaseLabel => _passengers.LiveFlightPhase.ToUpperInvariant();

    public bool IsCabinWifiEnabled
    {
        get => _isCabinWifiEnabled;
        private set
        {
            if (!SetProperty(ref _isCabinWifiEnabled, value)) return;
            RefreshTelemetry();
            OnPropertyChanged(nameof(NetworkStatusLabel));
            OnPropertyChanged(nameof(NetworkStatusDetail));
            OnPropertyChanged(nameof(WifiToggleLabel));
        }
    }

    public string NetworkStatusLabel => IsCabinWifiEnabled ? "CONNECTED" : "CABIN WI-FI PAUSED";

    public string NetworkStatusDetail => IsCabinWifiEnabled
        ? "Starlink terminal online · passenger network available"
        : "Cabin access paused locally · satellite terminal remains protected";

    public string WifiToggleLabel => IsCabinWifiEnabled ? "Pause cabin Wi-Fi" : "Enable cabin Wi-Fi";

    public int OnboardPassengerCount => _passengers.BoardedPassengerCount;

    public int OnlinePassengerCount => _onlinePassengerCount;

    public int ConnectedDeviceCount => _connectedDeviceCount;

    public int StreamingDeviceCount => _streamingDeviceCount;

    public int BrowsingDeviceCount => _browsingDeviceCount;

    public int MessagingDeviceCount => _messagingDeviceCount;

    public int IdleDeviceCount => Math.Max(0, ConnectedDeviceCount - StreamingDeviceCount - BrowsingDeviceCount - MessagingDeviceCount);

    public double DownlinkMbps => _downlinkMbps;

    public double UplinkMbps => _uplinkMbps;

    public double AvailableDownlinkMbps => Math.Max(0d, StarlinkDownlinkCapacityMbps - DownlinkMbps);

    public double DownlinkLoadPercent => Math.Clamp(DownlinkMbps * 100d / StarlinkDownlinkCapacityMbps, 0d, 100d);

    public double UplinkLoadPercent => Math.Clamp(UplinkMbps * 100d / StarlinkUplinkCapacityMbps, 0d, 100d);

    public int LatencyMs => _latencyMs;

    public double LinkQuality => _linkQuality;

    public string SatelliteLinkLabel => IsCabinWifiEnabled
        ? $"LEO relay acquired · {LatencyMs} ms round trip"
        : "LEO relay standing by";

    public string SatelliteBeamLabel => IsCabinWifiEnabled
        ? $"Starlink beam {207 + (_linkCheckCount % 13):000} · {LinkQuality:0}% link quality"
        : "No active passenger beam";

    public string PassengerUsageStatus => !_passengers.HasPassengerManifest
        ? "Load a website flight to model cabin passenger demand."
        : OnboardPassengerCount == 0
            ? "Cabin network is ready; passenger demand begins as passengers board."
            : $"{OnlinePassengerCount} passengers are currently online across {ConnectedDeviceCount} devices.";

    public string LinkCheckLabel => _lastLinkCheck is null
        ? "No manual link check in this session"
        : $"Last link check {FormatElapsed(DateTimeOffset.Now - _lastLinkCheck.Value)} ago · nominal";

    public string OperationsNotice => "Ember models cabin-network demand from the live passenger state. No Starlink account, satellite or passenger device is queried.";

    public void Dispose()
    {
        _timer.Stop();
        _timer.Tick -= HandleTimerTick;
        _passengers.PropertyChanged -= HandlePassengersPropertyChanged;
        _passengers.PassengerManifest.CollectionChanged -= HandleManifestCollectionChanged;
        GC.SuppressFinalize(this);
    }

    private void HandleTimerTick(object? sender, EventArgs e) => RefreshTelemetry();

    private void HandleManifestCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => RefreshTelemetry();

    private void HandlePassengersPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PassengerFlowViewModel.BoardedPassengerCount) or
            nameof(PassengerFlowViewModel.HasPassengerManifest) or
            nameof(PassengerFlowViewModel.ImportedAircraftIcao) or
            nameof(PassengerFlowViewModel.ImportedFlightNumber) or
            nameof(PassengerFlowViewModel.SelectedCabinLayoutProfile) or
            nameof(PassengerFlowViewModel.LiveFlightPhase))
        {
            RefreshTelemetry();
            OnPropertyChanged(nameof(AircraftLabel));
            OnPropertyChanged(nameof(FlightLabel));
            OnPropertyChanged(nameof(FlightPhaseLabel));
        }
    }

    private void RunLinkCheck()
    {
        _linkCheckCount++;
        _lastLinkCheck = DateTimeOffset.Now;
        RefreshTelemetry();
        OnPropertyChanged(nameof(LinkCheckLabel));
    }

    private void RefreshTelemetry()
    {
        var elapsedSeconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000d;
        var onboardPassengers = Math.Max(0, _passengers.BoardedPassengerCount);
        var phaseMultiplier = GetPhaseDemandMultiplier(_passengers.LiveFlightPhase);
        var demandWave = 0.46d + 0.12d * Math.Sin(elapsedSeconds / 13d) + 0.07d * Math.Cos(elapsedSeconds / 29d);

        _onlinePassengerCount = IsCabinWifiEnabled
            ? Math.Clamp((int)Math.Round(onboardPassengers * phaseMultiplier * demandWave), 0, onboardPassengers)
            : 0;
        _connectedDeviceCount = IsCabinWifiEnabled
            ? _onlinePassengerCount + (int)Math.Round(_onlinePassengerCount * 0.22d)
            : 0;
        _streamingDeviceCount = (int)Math.Round(_connectedDeviceCount * 0.27d);
        _browsingDeviceCount = (int)Math.Round(_connectedDeviceCount * 0.39d);
        _messagingDeviceCount = (int)Math.Round(_connectedDeviceCount * 0.22d);

        var trafficWave = 2.2d * Math.Sin(elapsedSeconds / 8d) + 1.1d * Math.Cos(elapsedSeconds / 17d);
        _downlinkMbps = IsCabinWifiEnabled
            ? Math.Clamp(8d + StreamingDeviceCount * 5.4d + BrowsingDeviceCount * 1.15d + MessagingDeviceCount * 0.2d + trafficWave, 0d, StarlinkDownlinkCapacityMbps)
            : 0d;
        _uplinkMbps = IsCabinWifiEnabled
            ? Math.Clamp(0.6d + StreamingDeviceCount * 0.32d + BrowsingDeviceCount * 0.12d + MessagingDeviceCount * 0.06d + trafficWave * 0.12d, 0d, StarlinkUplinkCapacityMbps)
            : 0d;
        _latencyMs = IsCabinWifiEnabled ? 41 + (int)Math.Round(5d * Math.Abs(Math.Sin(elapsedSeconds / 12d))) : 0;
        _linkQuality = IsCabinWifiEnabled ? 98d - 1.8d * Math.Abs(Math.Sin(elapsedSeconds / 20d)) : 0d;

        OnPropertyChanged(nameof(OnboardPassengerCount));
        OnPropertyChanged(nameof(OnlinePassengerCount));
        OnPropertyChanged(nameof(ConnectedDeviceCount));
        OnPropertyChanged(nameof(StreamingDeviceCount));
        OnPropertyChanged(nameof(BrowsingDeviceCount));
        OnPropertyChanged(nameof(MessagingDeviceCount));
        OnPropertyChanged(nameof(IdleDeviceCount));
        OnPropertyChanged(nameof(DownlinkMbps));
        OnPropertyChanged(nameof(UplinkMbps));
        OnPropertyChanged(nameof(AvailableDownlinkMbps));
        OnPropertyChanged(nameof(DownlinkLoadPercent));
        OnPropertyChanged(nameof(UplinkLoadPercent));
        OnPropertyChanged(nameof(LatencyMs));
        OnPropertyChanged(nameof(LinkQuality));
        OnPropertyChanged(nameof(SatelliteLinkLabel));
        OnPropertyChanged(nameof(SatelliteBeamLabel));
        OnPropertyChanged(nameof(PassengerUsageStatus));
        OnPropertyChanged(nameof(LinkCheckLabel));
    }

    private static double GetPhaseDemandMultiplier(string phase) =>
        phase.Contains("Cruise", StringComparison.OrdinalIgnoreCase) ? 1d :
        phase.Contains("Climb", StringComparison.OrdinalIgnoreCase) || phase.Contains("Descent", StringComparison.OrdinalIgnoreCase) ? 0.72d :
        phase.Contains("Board", StringComparison.OrdinalIgnoreCase) ? 0.36d :
        phase.Contains("Taxi", StringComparison.OrdinalIgnoreCase) || phase.Contains("Approach", StringComparison.OrdinalIgnoreCase) ? 0.18d :
        phase.Contains("Preflight", StringComparison.OrdinalIgnoreCase) ? 0d : 0.52d;

    private static string FormatElapsed(TimeSpan elapsed) => elapsed.TotalMinutes < 1d
        ? "just now"
        : elapsed.TotalHours < 1d
            ? $"{(int)elapsed.TotalMinutes} min"
            : $"{(int)elapsed.TotalHours}h {(int)elapsed.Minutes:00}m";
}
