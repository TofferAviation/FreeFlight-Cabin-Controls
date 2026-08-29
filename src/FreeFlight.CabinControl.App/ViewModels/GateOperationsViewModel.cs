using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Input;
using System.Windows.Threading;
using FreeFlight.CabinControl.App.Infrastructure;
using FreeFlight.CabinControl.App.Services;
using FreeFlight.CabinControl.Core.Configuration;
using FreeFlight.CabinControl.Core.Integration;
using FreeFlight.CabinControl.Core.Operations;
using FreeFlight.CabinControl.Core.Passengers;

namespace FreeFlight.CabinControl.App.ViewModels;

public sealed class GateOperationsViewModel : PageViewModel, IDisposable
{
    private static readonly TimeSpan LatePassengerGracePeriod = TimeSpan.FromMinutes(10);
    private readonly AppSettings _settings;
    private readonly PassengerFlowViewModel _passengers;
    private readonly IOperationsClock _operationsClock;
    private readonly Func<bool> _hasGateAccess;
    private readonly IBoardingPassPrinterService _boardingPassPrinterService;
    private readonly DispatcherTimer _clockTimer;
    private readonly Dictionary<int, GatePassengerViewModel> _passengersById = [];
    private GatePassengerViewModel? _selectedPassenger;
    private string _searchText = string.Empty;
    private string _selectedCabinFilter = "All Passengers";
    private bool _isGateOpen;
    private bool _gateHasClosed;
    private string _operationMessage = "No passenger list loaded. Import SimBrief or enter a manual passenger count.";
    private PrinterDestination? _selectedPrinter;
    private string _printerStatusMessage = "Checking Windows printers…";
    private bool _hasDeparted;
    private bool _hasLanded;
    private string _liveFlightPhase = "Preflight";
    private double _liveAltitudeFeet;
    private string _flightBannerMessage = string.Empty;
    private bool _pushbackActive;

    public GateOperationsViewModel(
        AppSettings settings,
        PassengerFlowViewModel passengers,
        IOperationsClock operationsClock,
        Func<bool>? hasGateAccess = null,
        IBoardingPassPrinterService? boardingPassPrinterService = null)
        : base("Overview", "Gate preparation, boarding readiness, and live passenger operations")
    {
        _settings = settings;
        _passengers = passengers;
        _operationsClock = operationsClock;
        _hasGateAccess = hasGateAccess ?? (() => true);
        _boardingPassPrinterService = boardingPassPrinterService ?? new WindowsBoardingPassPrinterService();
        CabinFilters = ["All Passengers", "First", "Club World", "World Traveller Plus", "World Traveller"];
        TimelineEvents =
        [
            new FlightTimelineEventViewModel("Flight Loaded", "\uE8F1"),
            new FlightTimelineEventViewModel("Turnaround Start", "\uE823"),
            new FlightTimelineEventViewModel("Gate Open", "\uE7C8"),
            new FlightTimelineEventViewModel("Boarding", "\uE716"),
            new FlightTimelineEventViewModel("Gate Closed", "\uE785"),
            new FlightTimelineEventViewModel("Departure", "\uE709")
        ];
        ToggleGateCommand = new RelayCommand(_ => ToggleGate());
        StartManageBoardingCommand = new RelayCommand(_ => StartManageBoarding());
        SelectPassengerCommand = new RelayCommand(SelectPassenger);
        CheckInPassengerCommand = new RelayCommand(CheckInPassenger);
        BoardPassengerCommand = new RelayCommand(BoardPassenger);
        ToggleBagLoadedCommand = new RelayCommand(ToggleBagLoaded);
        PrintBoardingPassCommand = new RelayCommand(PrintBoardingPass);
        RefreshPrintersCommand = new RelayCommand(_ => RefreshPrinters());
        MarkBoardingPassIssuedCommand = new RelayCommand(MarkBoardingPassIssued);
        ImportSimBriefCommand = new AsyncRelayCommand(ImportSimBriefAsync, HandleImportError);

        _passengers.PassengerManifest.CollectionChanged += HandleManifestCollectionChanged;
        _passengers.PropertyChanged += HandlePassengerFlowPropertyChanged;
        _clockTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _clockTimer.Tick += HandleClockTick;
        RebuildPassengerRecords();
        RefreshPrinters();
        RefreshOperationalClock();
        _clockTimer.Start();
    }

    public PassengerFlowViewModel PassengerFlow => _passengers;
    public ObservableCollection<GatePassengerViewModel> PassengerRecords { get; } = [];
    public ObservableCollection<GatePassengerViewModel> VisiblePassengers { get; } = [];
    public ObservableCollection<PrinterDestination> AvailablePrinters { get; } = [];
    public ObservableCollection<FlightTimelineEventViewModel> TimelineEvents { get; }
    public IReadOnlyList<string> CabinFilters { get; }
    public ICommand ToggleGateCommand { get; }
    public ICommand StartManageBoardingCommand { get; }
    public ICommand SelectPassengerCommand { get; }
    public ICommand CheckInPassengerCommand { get; }
    public ICommand BoardPassengerCommand { get; }
    public ICommand ToggleBagLoadedCommand { get; }
    public ICommand PrintBoardingPassCommand { get; }
    public ICommand RefreshPrintersCommand { get; }
    public ICommand MarkBoardingPassIssuedCommand { get; }
    public ICommand ImportSimBriefCommand { get; }

    public GatePassengerViewModel? SelectedPassenger
    {
        get => _selectedPassenger;
        set => SetProperty(ref _selectedPassenger, value);
    }

    public PrinterDestination? SelectedPrinter
    {
        get => _selectedPrinter;
        set
        {
            if (!SetProperty(ref _selectedPrinter, value))
            {
                return;
            }

            if (value is not null)
            {
                _settings.BoardingPassPrinter = value.QueueId;
                PrinterStatusMessage = value.IsOffline
                    ? $"{value.DisplayName} is currently offline."
                    : $"{value.DisplayName} is ready for boarding passes.";
            }

            OnPropertyChanged(nameof(HasAvailablePrinter));
            OnPropertyChanged(nameof(SelectedPrinterLabel));
        }
    }

    public bool HasAvailablePrinter => SelectedPrinter is { IsOffline: false };

    public string SelectedPrinterLabel => SelectedPrinter?.DisplayLabel ?? "No Windows printer selected";

    public string PrinterStatusMessage
    {
        get => _printerStatusMessage;
        private set => SetProperty(ref _printerStatusMessage, value);
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                RefreshVisiblePassengers();
            }
        }
    }

    public string SelectedCabinFilter
    {
        get => _selectedCabinFilter;
        set
        {
            if (SetProperty(ref _selectedCabinFilter, value))
            {
                RefreshVisiblePassengers();
            }
        }
    }

    public bool IsGateOpen
    {
        get => _isGateOpen;
        private set
        {
            if (!SetProperty(ref _isGateOpen, value))
            {
                return;
            }

            OnPropertyChanged(nameof(GateStatusLabel));
            OnPropertyChanged(nameof(GateStatusColor));
            OnPropertyChanged(nameof(GateActionLabel));
            OnPropertyChanged(nameof(GateActionGlyph));
            OnPropertyChanged(nameof(CanBoardPassengers));
            OnPropertyChanged(nameof(ReadinessGateStatus));
        }
    }

    public string OperationMessage
    {
        get => _operationMessage;
        private set => SetProperty(ref _operationMessage, value);
    }

    public string FlightNumber => string.IsNullOrWhiteSpace(_passengers.ImportedFlightNumber)
        ? _settings.GateFlightNumber
        : _passengers.ImportedFlightNumber;
    public string OriginIata => string.IsNullOrWhiteSpace(_passengers.ImportedOrigin)
        ? _settings.GateOriginIata
        : NormalizeAirport(_passengers.ImportedOrigin);
    public string DestinationIata => string.IsNullOrWhiteSpace(_passengers.ImportedDestination)
        ? _settings.GateDestinationIata
        : NormalizeAirport(_passengers.ImportedDestination);
    public string RouteSummary => $"{AirportName(OriginIata)}  →  {AirportName(DestinationIata)}";
    public string DetectedAircraftIcao => string.IsNullOrWhiteSpace(_passengers.ImportedAircraftIcao)
        ? _passengers.SelectedCabinLayoutProfile.Layout switch
        {
            PassengerCabinLayout.BritishAirways777200Er => "B772",
            PassengerCabinLayout.BritishAirways777300 => "B77W",
            _ => "B77W"
        }
        : _passengers.ImportedAircraftIcao;
    public AircraftGateAssignment DepartureGateAssignment => AircraftGateAssignmentService.Assign(
        OriginIata,
        DetectedAircraftIcao,
        FlightNumber,
        ScheduledDepartureMoment,
        _settings.GateNumber,
        _settings.AutomaticGateAssignment);
    public AircraftGateAssignment ArrivalGateAssignment => AircraftGateAssignmentService.Assign(
        DestinationIata,
        DetectedAircraftIcao,
        FlightNumber,
        ScheduledDepartureMoment,
        _settings.ArrivalGateNumber,
        _settings.AutomaticGateAssignment);
    public AircraftGateAssignment GateAssignment => DepartureGateAssignment;
    public string GateNumber => DepartureGateAssignment.GateNumber;
    public string ArrivalGateNumber => ArrivalGateAssignment.GateNumber;
    public string GateHeader => IsSimBriefSynced
        ? $"DEP {GateNumber}  →  ARR {ArrivalGateNumber}"
        : "DEP --  →  ARR --";
    public string GateAssignmentSummary => DepartureGateAssignment.Summary;
    public string AircraftName => AircraftGateAssignmentService.DescribeAircraft(DetectedAircraftIcao);
    public bool IsSimBriefSynced => _passengers.HasSimBriefFlight;
    public string SimBriefConnectionLabel => IsSimBriefSynced ? "SimBrief Synced" : "SimBrief Ready";
    public string SimBriefImportLabel => _passengers.LastSimBriefSyncLabel;

    public int TotalPassengers => PassengerRecords.Count;
    public bool HasPassengerList => TotalPassengers > 0;
    public bool IsPassengerListEmpty => !HasPassengerList;
    public string PassengerListStatus => HasPassengerList
        ? $"{TotalPassengers} passenger records loaded"
        : "No passenger list loaded — import SimBrief or enter a manual passenger count.";
    public int CheckedInPassengers => PassengerRecords.Count(passenger => passenger.IsCheckedIn);
    public int BoardedPassengers => PassengerRecords.Count(passenger => passenger.IsBoarded);
    public int LatePassengers => PassengerRecords.Count(passenger => passenger.IsLate);
    public int NoShowPassengers => PassengerRecords.Count(passenger => passenger.IsNoShow);
    public PassengerNoShowForecast NoShowForecast => PassengerNoShowForecastService.Calculate(
        FlightNumber,
        OriginIata,
        DestinationIata,
        TotalPassengers);
    public int ForecastNoShowRate => NoShowForecast.RatePercent;
    public int ForecastNoShowPassengers => NoShowForecast.ForecastPassengerCount;
    public string NoShowForecastSummary =>
        $"{ForecastNoShowPassengers} forecast no-shows · {ForecastNoShowRate}% · {NoShowForecast.ProfileLabel}";
    public string NoShowForecastCompact =>
        $"Forecast {ForecastNoShowPassengers} no-show · {ForecastNoShowRate}%";
    public int TotalBags => PassengerRecords.Sum(passenger => passenger.CheckedBags);
    public int PlannedBags => PassengerRecords
        .Where(passenger => passenger.IsCheckedIn && !passenger.IsNoShow)
        .Sum(passenger => passenger.CheckedBags);
    public int LoadedBags => PassengerRecords.Sum(passenger => passenger.LoadedBagCount);
    public int AwaitingBags => PassengerRecords.Sum(passenger => passenger.AwaitingBagCount);
    public int PlannedBaggageWeightKg => PassengerRecords
        .Where(passenger => passenger.IsCheckedIn && !passenger.IsNoShow)
        .Sum(passenger => passenger.PlannedBaggageWeightKg);
    public int LoadedBaggageWeightKg => PassengerRecords.Sum(passenger => passenger.LoadedBaggageWeightKg);
    public int BaggageWeightDeltaKg => LoadedBaggageWeightKg - PlannedBaggageWeightKg;
    public int BaggageDiscrepancyPassengers => PassengerRecords.Count(passenger => passenger.HasBaggageDiscrepancy);
    public bool HasBaggageDiscrepancies => BaggageDiscrepancyPassengers > 0;
    public string BaggageReconciliationLabel => HasBaggageDiscrepancies
        ? $"{BaggageDiscrepancyPassengers} baggage discrepancies · final load sheet blocked"
        : $"{LoadedBags}/{PlannedBags} accepted bags loaded · {LoadedBaggageWeightKg:N0} kg actual";
    public string BaggageReconciliationColor => HasBaggageDiscrepancies ? "#FF6666" : "#58E68A";
    public int CheckedInPercent => Percentage(CheckedInPassengers, TotalPassengers);
    public int BoardedPercent => Percentage(BoardedPassengers, TotalPassengers);
    public int BagsLoadedPercent => Percentage(LoadedBags, TotalBags);
    public int FirstCount => PassengerRecords.Count(passenger => passenger.CabinMarketingName == "First");
    public int ClubWorldCount => PassengerRecords.Count(passenger => passenger.CabinMarketingName == "Club World");
    public int WorldTravellerPlusCount => PassengerRecords.Count(passenger => passenger.CabinMarketingName == "World Traveller Plus");
    public int WorldTravellerCount => PassengerRecords.Count(passenger => passenger.CabinMarketingName == "World Traveller");
    public string BoardingProgressText => $"{BoardedPassengers} / {TotalPassengers}";
    public string BoardingStatusLabel => _passengers.BoardingState switch
    {
        BoardingRunState.Boarding => "BOARDING IN PROGRESS",
        BoardingRunState.Paused => "BOARDING PAUSED",
        BoardingRunState.Complete => "BOARDING COMPLETE",
        BoardingRunState.WaitingForDoor => "WAITING FOR AIRCRAFT DOOR",
        BoardingRunState.Deboarding => "DEBOARDING IN PROGRESS",
        BoardingRunState.DeboardingComplete => "CABIN EMPTY",
        _ => IsGateOpen ? "GATE OPEN / READY TO BOARD" : "GATE CLOSED / PREPARING"
    };
    public string BoardingStatusColor => _passengers.BoardingState is BoardingRunState.Boarding or BoardingRunState.Complete
        ? "#58E68A"
        : "#F0C64E";

    public string GateStatusLabel => IsGateOpen ? "GATE OPEN" : _gateHasClosed ? "GATE CLOSED" : "GATE PREPARATION";
    public string GateStatusColor => IsGateOpen ? "#58E68A" : "#F0C64E";
    public string GateActionLabel => IsGateOpen ? "Close Gate" : "Open Gate";
    public string GateActionGlyph => IsGateOpen ? "\uE77A" : "\uE7C8";
    public bool CanBoardPassengers => _hasGateAccess() && IsGateOpen && (!_gateHasClosed || _settings.ManualGateOverride);
    public string ReadinessGateStatus => !IsSimBriefSynced
        ? "Awaiting SimBrief flight plan"
        : IsGateOpen
        ? $"DEP {OriginIata} {GateNumber} open · ARR {DestinationIata} {ArrivalGateNumber}"
        : $"DEP {OriginIata} {GateNumber} → ARR {DestinationIata} {ArrivalGateNumber}";

    public DateTimeOffset ScheduledDepartureMoment => ResolveScheduledDeparture();
    public FlightTurnaroundSchedule TurnaroundSchedule => FlightTurnaroundSchedule.Create(
        ScheduledDepartureMoment,
        _settings.TurnaroundMinutes,
        _settings.BoardingStartMinutesBeforeDeparture,
        _settings.FinalBoardingMinutesBeforeDeparture,
        _settings.GateCloseMinutesBeforeDeparture);
    public string CurrentClockTime => _operationsClock.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
    public string CurrentClockDate => _operationsClock.Now.ToString("dd MMM yyyy", CultureInfo.InvariantCulture).ToUpperInvariant();
    public string ClockSourceLabel => _operationsClock.SourceLabel;
    public string ScheduleSourceLabel => IsSimBriefSynced ? "SIMBRIEF DEPARTURE" : "SETTINGS FALLBACK";
    public string ScheduledDeparture => FormatTime(TurnaroundSchedule.Departure);
    public string FlightDateShort => TurnaroundSchedule.Departure.ToString("ddMMM", CultureInfo.InvariantCulture).ToUpperInvariant();
    public string FlightDateLong => TurnaroundSchedule.Departure.ToString("dd MMM yyyy", CultureInfo.InvariantCulture).ToUpperInvariant();
    public string TurnaroundStartsAt => FormatTime(TurnaroundSchedule.TurnaroundStart);
    public string GateOpensAt => FormatTime(TurnaroundSchedule.GateOpen);
    public string BoardingBeginsAt => FormatTime(TurnaroundSchedule.BoardingStart);
    public string FinalBoardingAt => FormatTime(TurnaroundSchedule.FinalBoarding);
    public string GateClosesAt => FormatTime(TurnaroundSchedule.GateClose);
    public string TurnaroundSummary => $"{_settings.TurnaroundMinutes} MIN TURNAROUND";
    public bool IsArrivalMode => _hasDeparted;
    public string TimelineTitle => IsArrivalMode ? "LIVE FLIGHT & ARRIVAL TIMELINE" : "FLIGHT TIMELINE";
    public string TimelineContext => IsArrivalMode ? $"{OriginIata} → {DestinationIata}" : TurnaroundSummary;
    public string FlightBannerMessage => _flightBannerMessage;
    public bool HasFlightBannerMessage => !string.IsNullOrWhiteSpace(FlightBannerMessage);
    public string TimelinePhaseLabel => _pushbackActive && !IsArrivalMode
        ? "PUSHBACK ACTIVE"
        : IsArrivalMode
        ? _hasLanded ? "ARRIVED" : _liveFlightPhase.ToUpperInvariant()
        : !IsSimBriefSynced
        ? "FLIGHT LOADED · AWAITING SIMBRIEF"
        : TurnaroundSchedule.GetStage(_operationsClock.Now) switch
    {
        TurnaroundStage.AwaitingTurnaround => "AWAITING TURNAROUND",
        TurnaroundStage.Turnaround => "TURNAROUND IN PROGRESS",
        TurnaroundStage.GateOpen => "GATE OPEN WINDOW",
        TurnaroundStage.Boarding => "BOARDING WINDOW",
        TurnaroundStage.GateClosing => "GATE CLOSING",
        _ => "DEPARTURE DUE"
    };
    public string TimelinePhaseColor => _pushbackActive && !IsArrivalMode
        ? "#F0C64E"
        : IsArrivalMode
        ? _hasLanded ? "#58E68A" : "#63B9FF"
        : TurnaroundSchedule.GetStage(_operationsClock.Now) switch
    {
        TurnaroundStage.Turnaround or TurnaroundStage.GateOpen or TurnaroundStage.Boarding => "#58E68A",
        TurnaroundStage.GateClosing or TurnaroundStage.Departure => "#F0C64E",
        _ => "#63B9FF"
    };

    public int UnissuedBoardingPasses => PassengerRecords.Count(passenger => passenger.BoardingPassStatus == "Unissued");
    public int ReadyBoardingPasses => PassengerRecords.Count(passenger => passenger.BoardingPassStatus == "Ready to Print");
    public int PrintedBoardingPasses => PassengerRecords.Count(passenger => passenger.BoardingPassStatus == "Printed");
    public int ReprintBoardingPasses => PassengerRecords.Count(passenger => passenger.BoardingPassStatus == "Reprint Required");

    public void ApplySettings()
    {
        OnPropertyChanged(nameof(FlightNumber));
        OnPropertyChanged(nameof(OriginIata));
        OnPropertyChanged(nameof(DestinationIata));
        OnPropertyChanged(nameof(RouteSummary));
        OnPropertyChanged(nameof(DetectedAircraftIcao));
        OnPropertyChanged(nameof(DepartureGateAssignment));
        OnPropertyChanged(nameof(ArrivalGateAssignment));
        OnPropertyChanged(nameof(GateAssignment));
        OnPropertyChanged(nameof(GateNumber));
        OnPropertyChanged(nameof(ArrivalGateNumber));
        OnPropertyChanged(nameof(GateHeader));
        OnPropertyChanged(nameof(GateAssignmentSummary));
        OnPropertyChanged(nameof(AircraftName));
        OnPropertyChanged(nameof(ScheduledDepartureMoment));
        OnPropertyChanged(nameof(TurnaroundSchedule));
        OnPropertyChanged(nameof(ScheduledDeparture));
        OnPropertyChanged(nameof(FlightDateShort));
        OnPropertyChanged(nameof(FlightDateLong));
        OnPropertyChanged(nameof(TurnaroundStartsAt));
        OnPropertyChanged(nameof(GateOpensAt));
        OnPropertyChanged(nameof(BoardingBeginsAt));
        OnPropertyChanged(nameof(FinalBoardingAt));
        OnPropertyChanged(nameof(GateClosesAt));
        OnPropertyChanged(nameof(TurnaroundSummary));
        OnPropertyChanged(nameof(ScheduleSourceLabel));
        OnPropertyChanged(nameof(TimelinePhaseLabel));
        OnPropertyChanged(nameof(TimelinePhaseColor));
        OnPropertyChanged(nameof(CanBoardPassengers));
        OnPropertyChanged(nameof(ReadinessGateStatus));
        ApplyNoShowForecast();
        RefreshTimelineEvents();
    }

    public void ApplyCabinTelemetry(CabinTelemetrySnapshot snapshot)
    {
        _liveFlightPhase = snapshot.FlightPhase;
        _liveAltitudeFeet = snapshot.AltitudeFeet;
        _pushbackActive = snapshot.Signals.GetValueOrDefault("pushback_active") >= 0.5d;
        var wasArrivalMode = _hasDeparted;
        if (!snapshot.OnGround)
        {
            _hasDeparted = true;
            _hasLanded = false;
            _flightBannerMessage = string.Empty;
        }
        else if (_hasDeparted && !_hasLanded)
        {
            _hasLanded = true;
            _flightBannerMessage = $"Welcome to {AirportName(DestinationIata)}!";
        }

        if (wasArrivalMode != _hasDeparted)
        {
            OnPropertyChanged(nameof(IsArrivalMode));
            OnPropertyChanged(nameof(TimelineTitle));
            OnPropertyChanged(nameof(TimelineContext));
        }
        OnPropertyChanged(nameof(FlightBannerMessage));
        OnPropertyChanged(nameof(HasFlightBannerMessage));
        OnPropertyChanged(nameof(TimelinePhaseLabel));
        OnPropertyChanged(nameof(TimelinePhaseColor));
        RefreshTimelineEvents();
    }

    public void ApplyGateAccessState()
    {
        OnPropertyChanged(nameof(CanBoardPassengers));
        if (!_hasGateAccess())
        {
            OperationMessage = "Gate workspace locked. Sign in through Gate Login to continue operations.";
        }
    }

    public void Dispose()
    {
        _clockTimer.Stop();
        _clockTimer.Tick -= HandleClockTick;
        _passengers.PassengerManifest.CollectionChanged -= HandleManifestCollectionChanged;
        _passengers.PropertyChanged -= HandlePassengerFlowPropertyChanged;
        GC.SuppressFinalize(this);
    }

    private void ToggleGate()
    {
        if (!RequireGateAccess())
        {
            return;
        }

        if (IsGateOpen)
        {
            IsGateOpen = false;
            _gateHasClosed = true;
            if (_passengers.BoardingState is BoardingRunState.Boarding or BoardingRunState.WaitingForDoor)
            {
                _passengers.StartPauseCommand.Execute(null);
            }

            OperationMessage = $"Gate {GateNumber} closed. New boarding is held.";
        }
        else
        {
            IsGateOpen = true;
            _gateHasClosed = false;
            OperationMessage = $"Gate {GateNumber} opened for check-in and boarding.";
        }

        NotifyOperationalMetrics();
    }

    private void StartManageBoarding()
    {
        if (!RequireGateAccess())
        {
            return;
        }

        if (!CanBoardPassengers)
        {
            OperationMessage = _gateHasClosed && _settings.PreventBoardingAfterGateClose
                ? "Boarding is locked after gate close. Enable Manual Override in Settings to continue."
                : "Open the gate before starting passenger boarding.";
            return;
        }

        if (_passengers.BoardingState is BoardingRunState.Complete or BoardingRunState.Deboarding)
        {
            OperationMessage = "The boarding manifest is already complete.";
            return;
        }

        _passengers.StartPauseCommand.Execute(null);
        OperationMessage = _passengers.BoardingState == BoardingRunState.Paused
            ? "Boarding paused from the gate desk."
            : $"Boarding control handed to Group {_passengers.CurrentBoardingGroup}.";
        NotifyOperationalMetrics();
    }

    private void SelectPassenger(object? parameter)
    {
        if (ResolvePassenger(parameter) is { } passenger)
        {
            SelectedPassenger = passenger;
        }
    }

    private void CheckInPassenger(object? parameter)
    {
        if (!RequireGateAccess())
        {
            return;
        }

        if (ResolvePassenger(parameter) is not { } passenger)
        {
            return;
        }

        if (passenger.IsNoShow)
        {
            OperationMessage = $"{passenger.FullName} is recorded as a no-show after the 10-minute late limit.";
            return;
        }

        passenger.MarkCheckedIn();
        _passengers.SetPassengerBoardingHold(passenger.PassengerId, false);
        passenger.BoardingPassStatus = passenger.BoardingPassStatus == "Unissued" ? "Ready to Print" : passenger.BoardingPassStatus;
        SelectedPassenger = passenger;
        OperationMessage = $"{passenger.FullName} checked in for {FlightNumber}.";
        NotifyOperationalMetrics();
    }

    private void BoardPassenger(object? parameter)
    {
        if (!RequireGateAccess())
        {
            return;
        }

        if (ResolvePassenger(parameter) is not { } passenger)
        {
            return;
        }

        if (!CanBoardPassengers)
        {
            OperationMessage = "The passenger cannot board while the gate is closed.";
            return;
        }

        if (passenger.IsNoShow)
        {
            OperationMessage = $"{passenger.FullName} is recorded as a no-show and cannot board.";
            return;
        }

        if (!passenger.IsCheckedIn)
        {
            OperationMessage = $"Check in {passenger.FullName} before boarding.";
            return;
        }

        if (!_passengers.BoardPassengerFromGate(passenger.PassengerId))
        {
            OperationMessage = $"{passenger.FullName} is already boarded or unavailable for this operation.";
            return;
        }

        passenger.MarkManuallyBoarded();
        SelectedPassenger = passenger;
        OperationMessage = $"{passenger.FullName} boarded with Group {passenger.BoardingGroup}.";
        NotifyOperationalMetrics();
    }

    private void ToggleBagLoaded(object? parameter)
    {
        if (!RequireGateAccess())
        {
            return;
        }

        if (ResolvePassenger(parameter) is not { } passenger || passenger.CheckedBags == 0)
        {
            return;
        }

        passenger.ToggleBaggageLoadedState();
        SelectedPassenger = passenger;
        OperationMessage = passenger.BaggageOperationMessage;
        NotifyOperationalMetrics();
    }

    private void PrintBoardingPass(object? parameter)
    {
        if (!RequireGateAccess())
        {
            return;
        }

        if (ResolvePassenger(parameter) is not { } passenger)
        {
            return;
        }

        if (SelectedPrinter is not { } printer)
        {
            OperationMessage = "No Windows printer is available. Connect a printer and use the refresh arrow.";
            PrinterStatusMessage = OperationMessage;
            return;
        }

        SelectedPassenger = passenger;
        var result = _boardingPassPrinterService.PrintBoardingPass(
            printer,
            this,
            $"{FlightNumber} {passenger.FullName} {passenger.SeatNumber}");
        PrinterStatusMessage = result.Message;
        if (!result.IsSuccess)
        {
            OperationMessage = result.Message;
            return;
        }

        passenger.BoardingPassStatus = "Printed";
        passenger.LastPrintedLabel = DateTime.Now.ToString("HH:mm", CultureInfo.InvariantCulture);
        OperationMessage = $"Boarding pass printed for {passenger.FullName}. {result.Message}";
        NotifyOperationalMetrics();
    }

    private void RefreshPrinters()
    {
        var priorQueueId = SelectedPrinter?.QueueId ?? _settings.BoardingPassPrinter;
        AvailablePrinters.Clear();
        foreach (var printer in _boardingPassPrinterService.GetPrinters())
        {
            AvailablePrinters.Add(printer);
        }

        SelectedPrinter = AvailablePrinters.FirstOrDefault(printer =>
                              string.Equals(printer.QueueId, priorQueueId, StringComparison.OrdinalIgnoreCase) ||
                              string.Equals(printer.DisplayName, priorQueueId, StringComparison.OrdinalIgnoreCase)) ??
                          AvailablePrinters.FirstOrDefault(printer => printer.IsDefault) ??
                          AvailablePrinters.FirstOrDefault();
        if (SelectedPrinter is null)
        {
            PrinterStatusMessage = "No Windows printers found. Connect or install a printer, then refresh.";
            OnPropertyChanged(nameof(HasAvailablePrinter));
            OnPropertyChanged(nameof(SelectedPrinterLabel));
        }
    }

    private void MarkBoardingPassIssued(object? parameter)
    {
        if (!RequireGateAccess())
        {
            return;
        }

        if (ResolvePassenger(parameter) is not { } passenger)
        {
            return;
        }

        passenger.BoardingPassStatus = "Printed";
        SelectedPassenger = passenger;
        NotifyOperationalMetrics();
    }

    private async Task ImportSimBriefAsync()
    {
        await _passengers.SyncSimBriefAsync();
        ApplySettings();
        OperationMessage = _passengers.SimBriefStatus;
    }

    private void HandleImportError(Exception exception) => OperationMessage = exception.Message;

    private bool RequireGateAccess()
    {
        if (_hasGateAccess())
        {
            return true;
        }

        OperationMessage = "Gate workspace locked. Sign in through Gate Login to continue operations.";
        return false;
    }

    private GatePassengerViewModel? ResolvePassenger(object? parameter) => parameter switch
    {
        GatePassengerViewModel passenger => passenger,
        int id when _passengersById.TryGetValue(id, out var passenger) => passenger,
        _ => SelectedPassenger
    };

    private void RebuildPassengerRecords()
    {
        PassengerRecords.Clear();
        _passengersById.Clear();
        foreach (var source in _passengers.PassengerManifest)
        {
            AddPassengerRecord(source);
        }

        SelectedPassenger = PassengerRecords.FirstOrDefault();
        RefreshVisiblePassengers();
        ApplyNoShowForecast();
        OperationMessage = PassengerListStatus;
        NotifyOperationalMetrics();
    }

    private void AddPassengerRecord(PassengerManifestEntryViewModel source)
    {
        if (_passengersById.ContainsKey(source.PassengerId))
        {
            return;
        }

        var passenger = new GatePassengerViewModel(source, _settings.PassengerGenerationSeed);
        passenger.PropertyChanged += HandleGatePassengerPropertyChanged;
        _passengersById.Add(passenger.PassengerId, passenger);
        PassengerRecords.Add(passenger);
    }

    private void RefreshVisiblePassengers()
    {
        var search = SearchText.Trim();
        var filtered = PassengerRecords.Where(passenger =>
            (SelectedCabinFilter == "All Passengers" || passenger.CabinMarketingName == SelectedCabinFilter) &&
            (search.Length == 0 ||
             passenger.FullName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
             passenger.BookingReference.Contains(search, StringComparison.OrdinalIgnoreCase) ||
             passenger.SeatNumber.Contains(search, StringComparison.OrdinalIgnoreCase)));

        VisiblePassengers.Clear();
        foreach (var passenger in filtered)
        {
            VisiblePassengers.Add(passenger);
        }
    }

    private void HandleManifestCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (var passenger in PassengerRecords)
            {
                passenger.PropertyChanged -= HandleGatePassengerPropertyChanged;
            }

            RebuildPassengerRecords();
            return;
        }

        if (e.NewItems is not null)
        {
            foreach (var source in e.NewItems.OfType<PassengerManifestEntryViewModel>())
            {
                AddPassengerRecord(source);
            }
        }

        ApplyNoShowForecast();
        RefreshVisiblePassengers();
        SelectedPassenger ??= PassengerRecords.FirstOrDefault();
        OperationMessage = PassengerListStatus;
        NotifyOperationalMetrics();
    }

    private void HandlePassengerFlowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PassengerFlowViewModel.BoardedPassengerCount) or
            nameof(PassengerFlowViewModel.BoardingState) or
            nameof(PassengerFlowViewModel.CurrentBoardingGroup))
        {
            foreach (var passenger in PassengerRecords)
            {
                passenger.RefreshOperationalState();
            }

            NotifyOperationalMetrics();
            if (_passengers.BoardingState == BoardingRunState.DeboardingComplete)
            {
                _flightBannerMessage = "See you next time, Captain!";
                OnPropertyChanged(nameof(FlightBannerMessage));
                OnPropertyChanged(nameof(HasFlightBannerMessage));
            }
        }
        else if (e.PropertyName is nameof(PassengerFlowViewModel.HasSimBriefFlight) or
                 nameof(PassengerFlowViewModel.ImportedFlightNumber) or
                 nameof(PassengerFlowViewModel.ImportedOrigin) or
                 nameof(PassengerFlowViewModel.ImportedDestination) or
                 nameof(PassengerFlowViewModel.ImportedAircraftIcao) or
                 nameof(PassengerFlowViewModel.ImportedScheduledDepartureLocal) or
                 nameof(PassengerFlowViewModel.LastSimBriefSyncTime))
        {
            ApplySettings();
            OnPropertyChanged(nameof(IsSimBriefSynced));
            OnPropertyChanged(nameof(SimBriefConnectionLabel));
            OnPropertyChanged(nameof(SimBriefImportLabel));
        }
        else if (e.PropertyName == nameof(PassengerFlowViewModel.SelectedCabinLayoutProfile))
        {
            ApplySettings();
        }
    }

    private void HandleGatePassengerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(GatePassengerViewModel.IsCheckedIn) or
            nameof(GatePassengerViewModel.IsBoarded) or
            nameof(GatePassengerViewModel.IsLate) or
            nameof(GatePassengerViewModel.IsNoShow) or
            nameof(GatePassengerViewModel.LoadedBaggageWeightKg) or
            nameof(GatePassengerViewModel.BoardingPassStatus))
        {
            NotifyOperationalMetrics();
        }
    }

    private void ApplyNoShowForecast()
    {
        var forecast = NoShowForecast;
        var routeKey = $"{FlightNumber}|{OriginIata}|{DestinationIata}";
        var forecastCandidateIds = PassengerRecords
            .OrderBy(passenger => GetForecastRank(routeKey, passenger.PassengerId))
            .Take(forecast.ForecastPassengerCount)
            .Select(passenger => passenger.PassengerId)
            .ToHashSet();

        foreach (var passenger in PassengerRecords)
        {
            var isCandidate = forecastCandidateIds.Contains(passenger.PassengerId);
            passenger.ApplyNoShowForecastCandidate(isCandidate);
            _passengers.SetPassengerBoardingHold(
                passenger.PassengerId,
                isCandidate && !passenger.IsCheckedIn && !passenger.IsBoarded && !passenger.IsNoShow);
        }
    }

    private static long GetForecastRank(string routeKey, int passengerId)
    {
        var stableSeed = $"{routeKey}|{passengerId}".Aggregate(
            23,
            (current, character) => unchecked((current * 37) + character));
        return Math.Abs((long)stableSeed);
    }

    private void UpdateLatePassengerStates()
    {
        var now = _operationsClock.Now;
        var lateWindowStarts = TurnaroundSchedule.FinalBoarding;
        var noShowDeadline = new[]
        {
            lateWindowStarts.Add(LatePassengerGracePeriod),
            TurnaroundSchedule.GateClose
        }.Min();
        foreach (var passenger in PassengerRecords)
        {
            passenger.RefreshOperationalState();
            if (passenger.IsCheckedIn || passenger.IsBoarded || passenger.IsNoShow)
            {
                passenger.UpdateLateStatus(false, 0);
                continue;
            }

            if (now >= noShowDeadline)
            {
                if (_passengers.MarkPassengerNoShow(passenger.PassengerId))
                {
                    passenger.MarkNoShow();
                }

                continue;
            }

            var isLate = now >= lateWindowStarts;
            var remainingMinutes = isLate
                ? Math.Max(1, (int)Math.Ceiling((noShowDeadline - now).TotalMinutes))
                : 0;
            passenger.UpdateLateStatus(isLate, remainingMinutes);
        }

        if (_passengers.BoardingState == BoardingRunState.Complete && NoShowPassengers > 0)
        {
            OperationMessage = $"Boarding completed with {BoardedPassengers} boarded and {NoShowPassengers} no-show passenger(s).";
        }
    }

    private void NotifyOperationalMetrics()
    {
        OnPropertyChanged(nameof(TotalPassengers));
        OnPropertyChanged(nameof(HasPassengerList));
        OnPropertyChanged(nameof(IsPassengerListEmpty));
        OnPropertyChanged(nameof(PassengerListStatus));
        OnPropertyChanged(nameof(CheckedInPassengers));
        OnPropertyChanged(nameof(BoardedPassengers));
        OnPropertyChanged(nameof(LatePassengers));
        OnPropertyChanged(nameof(NoShowPassengers));
        OnPropertyChanged(nameof(NoShowForecast));
        OnPropertyChanged(nameof(ForecastNoShowRate));
        OnPropertyChanged(nameof(ForecastNoShowPassengers));
        OnPropertyChanged(nameof(NoShowForecastSummary));
        OnPropertyChanged(nameof(NoShowForecastCompact));
        OnPropertyChanged(nameof(TotalBags));
        OnPropertyChanged(nameof(PlannedBags));
        OnPropertyChanged(nameof(LoadedBags));
        OnPropertyChanged(nameof(AwaitingBags));
        OnPropertyChanged(nameof(PlannedBaggageWeightKg));
        OnPropertyChanged(nameof(LoadedBaggageWeightKg));
        OnPropertyChanged(nameof(BaggageWeightDeltaKg));
        OnPropertyChanged(nameof(BaggageDiscrepancyPassengers));
        OnPropertyChanged(nameof(HasBaggageDiscrepancies));
        OnPropertyChanged(nameof(BaggageReconciliationLabel));
        OnPropertyChanged(nameof(BaggageReconciliationColor));
        OnPropertyChanged(nameof(CheckedInPercent));
        OnPropertyChanged(nameof(BoardedPercent));
        OnPropertyChanged(nameof(BagsLoadedPercent));
        OnPropertyChanged(nameof(FirstCount));
        OnPropertyChanged(nameof(ClubWorldCount));
        OnPropertyChanged(nameof(WorldTravellerPlusCount));
        OnPropertyChanged(nameof(WorldTravellerCount));
        OnPropertyChanged(nameof(BoardingProgressText));
        OnPropertyChanged(nameof(BoardingStatusLabel));
        OnPropertyChanged(nameof(BoardingStatusColor));
        OnPropertyChanged(nameof(UnissuedBoardingPasses));
        OnPropertyChanged(nameof(ReadyBoardingPasses));
        OnPropertyChanged(nameof(PrintedBoardingPasses));
        OnPropertyChanged(nameof(ReprintBoardingPasses));
    }

    public void RefreshOperationalClock()
    {
        UpdateLatePassengerStates();
        OnPropertyChanged(nameof(CurrentClockTime));
        OnPropertyChanged(nameof(CurrentClockDate));
        OnPropertyChanged(nameof(ClockSourceLabel));
        OnPropertyChanged(nameof(TimelinePhaseLabel));
        OnPropertyChanged(nameof(TimelinePhaseColor));
        RefreshTimelineEvents();
    }

    private void HandleClockTick(object? sender, EventArgs e) => RefreshOperationalClock();

    private void RefreshTimelineEvents()
    {
        if (IsArrivalMode)
        {
            RefreshArrivalTimelineEvents();
            return;
        }

        if (!IsSimBriefSynced)
        {
            TimelineEvents[0].Update("Awaiting SimBrief plan", FlightTimelineEventState.Current);
            for (var index = 1; index < TimelineEvents.Count; index++)
            {
                TimelineEvents[index].Update("—", FlightTimelineEventState.Pending);
            }

            return;
        }

        var schedule = TurnaroundSchedule;
        var stage = schedule.GetStage(_operationsClock.Now);
        var activeIndex = stage switch
        {
            TurnaroundStage.AwaitingTurnaround or TurnaroundStage.Turnaround => 1,
            TurnaroundStage.GateOpen => 2,
            TurnaroundStage.Boarding => 3,
            TurnaroundStage.GateClosing => 4,
            _ => 5
        };

        TimelineEvents[0].Update(SimBriefImportLabel, FlightTimelineEventState.Complete);
        TimelineEvents[1].Update(FormatTime(schedule.TurnaroundStart), TimelineState(1, activeIndex));
        TimelineEvents[2].Update(FormatTime(schedule.GateOpen), TimelineState(2, activeIndex));
        TimelineEvents[3].Update(FormatTime(schedule.BoardingStart), TimelineState(3, activeIndex));
        TimelineEvents[4].Update(FormatTime(schedule.GateClose), TimelineState(4, activeIndex));
        TimelineEvents[5].Update(FormatTime(schedule.Departure), TimelineState(5, activeIndex));
    }

    private void RefreshArrivalTimelineEvents()
    {
        var phase = _liveFlightPhase.ToUpperInvariant();
        var activeIndex = _hasLanded ? 4 : phase switch
        {
            var value when value.Contains("CLIMB", StringComparison.Ordinal) => 1,
            var value when value.Contains("CRUISE", StringComparison.Ordinal) => 2,
            var value when value.Contains("DESCENT", StringComparison.Ordinal) || value.Contains("APPROACH", StringComparison.Ordinal) => 3,
            _ => 0
        };
        var altitudeLabel = _liveAltitudeFeet > 0d ? $"{_liveAltitudeFeet:N0} ft" : "LIVE";
        TimelineEvents[0].Update("Departure", ScheduledDeparture, FlightTimelineEventState.Complete);
        TimelineEvents[1].Update("Climb", activeIndex == 1 ? altitudeLabel : "", TimelineState(1, activeIndex));
        TimelineEvents[2].Update("Cruise", activeIndex == 2 ? altitudeLabel : "", TimelineState(2, activeIndex));
        TimelineEvents[3].Update("Descent", activeIndex == 3 ? altitudeLabel : "", TimelineState(3, activeIndex));
        TimelineEvents[4].Update("Arrival", _hasLanded ? DestinationIata : "—", TimelineState(4, activeIndex));
        var deboardingState = _passengers.BoardingState == BoardingRunState.DeboardingComplete
            ? FlightTimelineEventState.Complete
            : _passengers.BoardingState == BoardingRunState.Deboarding
                ? FlightTimelineEventState.Current
                : FlightTimelineEventState.Pending;
        TimelineEvents[5].Update("Deboarding", deboardingState == FlightTimelineEventState.Complete ? "Complete" : "—", deboardingState);
    }

    private DateTimeOffset ResolveScheduledDeparture()
    {
        if (_passengers.ImportedScheduledDepartureLocal is { } importedDeparture)
        {
            return importedDeparture;
        }

        var fallback = TimeOnly.TryParse(
            _settings.ScheduledDepartureLocal,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var parsed)
            ? parsed
            : new TimeOnly(18, 30);
        var current = _operationsClock.Now;
        return new DateTimeOffset(
            current.Year,
            current.Month,
            current.Day,
            fallback.Hour,
            fallback.Minute,
            0,
            current.Offset);
    }

    private static FlightTimelineEventState TimelineState(int index, int activeIndex) => index < activeIndex
        ? FlightTimelineEventState.Complete
        : index == activeIndex
            ? FlightTimelineEventState.Current
            : FlightTimelineEventState.Pending;

    private static string FormatTime(DateTimeOffset value) =>
        value.ToString("HH:mm", CultureInfo.InvariantCulture);

    private static int Percentage(int value, int total) => total <= 0 ? 0 : (int)Math.Round(value * 100d / total);

    private static string NormalizeAirport(string value) => value.Trim().ToUpperInvariant() switch
    {
        "EGLL" => "LHR",
        "KJFK" => "JFK",
        var airport when airport.Length > 3 => airport[^3..],
        var airport => airport
    };

    private static string AirportName(string iata) => iata switch
    {
        "LHR" => "London Heathrow",
        "JFK" => "New York JFK",
        "OSL" => "Oslo Gardermoen",
        "LGW" => "London Gatwick",
        _ => iata
    };
}

public enum FlightTimelineEventState
{
    Pending,
    Current,
    Complete
}

public sealed class FlightTimelineEventViewModel : ObservableObject
{
    private string _timeLabel = "--:--";
    private FlightTimelineEventState _state;

    public FlightTimelineEventViewModel(string label, string glyph)
    {
        Label = label;
        Glyph = glyph;
    }

    public string Label { get; private set; }
    public string Glyph { get; }

    public string TimeLabel
    {
        get => _timeLabel;
        private set => SetProperty(ref _timeLabel, value);
    }

    public FlightTimelineEventState State
    {
        get => _state;
        private set
        {
            if (!SetProperty(ref _state, value))
            {
                return;
            }

            OnPropertyChanged(nameof(DisplayGlyph));
            OnPropertyChanged(nameof(MarkerBackground));
            OnPropertyChanged(nameof(MarkerBorderBrush));
            OnPropertyChanged(nameof(MarkerForeground));
            OnPropertyChanged(nameof(MarkerBorderThickness));
            OnPropertyChanged(nameof(TimeForeground));
        }
    }

    public string DisplayGlyph => State == FlightTimelineEventState.Complete ? "\uE73E" : Glyph;
    public string MarkerBackground => State switch
    {
        FlightTimelineEventState.Complete => "#245F2A",
        FlightTimelineEventState.Current => "#16345A",
        _ => "#26364D"
    };
    public string MarkerBorderBrush => State switch
    {
        FlightTimelineEventState.Complete => "#397842",
        FlightTimelineEventState.Current => "#63B9FF",
        _ => "#354961"
    };
    public string MarkerForeground => State switch
    {
        FlightTimelineEventState.Complete => "#70E05B",
        FlightTimelineEventState.Current => "#8FC8FF",
        _ => "#8DA0B8"
    };
    public double MarkerBorderThickness => State == FlightTimelineEventState.Current ? 2d : 1d;
    public string TimeForeground => State == FlightTimelineEventState.Current ? "#FFFFFF" : "#8DA0B8";

    public void Update(string timeLabel, FlightTimelineEventState state)
    {
        TimeLabel = timeLabel;
        State = state;
    }

    public void Update(string label, string timeLabel, FlightTimelineEventState state)
    {
        if (!string.Equals(Label, label, StringComparison.Ordinal))
        {
            Label = label;
            OnPropertyChanged(nameof(Label));
        }
        Update(timeLabel, state);
    }
}

public sealed class GatePassengerViewModel : ObservableObject
{
    private readonly PassengerManifestEntryViewModel _source;
    private IReadOnlyList<TicketQrCell>? _qrCells;
    private IReadOnlyList<TicketBarcodeCell>? _boardingBarcodeCells;
    private bool _isCheckedIn;
    private bool _isBoarded;
    private bool _isLate;
    private bool _isNoShow;
    private bool _checkInConfirmed;
    private bool _isForecastNoShowCandidate;
    private bool _isUpdatingBags;
    private int _lateMinutesRemaining;
    private bool _manuallyBoarded;
    private string _boardingPassStatus;
    private string _lastPrintedLabel = "—";

    public GatePassengerViewModel(PassengerManifestEntryViewModel source, int generationSeed)
    {
        _source = source;
        var random = new Random(generationSeed + (source.PassengerId * 7_919) + source.SeatNumber.Sum(character => character * 31));
        _isCheckedIn = true;
        CheckedBagRecords = [];
        for (var bagIndex = 1; bagIndex <= source.CheckedBags; bagIndex++)
        {
            var bag = new PassengerCheckedBagViewModel(
                $"125{source.PassengerId:0000}{bagIndex:00}",
                12 + random.Next(13),
                source.PassengerId % 4 == 0 ? PassengerCheckedBagState.AwaitingLoading : PassengerCheckedBagState.Loaded);
            bag.PropertyChanged += HandleCheckedBagPropertyChanged;
            CheckedBagRecords.Add(bag);
        }
        _boardingPassStatus = source.PassengerId % 19 == 0
            ? "Reprint Required"
            : source.PassengerId % 7 == 0
                ? "Unissued"
                : _isCheckedIn ? "Printed" : "Ready to Print";
        TicketNumber = $"125{random.NextInt64(10_000_000_000L):0000000000}";
        SequenceNumber = ((source.PassengerId * 29) % 999 + 1).ToString("000", CultureInfo.InvariantCulture);
        DateOfBirth = DateOnly.FromDateTime(DateTime.Today)
            .AddYears(-source.Age)
            .AddDays(-random.Next(1, 340))
            .ToString("dd MMM yyyy", CultureInfo.InvariantCulture);
        DocumentNumber = random.Next(10_000_000, 99_999_999).ToString(CultureInfo.InvariantCulture);
        ExecutiveClubNumber = random.NextInt64(1_000_000_000L, 9_999_999_999L).ToString(CultureInfo.InvariantCulture);
        Email = source.Email;
        Phone = $"+44 7700 {random.Next(100000, 999999)}";
        RefreshOperationalState();
    }

    public int PassengerId => _source.PassengerId;
    public string PassengerNumber => _source.PassengerNumber;
    public string FullName => _source.FullName;
    public string FullNameUpper => FullName.ToUpperInvariant();
    public string BookingReference => _source.BookingReference;
    public string SeatNumber => _source.SeatNumber;
    public string CabinClassName => _source.CabinClassName;
    public string CabinMarketingName => CabinClassName switch
    {
        "First" => "First",
        "Business" => "Club World",
        "Premium Economy" => "World Traveller Plus",
        _ => "World Traveller"
    };
    public string CabinCode => CabinMarketingName switch
    {
        "First" => "F",
        "Club World" => "CW",
        "World Traveller Plus" => "W+",
        _ => "WT"
    };
    public int BoardingGroup => _source.BoardingGroup;
    public int Age => _source.Age;
    public string Nationality => _source.Nationality;
    public string TravelPurpose => _source.TravelPurpose;
    public string FrequentFlyerTier => _source.FrequentFlyerTier;
    public string ClubMembershipLabel => FrequentFlyerTier == "None"
        ? "No Executive Club membership"
        : $"Executive Club {FrequentFlyerTier}";
    public bool IsClubMember => FrequentFlyerTier != "None";
    public int CheckedBags => _source.CheckedBags;
    public ObservableCollection<PassengerCheckedBagViewModel> CheckedBagRecords { get; }
    public int LoadedBagCount => CheckedBagRecords.Count(bag => bag.State == PassengerCheckedBagState.Loaded);
    public int AwaitingBagCount => CheckedBagRecords.Count(bag => bag.State == PassengerCheckedBagState.AwaitingLoading);
    public int OffloadedBagCount => CheckedBagRecords.Count(bag => bag.State == PassengerCheckedBagState.OffloadedNoShow);
    public int PlannedBaggageWeightKg => CheckedBagRecords.Sum(bag => bag.WeightKg);
    public int LoadedBaggageWeightKg => CheckedBagRecords
        .Where(bag => bag.State == PassengerCheckedBagState.Loaded)
        .Sum(bag => bag.WeightKg);
    public int AwaitingBaggageWeightKg => CheckedBagRecords
        .Where(bag => bag.State == PassengerCheckedBagState.AwaitingLoading)
        .Sum(bag => bag.WeightKg);
    public int EstimatedBagCount => CheckedBagRecords.Count(bag => bag.IsEstimatedWeight);
    public string Assistance => _source.Assistance;
    public string AssistanceLabel => Assistance == "None" ? "—" : Assistance;
    public string TicketNumber { get; }
    public string SequenceNumber { get; }
    public string DateOfBirth { get; }
    public string DocumentNumber { get; }
    public string ExecutiveClubNumber { get; }
    public string Email { get; }
    public string Phone { get; }
    public IReadOnlyList<TicketQrCell> QrCells => _qrCells ??=
        BuildQrCells($"{BookingReference}|{SeatNumber}|{TicketNumber}|{SequenceNumber}");
    public IReadOnlyList<TicketBarcodeCell> BoardingBarcodeCells => _boardingBarcodeCells ??=
        BuildBoardingBarcodeCells($"{BookingReference}|{SeatNumber}|{TicketNumber}|{SequenceNumber}");

    public bool IsCheckedIn
    {
        get => _isCheckedIn;
        set
        {
            if (SetProperty(ref _isCheckedIn, value))
            {
                if (value)
                {
                    UpdateLateStatus(false, 0);
                }

                OnPropertyChanged(nameof(CheckInLabel));
                OnPropertyChanged(nameof(CheckInColor));
                NotifyBaggagePresentationChanged();
            }
        }
    }

    public bool IsLate
    {
        get => _isLate;
        private set
        {
            if (SetProperty(ref _isLate, value))
            {
                OnPropertyChanged(nameof(CheckInLabel));
                OnPropertyChanged(nameof(CheckInColor));
            }
        }
    }

    public bool IsNoShow
    {
        get => _isNoShow;
        private set
        {
            if (SetProperty(ref _isNoShow, value))
            {
                OnPropertyChanged(nameof(CheckInLabel));
                OnPropertyChanged(nameof(CheckInColor));
                OnPropertyChanged(nameof(BoardingLabel));
                OnPropertyChanged(nameof(BoardingColor));
                NotifyBaggagePresentationChanged();
            }
        }
    }

    public bool IsForecastNoShowCandidate
    {
        get => _isForecastNoShowCandidate;
        private set => SetProperty(ref _isForecastNoShowCandidate, value);
    }

    public bool IsBagLoaded
    {
        get => CheckedBags == 0 || LoadedBagCount == CheckedBags;
        set => SetAllBagsState(value ? PassengerCheckedBagState.Loaded : PassengerCheckedBagState.AwaitingLoading);
    }

    public bool IsBoarded
    {
        get => _isBoarded;
        private set
        {
            if (SetProperty(ref _isBoarded, value))
            {
                OnPropertyChanged(nameof(BoardingLabel));
                OnPropertyChanged(nameof(BoardingColor));
                NotifyBaggagePresentationChanged();
            }
        }
    }

    public string BoardingPassStatus
    {
        get => _boardingPassStatus;
        set
        {
            if (SetProperty(ref _boardingPassStatus, value))
            {
                OnPropertyChanged(nameof(BoardingPassStatusColor));
            }
        }
    }

    public string LastPrintedLabel
    {
        get => _lastPrintedLabel;
        set => SetProperty(ref _lastPrintedLabel, value);
    }

    public string CheckInLabel => IsNoShow
        ? "✕ No Show"
        : IsCheckedIn
            ? "✓ Checked In"
            : IsLate ? $"Late · {_lateMinutesRemaining} min" : "Not Checked In";
    public string CheckInColor => IsCheckedIn ? "#58E68A" : IsLate ? "#F0C64E" : "#FF6666";
    public string BoardingLabel => IsNoShow ? "✕ Not Boarded" : IsBoarded ? "✓ Boarded" : "◷ Waiting";
    public string BoardingColor => IsNoShow ? "#FF6666" : IsBoarded ? "#58E68A" : "#F0C64E";
    public bool HasBaggageDiscrepancy => CheckedBags > 0 &&
        ((IsBoarded && LoadedBagCount != CheckedBags) ||
         (IsNoShow && LoadedBagCount > 0) ||
         (!IsCheckedIn && LoadedBagCount > 0));
    public string BaggageCompactLabel => CheckedBags == 0
        ? "No hold bag"
        : HasBaggageDiscrepancy
            ? $"⚠ Discrepancy {LoadedBagCount}/{CheckedBags}"
            : OffloadedBagCount == CheckedBags
                ? "Offloaded · No-show"
                : IsBagLoaded
                    ? $"✓ Loaded {LoadedBagCount}/{CheckedBags}"
                    : $"Awaiting {AwaitingBagCount}/{CheckedBags}";
    public string BaggageLabel => CheckedBags == 0
        ? "No checked baggage — carry-on not tracked separately"
        : HasBaggageDiscrepancy
            ? $"Baggage discrepancy — {LoadedBagCount}/{CheckedBags} loaded"
            : OffloadedBagCount == CheckedBags
                ? $"Offloaded — passenger no-show · {PlannedBaggageWeightKg} kg"
                : IsBagLoaded
                    ? $"Loaded — {LoadedBagCount} bag(s) · {LoadedBaggageWeightKg} kg"
                    : $"Awaiting loading — {AwaitingBagCount} bag(s) · {AwaitingBaggageWeightKg} kg";
    public string BaggageWeightLabel => CheckedBags == 0
        ? "0 kg hold baggage"
        : $"{PlannedBaggageWeightKg} kg planned · {LoadedBaggageWeightKg} kg loaded" +
          (EstimatedBagCount > 0 ? $" · {EstimatedBagCount} est." : string.Empty);
    public string BaggageColor => CheckedBags == 0
        ? "#8DA0B8"
        : HasBaggageDiscrepancy
            ? "#FF6666"
            : OffloadedBagCount == CheckedBags
                ? "#8FC8FF"
                : IsBagLoaded ? "#58E68A" : "#F0C64E";
    public string BaggageActionLabel => CheckedBags == 0
        ? "No Hold Bag"
        : IsNoShow ? "Offloaded" : IsBagLoaded ? "Mark Awaiting" : "Confirm Load";
    public string BaggageOperationMessage => CheckedBags == 0
        ? $"{FullName} has no checked baggage. Carry-on is represented by the passenger standard mass."
        : IsBagLoaded
            ? $"{LoadedBagCount} bag(s) for {FullName} confirmed loaded at {LoadedBaggageWeightKg} kg."
            : OffloadedBagCount == CheckedBags
                ? $"{CheckedBags} bag(s) for {FullName} are offloaded because the passenger is a no-show."
                : $"{AwaitingBagCount} bag(s) for {FullName} are awaiting aircraft loading.";
    public string BoardingPassStatusColor => BoardingPassStatus switch
    {
        "Printed" => "#58E68A",
        "Ready to Print" => "#55AEFF",
        "Reprint Required" => "#FF6666",
        _ => "#F0C64E"
    };

    public void MarkManuallyBoarded()
    {
        _manuallyBoarded = true;
        MarkCheckedIn();
        IsBoarded = true;
    }

    public void MarkCheckedIn()
    {
        _checkInConfirmed = true;
        IsCheckedIn = true;
    }

    public void ApplyNoShowForecastCandidate(bool isCandidate)
    {
        IsForecastNoShowCandidate = isCandidate;
        if (_checkInConfirmed || IsBoarded || IsNoShow)
        {
            return;
        }

        IsCheckedIn = !isCandidate;
        if (isCandidate)
        {
            SetAllBagsState(PassengerCheckedBagState.AwaitingLoading);
            MarkBaggageWeightsEstimated();
        }
        if (isCandidate && BoardingPassStatus == "Printed")
        {
            BoardingPassStatus = "Ready to Print";
        }
        else if (!isCandidate && BoardingPassStatus == "Ready to Print")
        {
            BoardingPassStatus = "Printed";
        }
    }

    public void RefreshOperationalState()
    {
        var sourceBoarded = _source.StatusLabel is "Walking to seat" or "Occupying seat" or "Seated · secured" or "Deboarded";
        if (sourceBoarded && !IsNoShow)
        {
            MarkCheckedIn();
        }

        IsBoarded = !IsNoShow && (_manuallyBoarded || sourceBoarded);
    }

    public void UpdateLateStatus(bool isLate, int minutesRemaining)
    {
        var normalizedMinutes = isLate ? Math.Max(1, minutesRemaining) : 0;
        if (_lateMinutesRemaining != normalizedMinutes)
        {
            _lateMinutesRemaining = normalizedMinutes;
            OnPropertyChanged(nameof(CheckInLabel));
        }

        IsLate = isLate && !IsCheckedIn && !IsNoShow;
    }

    public void MarkNoShow()
    {
        UpdateLateStatus(false, 0);
        IsNoShow = true;
        IsBoarded = false;
        if (CheckedBags > 0)
        {
            SetAllBagsState(PassengerCheckedBagState.OffloadedNoShow);
        }
    }

    public void ToggleBaggageLoadedState()
    {
        if (CheckedBags == 0 || IsNoShow)
        {
            return;
        }

        SetAllBagsState(IsBagLoaded
            ? PassengerCheckedBagState.AwaitingLoading
            : PassengerCheckedBagState.Loaded);
    }

    private void SetAllBagsState(PassengerCheckedBagState state)
    {
        _isUpdatingBags = true;
        try
        {
            foreach (var bag in CheckedBagRecords)
            {
                bag.State = state;
            }
        }
        finally
        {
            _isUpdatingBags = false;
        }

        NotifyBaggageStateChanged();
    }

    private void MarkBaggageWeightsEstimated()
    {
        _isUpdatingBags = true;
        try
        {
            foreach (var bag in CheckedBagRecords)
            {
                bag.MarkWeightEstimated();
            }
        }
        finally
        {
            _isUpdatingBags = false;
        }

        NotifyBaggageStateChanged();
    }

    private void HandleCheckedBagPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!_isUpdatingBags)
        {
            NotifyBaggageStateChanged();
        }
    }

    private void NotifyBaggageStateChanged()
    {
        OnPropertyChanged(nameof(IsBagLoaded));
        OnPropertyChanged(nameof(LoadedBagCount));
        OnPropertyChanged(nameof(AwaitingBagCount));
        OnPropertyChanged(nameof(OffloadedBagCount));
        OnPropertyChanged(nameof(PlannedBaggageWeightKg));
        OnPropertyChanged(nameof(LoadedBaggageWeightKg));
        OnPropertyChanged(nameof(AwaitingBaggageWeightKg));
        OnPropertyChanged(nameof(EstimatedBagCount));
        NotifyBaggagePresentationChanged();
    }

    private void NotifyBaggagePresentationChanged()
    {
        OnPropertyChanged(nameof(HasBaggageDiscrepancy));
        OnPropertyChanged(nameof(BaggageCompactLabel));
        OnPropertyChanged(nameof(BaggageLabel));
        OnPropertyChanged(nameof(BaggageWeightLabel));
        OnPropertyChanged(nameof(BaggageColor));
        OnPropertyChanged(nameof(BaggageActionLabel));
        OnPropertyChanged(nameof(BaggageOperationMessage));
    }

    private static string BuildEmail(string fullName)
    {
        var parts = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length < 2
            ? $"{fullName.ToLowerInvariant()}@example.test"
            : $"{parts[0].ToLowerInvariant()}.{parts[^1].ToLowerInvariant()}@example.test";
    }

    private static IReadOnlyList<TicketQrCell> BuildQrCells(string value)
    {
        const int size = 21;
        var cells = new List<TicketQrCell>(size * size);
        var seed = value.Aggregate(17, (current, character) => unchecked((current * 31) + character));
        var random = new Random(seed);
        for (var row = 0; row < size; row++)
        {
            for (var column = 0; column < size; column++)
            {
                var finder = IsFinderCell(row, column, 0, 0) ||
                             IsFinderCell(row, column, 0, size - 7) ||
                             IsFinderCell(row, column, size - 7, 0);
                cells.Add(new TicketQrCell(finder || random.Next(100) < 46));
            }
        }

        return cells;
    }

    private static IReadOnlyList<TicketBarcodeCell> BuildBoardingBarcodeCells(string value)
    {
        const int rows = 9;
        const int columns = 58;
        var seed = value.Aggregate(23, (current, character) => unchecked((current * 37) + character));
        var random = new Random(seed);
        var cells = new List<TicketBarcodeCell>(rows * columns);
        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                var startGuard = column is 0 or 1 or 3;
                var stopGuard = column is columns - 1 or columns - 2 or columns - 4;
                var rowMarker = column == 5 + (row % 3) || column == columns - 7 - (row % 2);
                cells.Add(new TicketBarcodeCell(startGuard || stopGuard || rowMarker || random.Next(100) < 43));
            }
        }

        return cells;
    }

    private static bool IsFinderCell(int row, int column, int startRow, int startColumn)
    {
        var localRow = row - startRow;
        var localColumn = column - startColumn;
        if (localRow is < 0 or > 6 || localColumn is < 0 or > 6)
        {
            return false;
        }

        return localRow is 0 or 6 || localColumn is 0 or 6 ||
               localRow is >= 2 and <= 4 && localColumn is >= 2 and <= 4;
    }
}

public enum PassengerCheckedBagState
{
    AwaitingLoading,
    Loaded,
    OffloadedNoShow
}

public sealed class PassengerCheckedBagViewModel : ObservableObject
{
    private int _weightKg;
    private PassengerCheckedBagState _state;
    private bool _isEstimatedWeight;

    public PassengerCheckedBagViewModel(string tagNumber, int weightKg, PassengerCheckedBagState state)
    {
        TagNumber = tagNumber;
        _weightKg = Math.Clamp(weightKg, 1, 50);
        _state = state;
        _isEstimatedWeight = state != PassengerCheckedBagState.Loaded;
    }

    public string TagNumber { get; }

    public int WeightKg
    {
        get => _weightKg;
        set
        {
            var weightChanged = SetProperty(ref _weightKg, Math.Clamp(value, 1, 50));
            var sourceChanged = _isEstimatedWeight;
            _isEstimatedWeight = false;
            if (weightChanged || sourceChanged)
            {
                OnPropertyChanged(nameof(WeightLabel));
                OnPropertyChanged(nameof(IsEstimatedWeight));
                OnPropertyChanged(nameof(WeightSourceLabel));
            }
        }
    }

    public bool IsEstimatedWeight => _isEstimatedWeight;

    public void MarkWeightEstimated()
    {
        if (_isEstimatedWeight)
        {
            return;
        }

        _isEstimatedWeight = true;
        OnPropertyChanged(nameof(IsEstimatedWeight));
        OnPropertyChanged(nameof(WeightLabel));
        OnPropertyChanged(nameof(WeightSourceLabel));
    }

    public PassengerCheckedBagState State
    {
        get => _state;
        set
        {
            if (SetProperty(ref _state, value))
            {
                if (value == PassengerCheckedBagState.Loaded && _isEstimatedWeight)
                {
                    _isEstimatedWeight = false;
                    OnPropertyChanged(nameof(IsEstimatedWeight));
                    OnPropertyChanged(nameof(WeightLabel));
                    OnPropertyChanged(nameof(WeightSourceLabel));
                }

                OnPropertyChanged(nameof(CompactStatusLabel));
                OnPropertyChanged(nameof(StatusLabel));
                OnPropertyChanged(nameof(StatusColor));
            }
        }
    }

    public string WeightLabel => IsEstimatedWeight ? $"{WeightKg} kg est." : $"{WeightKg} kg";
    public string WeightSourceLabel => IsEstimatedWeight ? "Estimated until the load scan or manual weight entry" : "Actual confirmed bag weight";
    public string CompactStatusLabel => State switch
    {
        PassengerCheckedBagState.Loaded => "Loaded",
        PassengerCheckedBagState.OffloadedNoShow => "Offloaded",
        _ => "Awaiting load"
    };
    public string StatusLabel => State switch
    {
        PassengerCheckedBagState.Loaded => "Loaded",
        PassengerCheckedBagState.OffloadedNoShow => "Offloaded — no-show",
        _ => "Awaiting loading"
    };
    public string StatusColor => State switch
    {
        PassengerCheckedBagState.Loaded => "#58E68A",
        PassengerCheckedBagState.OffloadedNoShow => "#8FC8FF",
        _ => "#F0C64E"
    };
}

public sealed record TicketQrCell(bool IsDark);

public sealed record TicketBarcodeCell(bool IsDark);
