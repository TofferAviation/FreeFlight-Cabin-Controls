using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Input;
using System.Windows.Threading;
using FreeFlight.CabinControl.App.Infrastructure;
using FreeFlight.CabinControl.App.Services;
using FreeFlight.CabinControl.Core.Configuration;
using FreeFlight.CabinControl.Core.Operations;
using FreeFlight.CabinControl.Core.Passengers;

namespace FreeFlight.CabinControl.App.ViewModels;

public sealed class GateOperationsViewModel : PageViewModel, IDisposable
{
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
    public string GateHeader => $"DEP {GateNumber}  →  ARR {ArrivalGateNumber}";
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
    public int TotalBags => PassengerRecords.Sum(passenger => passenger.CheckedBags);
    public int LoadedBags => PassengerRecords.Where(passenger => passenger.IsBagLoaded).Sum(passenger => passenger.CheckedBags);
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
    public string ReadinessGateStatus => IsGateOpen
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
    public string TimelinePhaseLabel => TurnaroundSchedule.GetStage(_operationsClock.Now) switch
    {
        TurnaroundStage.AwaitingTurnaround => "AWAITING TURNAROUND",
        TurnaroundStage.Turnaround => "TURNAROUND IN PROGRESS",
        TurnaroundStage.GateOpen => "GATE OPEN WINDOW",
        TurnaroundStage.Boarding => "BOARDING WINDOW",
        TurnaroundStage.GateClosing => "GATE CLOSING",
        _ => "DEPARTURE DUE"
    };
    public string TimelinePhaseColor => TurnaroundSchedule.GetStage(_operationsClock.Now) switch
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

        passenger.IsCheckedIn = true;
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

        passenger.IsBagLoaded = !passenger.IsBagLoaded;
        SelectedPassenger = passenger;
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

            PassengerRecords.Clear();
            _passengersById.Clear();
            SelectedPassenger = null;
        }

        if (e.NewItems is not null)
        {
            foreach (var source in e.NewItems.OfType<PassengerManifestEntryViewModel>())
            {
                AddPassengerRecord(source);
            }
        }

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
            nameof(GatePassengerViewModel.IsBagLoaded) or
            nameof(GatePassengerViewModel.BoardingPassStatus))
        {
            NotifyOperationalMetrics();
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
        OnPropertyChanged(nameof(TotalBags));
        OnPropertyChanged(nameof(LoadedBags));
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

        TimelineEvents[0].Update(
            SimBriefImportLabel,
            IsSimBriefSynced
                ? FlightTimelineEventState.Complete
                : stage == TurnaroundStage.AwaitingTurnaround
                    ? FlightTimelineEventState.Current
                    : FlightTimelineEventState.Pending);
        TimelineEvents[1].Update(FormatTime(schedule.TurnaroundStart), TimelineState(1, activeIndex));
        TimelineEvents[2].Update(FormatTime(schedule.GateOpen), TimelineState(2, activeIndex));
        TimelineEvents[3].Update(FormatTime(schedule.BoardingStart), TimelineState(3, activeIndex));
        TimelineEvents[4].Update(FormatTime(schedule.GateClose), TimelineState(4, activeIndex));
        TimelineEvents[5].Update(FormatTime(schedule.Departure), TimelineState(5, activeIndex));
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

    public string Label { get; }
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
}

public sealed class GatePassengerViewModel : ObservableObject
{
    private readonly PassengerManifestEntryViewModel _source;
    private IReadOnlyList<TicketQrCell>? _qrCells;
    private IReadOnlyList<TicketBarcodeCell>? _boardingBarcodeCells;
    private bool _isCheckedIn;
    private bool _isBagLoaded;
    private bool _isBoarded;
    private bool _manuallyBoarded;
    private string _boardingPassStatus;
    private string _lastPrintedLabel = "—";

    public GatePassengerViewModel(PassengerManifestEntryViewModel source, int generationSeed)
    {
        _source = source;
        var random = new Random(generationSeed + (source.PassengerId * 7_919) + source.SeatNumber.Sum(character => character * 31));
        _isCheckedIn = source.PassengerId % 5 != 0;
        _isBagLoaded = source.CheckedBags == 0 || source.PassengerId % 4 != 0;
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
        Email = BuildEmail(source.FullName);
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
                OnPropertyChanged(nameof(CheckInLabel));
                OnPropertyChanged(nameof(CheckInColor));
            }
        }
    }

    public bool IsBagLoaded
    {
        get => _isBagLoaded;
        set
        {
            if (SetProperty(ref _isBagLoaded, value))
            {
                OnPropertyChanged(nameof(BaggageLabel));
                OnPropertyChanged(nameof(BaggageColor));
            }
        }
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

    public string CheckInLabel => IsCheckedIn ? "✓ Checked In" : "Not Checked In";
    public string CheckInColor => IsCheckedIn ? "#58E68A" : "#FF6666";
    public string BoardingLabel => IsBoarded ? "✓ Boarded" : "◷ Waiting";
    public string BoardingColor => IsBoarded ? "#58E68A" : "#F0C64E";
    public string BaggageLabel => CheckedBags == 0 ? "No checked bags" : IsBagLoaded ? "▣ Bag Loaded" : "Bag Not Loaded";
    public string BaggageColor => CheckedBags == 0 ? "#8DA0B8" : IsBagLoaded ? "#58E68A" : "#FF6666";
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
        IsBoarded = true;
    }

    public void RefreshOperationalState()
    {
        var sourceBoarded = _source.StatusLabel is "Walking to seat" or "Occupying seat" or "Seated · secured" or "Deboarded";
        IsBoarded = _manuallyBoarded || sourceBoarded;
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

public sealed record TicketQrCell(bool IsDark);

public sealed record TicketBarcodeCell(bool IsDark);
