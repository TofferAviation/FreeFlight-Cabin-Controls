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
    private bool _hasObservedEnginesRunning;
    private bool _hasObservedDeparture;
    private bool _wasAirborne;
    private bool _landingAssessmentReported;
    private bool _acarsFlightStartRequested;
    private bool _fleetFlightStartRequested;
    private bool _fleetFlightCompletionInProgress;
    private bool _acarsTelemetryInFlight;
    private PendingAcarsTelemetry? _pendingAcarsTelemetry;
    private int? _touchdownFpm;
    private string? _fleetAircraftIdForCurrentFlight;
    private bool _preserveFlightForUpdate;
    private DateTimeOffset? _engineShutdownCandidateSince;
    private DateTimeOffset? _lastAcarsTelemetrySentAt;
    private DateTimeOffset? _lastSimulatorTelemetryReceivedAt;
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
        Catering = new CateringViewModel(Passengers, resolvedSettingsDirectory);
        Passengers.DoorControlRequested += HandleDoorControlRequested;
        Passengers.SeatbeltControlRequested += HandleSeatbeltControlRequested;
        Passengers.FlightUnloaded += HandleFlightUnloaded;
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
            boardingPassPrinterService,
            simulatorBridge as ISimulatorJetwayControlBridge);
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
            PrepareFlightForUpdate,
            CancelFlightUpdateShutdown);
        FlightLogger = new FlightLoggerViewModel();
        Account = new CabinAccountViewModel(
            settings,
            new FleetApiClient(),
            new BavAccountSessionStore(resolvedSettingsDirectory));
        IportDcs = new IportDcsViewModel(Operations, GateLogin, Account);
        Fleet = new FleetViewModel(
            settings,
            new FleetApiClient(),
            () => Account.Session,
            Account.GetCurrentFlightContext);
        Account.SessionChanged += HandleBavAccountSessionChanged;
        Account.WebsiteFlightAssignmentRefreshed += HandleWebsiteFlightAssignmentRefreshed;
        Passengers.PropertyChanged += HandlePassengerFlightPropertyChanged;
        // A BAV account is the front door to Ember. The navigator cannot
        // enter operational pages until the encrypted local session has been
        // verified with the live BAV website.
        _currentPage = Account;
        _activePage = "CabinAccount";
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

        _ = RestoreBavAccountSessionAsync();
    }

    public SharedStatusViewModel Status { get; }

    public GateLoginViewModel GateLogin { get; }

    public GateOperationsViewModel Operations { get; }

    public IportDcsViewModel IportDcs { get; }

    public GateOperationsViewModel Dashboard { get; }

    public AirlinersViewModel Airliners { get; }

    public PassengerFlowViewModel Passengers { get; }

    public CateringViewModel Catering { get; }

    public CabinControlPanelViewModel CabinPanel { get; }

    public AudioViewModel Audio { get; }

    public PerformanceViewModel Performance { get; }

    public SettingsViewModel Settings { get; }

    public UpdatesViewModel Updates { get; }

    public FlightLoggerViewModel FlightLogger { get; }

    public CabinAccountViewModel Account { get; }

    public FleetViewModel Fleet { get; }

    /// <summary>Controls whether the operational Ember shell may be shown.</summary>
    public bool IsBavAuthenticated => Account.IsAuthenticated;

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
        if (_preserveFlightForUpdate)
        {
            PersistFlightSession();
        }
        else
        {
            _flightSessionStore?.Clear();
        }
        GateLogin.SignedIn -= HandleGateSignedIn;
        GateLogin.SignedOut -= HandleGateSignedOut;
        Account.SessionChanged -= HandleBavAccountSessionChanged;
        Account.WebsiteFlightAssignmentRefreshed -= HandleWebsiteFlightAssignmentRefreshed;
        Passengers.PropertyChanged -= HandlePassengerFlightPropertyChanged;
        Performance.PropertyChanged -= HandlePerformancePropertyChanged;
        Passengers.DoorControlRequested -= HandleDoorControlRequested;
        Passengers.SeatbeltControlRequested -= HandleSeatbeltControlRequested;
        Passengers.FlightUnloaded -= HandleFlightUnloaded;
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
        Catering.Dispose();
        Performance.Dispose();
        Account.Dispose();
        Fleet.Dispose();
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

        if (!Account.IsAuthenticated && destination != "CabinAccount")
        {
            CurrentPage = Account;
            ActivePage = "CabinAccount";
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
            "Catering" => Catering,
            "OnboardMenu" => Catering,
            "CabinPanel" => CabinPanel,
            "Audio" => Audio,
            "Performance" => Performance,
            "Settings" => Settings,
            "FlightLogger" => FlightLogger,
            "CabinAccount" => Account,
            "Fleet" => Fleet,
            _ => Dashboard
        };
        ActivePage = destination;
    }

    /// <summary>
    /// Keeps the BAV account page as the only entry point to Ember whenever
    /// there is no verified account session. This is deliberately invoked by
    /// the window after it has loaded as a defence against startup UI events
    /// selecting a navigation item before a pilot has authenticated.
    /// </summary>
    public void EnsureBavAccountGateway()
    {
        if (Account.IsAuthenticated)
        {
            return;
        }

        CurrentPage = Account;
        ActivePage = "CabinAccount";
    }

    private void HandleGateSignedIn(object? sender, EventArgs e)
    {
        Operations.ApplyGateAccessState();
        Navigate("GateDesk");
    }

    private void HandleGateSignedOut(object? sender, EventArgs e)
    {
        Operations.ApplyGateAccessState();
        if (!Account.IsAuthenticated)
        {
            CurrentPage = Account;
            ActivePage = "CabinAccount";
            return;
        }

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

        _lastSimulatorTelemetryReceivedAt = DateTimeOffset.UtcNow;
        Status.ApplyTelemetry(snapshot);
        if (_operationsClock is LocalOperationsClock simulatorClock)
        {
            simulatorClock.ApplyTelemetry(snapshot, _simulatorBridge?.CurrentStatus.Simulator ?? string.Empty);
        }
        Passengers.ApplyCabinTelemetry(snapshot);
        Operations.ApplyCabinTelemetry(snapshot);
        SendAcarsTelemetryWhenDue(snapshot);
        if (TrackAutomaticFlightCompletion(snapshot))
        {
            return;
        }
        CabinPanel.ApplyFlightTelemetry(
            snapshot,
            $"{Operations.DetectedAircraftIcao} {_simulatorBridge?.CurrentStatus.Aircraft}");
        if (!_settings.SyncXPlaneDoors)
        {
            return;
        }

        foreach (var door in Enum.GetValues<BoardingDoor>())
        {
            var signalName = $"door_{door.ToString().ToLowerInvariant()}_ratio";
            if (snapshot.Signals.TryGetValue(signalName, out var ratio))
            {
                Passengers.ApplySimulatorDoorState(door, ratio >= 0.5d);
            }
        }
    }

    private bool TrackAutomaticFlightCompletion(CabinTelemetrySnapshot snapshot)
    {
        // ACARS is the flight-tracking system; Fleet airframe accounting is a
        // separate enhancement. A temporary Fleet issue must not leave an
        // otherwise valid BAV assignment invisible on BA-Radar.
        if (!Passengers.HasPassengerManifest &&
            !Fleet.HasActiveFlightAssignment &&
            !Account.IsAcarsOperating &&
            !Account.HasWebsiteFlightAssignment)
        {
            ResetFlightCompletionTracking();
            return false;
        }

        var enginesRunning = snapshot.Signals.GetValueOrDefault("engines_running") >= 0.5d;
        var pushbackActive = snapshot.Signals.GetValueOrDefault("pushback_active") >= 0.5d;
        var beaconOn = snapshot.Signals.GetValueOrDefault("beacon_on") >= 0.5d;
        var groundSpeed = snapshot.Signals.GetValueOrDefault("groundspeed_mps");
        // Some simulator aircraft do not expose a reliable engine-running
        // signal until after take-off.  Airborne movement is just as clear an
        // indication that the booked operation has begun, and prevents a
        // valid BAV flight from remaining silently untracked.
        var readyToStart = beaconOn || enginesRunning || pushbackActive || !snapshot.OnGround;
        var readyToStartFleetOperation = enginesRunning || pushbackActive || !snapshot.OnGround;

        if (!_acarsFlightStartRequested &&
            !Account.IsAcarsOperating &&
            Account.HasWebsiteFlightAssignment &&
            readyToStart)
        {
            _acarsFlightStartRequested = true;
            _ = StartAcarsFlightAsync();
        }

        if (!_fleetFlightStartRequested && Fleet.IsFlightReserved && readyToStartFleetOperation)
        {
            _fleetFlightStartRequested = true;
            _fleetAircraftIdForCurrentFlight ??= Fleet.ActiveFlightAircraftId;
            _ = StartFleetFlightAsync();
        }
        _hasObservedEnginesRunning |= enginesRunning;
        _hasObservedDeparture |= !snapshot.OnGround || Operations.IsArrivalMode || pushbackActive;
        if (!snapshot.OnGround)
        {
            _wasAirborne = true;
            _fleetAircraftIdForCurrentFlight ??= Fleet.ActiveFlightAircraftId;
        }
        else if (_wasAirborne && _touchdownFpm is null &&
                 snapshot.Signals.TryGetValue("vertical_speed_fpm", out var verticalSpeed) &&
                 double.IsFinite(verticalSpeed) && verticalSpeed < -20d)
        {
            _touchdownFpm = (int)Math.Round(verticalSpeed);
        }

        // A recovered ACARS/fleet operation is itself durable proof that the
        // flight began.  This is important after an Ember restart or a cabin
        // unload: the in-memory engine/departure flags are gone, but the
        // server-side session must still be finalised at engine shutdown.
        // Beacon arms live tracking, but an aircraft still parked with no
        // engine, pushback or airborne movement must not produce a PIREP.
        var hasDurableFlightStart = Account.IsAcarsOperating &&
                                    Account.ActiveAcarsSession?.LastSnapshot?.FlightStarted == true;
        var completedShutdown = (hasDurableFlightStart ||
                                 (_hasObservedEnginesRunning && _hasObservedDeparture)) &&
                                snapshot.OnGround &&
                                !enginesRunning &&
                                groundSpeed < 0.35d;
        if (!completedShutdown)
        {
            _engineShutdownCandidateSince = null;
            return false;
        }

        _engineShutdownCandidateSince ??= snapshot.Timestamp;
        if (snapshot.Timestamp - _engineShutdownCandidateSince < TimeSpan.FromSeconds(10))
        {
            return false;
        }

        if (_fleetFlightCompletionInProgress)
        {
            return true;
        }

        _fleetFlightCompletionInProgress = true;
        _ = CompleteFleetFlightAndUnloadAsync(_fleetAircraftIdForCurrentFlight, _touchdownFpm);
        return true;
    }

    private async Task RestoreBavAccountSessionAsync()
    {
        if (await Account.RestoreSessionAsync())
        {
            Navigate("Dashboard");
        }
    }

    private void HandleBavAccountSessionChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(IsBavAuthenticated));
        if (Account.Session is not null)
        {
            GateLogin.SignInWithBavAccount(Account.Session);
        }
        else
        {
            GateLogin.SignOutBavAccount();
            EnsureBavAccountGateway();
        }

        Operations.ApplyGateAccessState();
        Account.RefreshFlightPlanLink(Passengers.ImportedFlightNumber, Passengers.ImportedOrigin, Passengers.ImportedDestination);
        // Fleet data is authorized by the same short-lived BAV account
        // session. Refresh immediately after sign-in instead of making pilots
        // discover that they must press Refresh a second time.
        _ = Fleet.RefreshAsync();
    }

    private void HandlePassengerFlightPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PassengerFlowViewModel.ImportedFlightNumber) or
            nameof(PassengerFlowViewModel.ImportedOrigin) or
            nameof(PassengerFlowViewModel.ImportedDestination))
        {
            Account.RefreshFlightPlanLink(Passengers.ImportedFlightNumber, Passengers.ImportedOrigin, Passengers.ImportedDestination);
        }
    }

    private async void HandleWebsiteFlightAssignmentRefreshed(object? sender, WebsiteFlightAssignmentRefreshedEventArgs e)
    {
        Operations.ApplyWebsiteFlightAssignment(e.Assignment);
        if (!e.IsNewAssignment)
        {
            return;
        }

        try
        {
            var cabinLayout = Passengers.ApplyBavAssignmentAircraft(e.Assignment);
            await Fleet.RefreshAsync();
            Fleet.SelectAircraftForBavAssignment(e.Assignment);
            var import = await Passengers.ImportBavAssignmentAsync(e.Assignment);

            if (import.Imported)
            {
                Operations.ApplySettings();
                Account.RefreshFlightPlanLink(
                    Passengers.ImportedFlightNumber,
                    Passengers.ImportedOrigin,
                    Passengers.ImportedDestination);
            }

            Account.ReportAutomaticAssignmentImport($"{cabinLayout.Message} {import.Message}");
        }
        catch (Exception exception)
        {
            Account.ReportAutomaticAssignmentImport(
                $"BAV assignment {e.Assignment.FlightNumber} was detected, but the automatic import needs attention: {exception.Message}");
        }
    }

    private async Task StartFleetFlightAsync()
    {
        try
        {
            Account.EnsureFlightPlanLink(Passengers.ImportedFlightNumber, Passengers.ImportedOrigin, Passengers.ImportedDestination);
        }
        catch (Exception exception)
        {
            Account.ReportFlightLinkFailure(exception);
            _fleetFlightStartRequested = false;
            return;
        }

        if (!await Fleet.StartAssignedFlightAsync())
        {
            _fleetFlightStartRequested = false;
            return;
        }

    }

    private async Task StartAcarsFlightAsync()
    {
        try
        {
            Account.EnsureFlightPlanLink(Passengers.ImportedFlightNumber, Passengers.ImportedOrigin, Passengers.ImportedDestination);
            await Account.StartAcarsSessionAsync(ResolveAcarsSimulator());
            var latestSnapshot = Volatile.Read(ref _latestTelemetry);
            if (latestSnapshot is not null && TryCreateAcarsTelemetry(latestSnapshot, out var telemetry))
            {
                QueueAcarsTelemetry(telemetry, latestSnapshot.Timestamp);
            }
        }
        catch (Exception exception)
        {
            // Do not prevent a later engine/pushback sample from retrying a
            // transient website failure. Fleet reservation state remains
            // independent and is never modified here.
            _acarsFlightStartRequested = false;
            Account.ReportBackgroundAcarsFailure(exception);
        }
    }

    private async Task CompleteFleetFlightAndUnloadAsync(string? aircraftId, int? touchdownFpm)
    {
        try
        {
            await Fleet.CompleteAssignedFlightAsync();
            if (!_landingAssessmentReported && aircraftId is not null && touchdownFpm is <= -500)
            {
                _landingAssessmentReported = true;
                await Fleet.RecordHardLandingAssessmentAsync(aircraftId, touchdownFpm.Value, Passengers.ImportedDestination);
            }
        }
        catch (Exception exception)
        {
            // A Fleet accounting failure must not leave the independent ACARS
            // session live after the aircraft has shut down.
            Account.ReportBackgroundAcarsFailure(exception);
        }

        try
        {
            await Account.CompleteAcarsSessionAsync(touchdownFpm);
        }
        catch (Exception exception)
        {
            Account.ReportBackgroundAcarsFailure(exception);
        }
        finally
        {
            Passengers.UnloadFlight("Flight completed · aircraft stopped and engines shut down");
            _fleetFlightCompletionInProgress = false;
        }
    }

    private void HandleFlightUnloaded()
    {
        // Unloading the cabin is a local presentation action.  It must never
        // release the reserved registration: the BAV website flight can still
        // be valid, and this event is also raised while Ember restores or
        // changes cabin content.  The dedicated ten-minute safety monitor
        // remains responsible for releasing genuinely unused reservations.
        // Do not discard completion evidence while a real ACARS or fleet
        // operation is live. Pilots may close the cabin after arrival before
        // the simulator delivers its final engine-off telemetry sample.
        if (Account.IsAcarsOperating || Fleet.IsFlightOperating)
        {
            return;
        }

        _flightSessionStore?.Clear();
        ResetFlightCompletionTracking();
    }

    private void ResetFlightCompletionTracking()
    {
        _hasObservedEnginesRunning = false;
        _hasObservedDeparture = false;
        _wasAirborne = false;
        _landingAssessmentReported = false;
        _acarsFlightStartRequested = false;
        _fleetFlightStartRequested = false;
        _fleetFlightCompletionInProgress = false;
        _touchdownFpm = null;
        _fleetAircraftIdForCurrentFlight = null;
        _engineShutdownCandidateSince = null;
        _lastAcarsTelemetrySentAt = null;
        _acarsTelemetryInFlight = false;
        _pendingAcarsTelemetry = null;
    }

    private void SendAcarsTelemetryWhenDue(CabinTelemetrySnapshot snapshot)
    {
        if (!Account.IsAcarsOperating)
        {
            return;
        }

        if (_lastAcarsTelemetrySentAt is { } lastSent && snapshot.Timestamp - lastSent < TimeSpan.FromSeconds(5))
        {
            return;
        }

        if (!TryCreateAcarsTelemetry(snapshot, out var telemetry))
        {
            return;
        }

        QueueAcarsTelemetry(telemetry, snapshot.Timestamp);
    }

    private void QueueAcarsTelemetry(FleetAcarsTelemetryDto telemetry, DateTimeOffset timestamp)
    {
        // Keep the newest unsent position. This provides a small, bounded
        // retry buffer across transient website failures without an
        // unbounded queue or stale movement reports.
        _pendingAcarsTelemetry = new PendingAcarsTelemetry(telemetry, timestamp);
        if (!_acarsTelemetryInFlight)
        {
            _ = FlushAcarsTelemetryAsync();
        }
    }

    private async Task FlushAcarsTelemetryAsync()
    {
        while (Account.IsAcarsOperating && _pendingAcarsTelemetry is { } pending)
        {
            _pendingAcarsTelemetry = null;
            _acarsTelemetryInFlight = true;
            try
            {
                await Account.SendAcarsTelemetryAsync(pending.Telemetry);
                _lastAcarsTelemetrySentAt = pending.Timestamp;
            }
            catch (Exception exception)
            {
                // Retain the last confirmed position for the next simulator
                // sample to retry; the active server-side ACARS session is
                // already durable even if this particular update fails.
                _pendingAcarsTelemetry ??= pending;
                Account.ReportBackgroundAcarsFailure(exception);
                return;
            }
            finally
            {
                _acarsTelemetryInFlight = false;
            }
        }
    }

    private bool TryCreateAcarsTelemetry(CabinTelemetrySnapshot snapshot, out FleetAcarsTelemetryDto telemetry)
    {
        var latitude = snapshot.Signals.GetValueOrDefault("latitude_deg", double.NaN);
        var longitude = snapshot.Signals.GetValueOrDefault("longitude_deg", double.NaN);
        var heading = snapshot.Signals.GetValueOrDefault("heading_deg", double.NaN);
        if (!double.IsFinite(latitude) || !double.IsFinite(longitude) || !double.IsFinite(heading))
        {
            telemetry = default!;
            return false;
        }

        var groundSpeedMetresPerSecond = snapshot.Signals.GetValueOrDefault("groundspeed_mps", 0d);
        var fuelKg = snapshot.Signals.GetValueOrDefault("fuel_kg", double.NaN);
        var verticalSpeed = snapshot.Signals.GetValueOrDefault("vertical_speed_fpm", double.NaN);
        var indicatedAirspeed = snapshot.Signals.GetValueOrDefault("indicated_airspeed_kt", double.NaN);
        var flightStarted = snapshot.Signals.GetValueOrDefault("engines_running") >= 0.5d ||
                            snapshot.Signals.GetValueOrDefault("pushback_active") >= 0.5d ||
                            _hasObservedEnginesRunning || _hasObservedDeparture || !snapshot.OnGround ||
                            Account.ActiveAcarsSession?.LastSnapshot?.FlightStarted == true;
        telemetry = new FleetAcarsTelemetryDto(
            latitude,
            longitude,
            snapshot.AltitudeFeet,
            Math.Max(0d, groundSpeedMetresPerSecond * 1.9438444924406d),
            ((heading % 360d) + 360d) % 360d,
            double.IsFinite(indicatedAirspeed) ? Math.Max(0d, indicatedAirspeed) : null,
            FormatSquawk(snapshot.Signals.GetValueOrDefault("squawk_bco16", double.NaN)),
            snapshot.Signals.GetValueOrDefault("beacon_on") >= 0.5d,
            double.IsFinite(fuelKg) ? Math.Max(0d, fuelKg) : null,
            snapshot.Signals.GetValueOrDefault("engines_running") >= 0.5d,
            snapshot.Signals.GetValueOrDefault("parking_brake_set") >= 0.5d,
            snapshot.OnGround,
            double.IsFinite(verticalSpeed) ? verticalSpeed : null,
            flightStarted,
            Fleet.ActiveFlightRegistration);
        return true;
    }

    private static string? FormatSquawk(double value)
    {
        if (!double.IsFinite(value) || value < 0d || value > 65_535d) return null;
        var rounded = (int)Math.Round(value);
        var decimalCode = rounded.ToString("D4", System.Globalization.CultureInfo.InvariantCulture);
        if (decimalCode.Length == 4 && decimalCode.All(character => character is >= '0' and <= '7')) return decimalCode;
        var bcdCode = rounded.ToString("X4", System.Globalization.CultureInfo.InvariantCulture);
        return bcdCode.All(character => character is >= '0' and <= '7') ? bcdCode : null;
    }

    private sealed record PendingAcarsTelemetry(FleetAcarsTelemetryDto Telemetry, DateTimeOffset Timestamp);

    private string ResolveAcarsSimulator()
    {
        var simulator = _simulatorBridge?.CurrentStatus.Simulator ?? string.Empty;
        if (simulator.Contains("X-Plane", StringComparison.OrdinalIgnoreCase)) return "xplane12";
        if (simulator.Contains("2020", StringComparison.OrdinalIgnoreCase)) return "msfs2020";
        return "msfs2024";
    }

    private void PrepareFlightForUpdate()
    {
        _preserveFlightForUpdate = true;
        PersistFlightSession();
    }

    private void CancelFlightUpdateShutdown() => _preserveFlightForUpdate = false;

    private async void HandleDoorControlRequested(BoardingDoor door, bool isOpen)
    {
        if (_simulatorCabinControlBridge is null)
        {
            return;
        }

        await _simulatorCabinControlBridge.SetAircraftDoorOpenAsync(door.ToString(), isOpen);
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
