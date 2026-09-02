using System.IO;
using System.Windows.Input;
using System.Windows.Threading;
using FreeFlight.CabinControl.App.Infrastructure;
using FreeFlight.CabinControl.App.Services;
using FreeFlight.CabinControl.Core.Configuration;
using FreeFlight.CabinControl.Core.Integration;
using FreeFlight.CabinControl.Core.Operations;
using FreeFlight.CabinControl.Core.Passengers;
using FreeFlight.CabinControl.Core.Persistence;

namespace FreeFlight.CabinControl.App.ViewModels;

public sealed class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly AppSettings _settings;
    private readonly ISimulatorBridge? _simulatorBridge;
    private readonly ISimulatorCabinControlBridge? _simulatorCabinControlBridge;
    private readonly FlightSessionStore? _flightSessionStore;
    private readonly IOperationsClock _operationsClock;
    private readonly DispatcherTimer _sessionSaveTimer;
    private CabinTelemetrySnapshot? _latestTelemetry;
    private int _telemetryDispatchPending;
    private PageViewModel _currentPage;
    private string _activePage = "Dashboard";

    public MainWindowViewModel(
        AppSettings settings,
        ISettingsStore settingsStore,
        string logDirectory,
        string? safetyVideoLocalFilePath = null,
        string? boardingMusicDirectory = null,
        ISimBriefClient? simBriefClient = null,
        IOperationsClock? operationsClock = null,
        IBoardingPassPrinterService? boardingPassPrinterService = null,
        ISimulatorBridge? simulatorBridge = null,
        FlightSessionStore? flightSessionStore = null,
        UpdateService? updateService = null,
        IVamsysOAuthService? vamsysService = null,
        string? settingsDirectory = null)
    {
        _settings = settings;
        _simulatorBridge = simulatorBridge;
        _simulatorCabinControlBridge = simulatorBridge as ISimulatorCabinControlBridge;
        _flightSessionStore = flightSessionStore;
        var resolvedOperationsClock = operationsClock ?? new LocalOperationsClock();
        _operationsClock = resolvedOperationsClock;
        Status = new SharedStatusViewModel();
        GateLogin = new GateLoginViewModel(settings, resolvedOperationsClock);
        var resolvedSettingsDirectory = settingsDirectory ??
                                        Path.GetDirectoryName(logDirectory) ??
                                        Path.GetTempPath();
        Airliners = new AirlinersViewModel(
            settings,
            settingsStore,
            Status,
            vamsysService ?? new VamsysOAuthService(settings, resolvedSettingsDirectory),
            resolvedSettingsDirectory);
        Passengers = new PassengerFlowViewModel(settings, Status, settingsStore, simBriefClient, resolvedOperationsClock);
        Passengers.DoorControlRequested += HandleDoorControlRequested;
        Passengers.SeatbeltControlRequested += HandleSeatbeltControlRequested;
        var savedFlight = _flightSessionStore?.Load();
        if (savedFlight is not null && savedFlight.Boarding.State != BoardingRunState.DeboardingComplete)
        {
            _ = Passengers.RestoreFlightSession(savedFlight);
        }
        Operations = new GateOperationsViewModel(
            settings,
            Passengers,
            resolvedOperationsClock,
            () => GateLogin.IsAuthenticated,
            boardingPassPrinterService);
        IportDcs = new IportDcsViewModel(Operations, GateLogin);
        Dashboard = Operations;
        CabinPanel = new CabinControlPanelViewModel(
            settings,
            settingsStore,
            Status,
            safetyVideoLocalFilePath,
            boardingMusicDirectory);
        Audio = new AudioViewModel(settings, settingsStore, Status, cabinPanel: CabinPanel);
        Performance = new PerformanceViewModel(settings, Status, logDirectory, simulatorBridge, settingsStore);
        Passengers.ApplyPerformanceMode(Performance.PerformanceMode);
        Performance.PropertyChanged += HandlePerformancePropertyChanged;
        Settings = new SettingsViewModel(settings, settingsStore, Status, simulatorBridge);
        Updates = new UpdatesViewModel(
            settings,
            settingsStore,
            updateService ?? new UpdateService(Path.GetDirectoryName(logDirectory) ?? logDirectory),
            PersistFlightSession);
        FlightLogger = new FlightLoggerViewModel();
        _currentPage = Dashboard;
        NavigateCommand = new RelayCommand(Navigate);
        _sessionSaveTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(15)
        };
        _sessionSaveTimer.Tick += HandleSessionSaveTick;
        _sessionSaveTimer.Start();
        GateLogin.SignedIn += HandleGateSignedIn;
        GateLogin.SignedOut += HandleGateSignedOut;
        if (_simulatorBridge is not null)
        {
            _simulatorBridge.StatusChanged += HandleBridgeStatusChanged;
            _simulatorBridge.TelemetryReceived += HandleTelemetryReceived;
            Status.ApplyBridgeStatus(_simulatorBridge.CurrentStatus);
            _simulatorBridge.Start();
        }
    }

    public SharedStatusViewModel Status { get; }

    public GateLoginViewModel GateLogin { get; }

    public GateOperationsViewModel Operations { get; }

    public IportDcsViewModel IportDcs { get; }

    public GateOperationsViewModel Dashboard { get; }

    public AirlinersViewModel Airliners { get; }

    public PassengerFlowViewModel Passengers { get; }

    public CabinControlPanelViewModel CabinPanel { get; }

    public AudioViewModel Audio { get; }

    public PerformanceViewModel Performance { get; }

    public SettingsViewModel Settings { get; }

    public UpdatesViewModel Updates { get; }

    public FlightLoggerViewModel FlightLogger { get; }

    public bool IsFlightInProgress =>
        Passengers.PassengerManifest.Count > 0 && !Passengers.IsFlightCompleted;

    public ICommand NavigateCommand { get; }

    public string ActivePage
    {
        get => _activePage;
        private set => SetProperty(ref _activePage, value);
    }

    public PageViewModel CurrentPage
    {
        get => _currentPage;
        private set => SetProperty(ref _currentPage, value);
    }

    public void Dispose()
    {
        _sessionSaveTimer.Stop();
        _sessionSaveTimer.Tick -= HandleSessionSaveTick;
        PersistFlightSession();
        GateLogin.SignedIn -= HandleGateSignedIn;
        GateLogin.SignedOut -= HandleGateSignedOut;
        Performance.PropertyChanged -= HandlePerformancePropertyChanged;
        Passengers.DoorControlRequested -= HandleDoorControlRequested;
        Passengers.SeatbeltControlRequested -= HandleSeatbeltControlRequested;
        if (_simulatorBridge is not null)
        {
            _simulatorBridge.StatusChanged -= HandleBridgeStatusChanged;
            _simulatorBridge.TelemetryReceived -= HandleTelemetryReceived;
            _simulatorBridge.Dispose();
        }
        GateLogin.Dispose();
        Airliners.Dispose();
        Audio.Dispose();
        IportDcs.Dispose();
        Operations.Dispose();
        Passengers.Dispose();
        Performance.Dispose();
        GC.SuppressFinalize(this);
    }

    private void HandleSessionSaveTick(object? sender, EventArgs e) => PersistFlightSession();

    private void PersistFlightSession()
    {
        if (_flightSessionStore is null)
        {
            return;
        }

        var snapshot = Passengers.CaptureFlightSession();
        _flightSessionStore.SaveOrClear(snapshot, Passengers.IsFlightCompleted);
    }

    private void Navigate(object? parameter)
    {
        if (parameter is not string destination)
        {
            return;
        }

        if (IsGateWorkspacePage(destination) && !GateLogin.IsAuthenticated)
        {
            CurrentPage = GateLogin;
            ActivePage = "GateLogin";
            return;
        }

        if (destination == "Passengers")
        {
            Passengers.ApplyCabinLayoutSelection(_settings.PassengerCabinLayoutId);
        }
        else if (destination == "Settings")
        {
            Settings.ApplyCabinLayoutSelection(_settings.PassengerCabinLayoutId);
        }

        Operations.ApplySettings();

        CurrentPage = destination switch
        {
            "GateLogin" => GateLogin,
            "GateDesk" => Operations,
            "PassengerManifest" => Operations,
            "BoardingPasses" => Operations,
            "IportDcs" => IportDcs,
            "Airliners" => Airliners,
            "Passengers" => Passengers,
            "CabinPanel" => CabinPanel,
            "Audio" => Audio,
            "Performance" => Performance,
            "Settings" => Settings,
            "FlightLogger" => FlightLogger,
            _ => Dashboard
        };
        ActivePage = destination;
    }

    private void HandleGateSignedIn(object? sender, EventArgs e)
    {
        Operations.ApplyGateAccessState();
        Navigate("GateDesk");
    }

    private void HandleGateSignedOut(object? sender, EventArgs e)
    {
        Operations.ApplyGateAccessState();
        CurrentPage = GateLogin;
        ActivePage = "GateLogin";
    }

    private void HandlePerformancePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PerformanceViewModel.PerformanceMode))
        {
            Passengers.ApplyPerformanceMode(Performance.PerformanceMode);
        }
    }

    private void HandleBridgeStatusChanged(BridgeStatus status) => DispatchToUi(() =>
        Status.ApplyBridgeStatus(status));

    private void HandleTelemetryReceived(CabinTelemetrySnapshot snapshot)
    {
        Interlocked.Exchange(ref _latestTelemetry, snapshot);
        if (Interlocked.Exchange(ref _telemetryDispatchPending, 1) != 0)
        {
            return;
        }

        DispatchToUi(DrainLatestTelemetry);
    }

    private void DrainLatestTelemetry()
    {
        var snapshot = Interlocked.Exchange(ref _latestTelemetry, null);
        Interlocked.Exchange(ref _telemetryDispatchPending, 0);
        if (snapshot is null)
        {
            return;
        }

        Status.ApplyTelemetry(snapshot);
        if (_operationsClock is LocalOperationsClock simulatorClock)
        {
            simulatorClock.ApplyTelemetry(snapshot, _simulatorBridge?.CurrentStatus.Simulator ?? string.Empty);
        }
        Passengers.ApplyCabinTelemetry(snapshot);
        Operations.ApplyCabinTelemetry(snapshot);
        CabinPanel.ApplyFlightTelemetry(
            snapshot,
            $"{Operations.DetectedAircraftIcao} {_simulatorBridge?.CurrentStatus.Aircraft}");
        if (!_settings.SyncXPlaneDoors)
        {
            return;
        }

        if (snapshot.Signals.TryGetValue("door_l1_ratio", out var l1DoorRatio))
        {
            Passengers.ApplySimulatorDoorState(BoardingDoor.L1, l1DoorRatio >= 0.5d);
        }

        if (snapshot.Signals.TryGetValue("door_l2_ratio", out var l2DoorRatio))
        {
            Passengers.ApplySimulatorDoorState(BoardingDoor.L2, l2DoorRatio >= 0.5d);
        }
    }

    private async void HandleDoorControlRequested(BoardingDoor door, bool isOpen)
    {
        if (_simulatorCabinControlBridge is null)
        {
            return;
        }

        await _simulatorCabinControlBridge.SetPassengerDoorOpenAsync((int)door + 1, isOpen);
    }

    private async void HandleSeatbeltControlRequested(bool isOn)
    {
        if (_simulatorCabinControlBridge is null)
        {
            return;
        }

        await _simulatorCabinControlBridge.SetSeatbeltSignAsync(isOn);
    }

    private static void DispatchToUi(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return;
        }

        _ = dispatcher.BeginInvoke(action);
    }

    private static bool IsGateWorkspacePage(string destination) => destination is
        "GateDesk" or "PassengerManifest" or "BoardingPasses" or "IportDcs";
}
