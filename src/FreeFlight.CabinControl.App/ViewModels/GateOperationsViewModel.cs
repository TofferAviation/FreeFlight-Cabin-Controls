using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Input;
using FreeFlight.CabinControl.App.Infrastructure;
using FreeFlight.CabinControl.Core.Configuration;
using FreeFlight.CabinControl.Core.Passengers;

namespace FreeFlight.CabinControl.App.ViewModels;

public sealed class GateOperationsViewModel : PageViewModel, IDisposable
{
    private readonly AppSettings _settings;
    private readonly PassengerFlowViewModel _passengers;
    private readonly Dictionary<int, GatePassengerViewModel> _passengersById = [];
    private GatePassengerViewModel? _selectedPassenger;
    private string _searchText = string.Empty;
    private string _selectedCabinFilter = "All Passengers";
    private bool _isGateOpen;
    private bool _gateHasClosed;
    private string _operationMessage = "Flight and passenger data are ready for gate preparation.";

    public GateOperationsViewModel(AppSettings settings, PassengerFlowViewModel passengers)
        : base("Overview", "Gate preparation, boarding readiness, and live passenger operations")
    {
        _settings = settings;
        _passengers = passengers;
        CabinFilters = ["All Passengers", "First", "Club World", "World Traveller Plus", "World Traveller"];
        ToggleGateCommand = new RelayCommand(_ => ToggleGate());
        StartManageBoardingCommand = new RelayCommand(_ => StartManageBoarding());
        SelectPassengerCommand = new RelayCommand(SelectPassenger);
        CheckInPassengerCommand = new RelayCommand(CheckInPassenger);
        BoardPassengerCommand = new RelayCommand(BoardPassenger);
        ToggleBagLoadedCommand = new RelayCommand(ToggleBagLoaded);
        PrintBoardingPassCommand = new RelayCommand(PrintBoardingPass);
        MarkBoardingPassIssuedCommand = new RelayCommand(MarkBoardingPassIssued);
        ImportSimBriefCommand = new AsyncRelayCommand(ImportSimBriefAsync, HandleImportError);

        _passengers.PassengerManifest.CollectionChanged += HandleManifestCollectionChanged;
        _passengers.PropertyChanged += HandlePassengerFlowPropertyChanged;
        RebuildPassengerRecords();
    }

    public PassengerFlowViewModel PassengerFlow => _passengers;
    public ObservableCollection<GatePassengerViewModel> PassengerRecords { get; } = [];
    public ObservableCollection<GatePassengerViewModel> VisiblePassengers { get; } = [];
    public IReadOnlyList<string> CabinFilters { get; }
    public ICommand ToggleGateCommand { get; }
    public ICommand StartManageBoardingCommand { get; }
    public ICommand SelectPassengerCommand { get; }
    public ICommand CheckInPassengerCommand { get; }
    public ICommand BoardPassengerCommand { get; }
    public ICommand ToggleBagLoadedCommand { get; }
    public ICommand PrintBoardingPassCommand { get; }
    public ICommand MarkBoardingPassIssuedCommand { get; }
    public ICommand ImportSimBriefCommand { get; }

    public GatePassengerViewModel? SelectedPassenger
    {
        get => _selectedPassenger;
        set => SetProperty(ref _selectedPassenger, value);
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
    public string GateNumber => _settings.GateNumber;
    public string GateHeader => $"Gate {GateNumber}";
    public string AircraftName => _passengers.SelectedCabinLayoutProfile.Layout switch
    {
        PassengerCabinLayout.BritishAirways777200Er => "Boeing 777-200ER",
        PassengerCabinLayout.BritishAirways777300 => "Boeing 777-300",
        _ => "FlightFactor 777 v2"
    };
    public bool IsSimBriefSynced => _passengers.HasSimBriefFlight;
    public string SimBriefConnectionLabel => IsSimBriefSynced ? "SimBrief Synced" : "SimBrief Ready";
    public string SimBriefImportLabel => _passengers.LastSimBriefSyncLabel;

    public int TotalPassengers => PassengerRecords.Count;
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
    public bool CanBoardPassengers => IsGateOpen && (!_gateHasClosed || _settings.ManualGateOverride);
    public string ReadinessGateStatus => IsGateOpen ? $"Gate {GateNumber} open for passengers" : $"Gate {GateNumber} assigned";

    public string ScheduledDeparture => NormalizeTime(_settings.ScheduledDepartureLocal, "18:30");
    public string GateOpensAt => OffsetDeparture(-60);
    public string BoardingBeginsAt => OffsetDeparture(-_settings.BoardingStartMinutesBeforeDeparture);
    public string FinalBoardingAt => OffsetDeparture(-_settings.FinalBoardingMinutesBeforeDeparture);
    public string GateClosesAt => OffsetDeparture(-_settings.GateCloseMinutesBeforeDeparture);

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
        OnPropertyChanged(nameof(GateNumber));
        OnPropertyChanged(nameof(GateHeader));
        OnPropertyChanged(nameof(ScheduledDeparture));
        OnPropertyChanged(nameof(GateOpensAt));
        OnPropertyChanged(nameof(BoardingBeginsAt));
        OnPropertyChanged(nameof(FinalBoardingAt));
        OnPropertyChanged(nameof(GateClosesAt));
        OnPropertyChanged(nameof(CanBoardPassengers));
        OnPropertyChanged(nameof(ReadinessGateStatus));
    }

    public void Dispose()
    {
        _passengers.PassengerManifest.CollectionChanged -= HandleManifestCollectionChanged;
        _passengers.PropertyChanged -= HandlePassengerFlowPropertyChanged;
        GC.SuppressFinalize(this);
    }

    private void ToggleGate()
    {
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
        if (ResolvePassenger(parameter) is not { } passenger)
        {
            return;
        }

        passenger.BoardingPassStatus = "Printed";
        passenger.LastPrintedLabel = DateTime.Now.ToString("HH:mm", CultureInfo.InvariantCulture);
        SelectedPassenger = passenger;
        OperationMessage = $"Boarding pass printed for {passenger.FullName} on {_settings.BoardingPassPrinter}.";
        NotifyOperationalMetrics();
    }

    private void MarkBoardingPassIssued(object? parameter)
    {
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
                 nameof(PassengerFlowViewModel.LastSimBriefSyncTime))
        {
            ApplySettings();
            OnPropertyChanged(nameof(IsSimBriefSynced));
            OnPropertyChanged(nameof(SimBriefConnectionLabel));
            OnPropertyChanged(nameof(SimBriefImportLabel));
        }
        else if (e.PropertyName == nameof(PassengerFlowViewModel.SelectedCabinLayoutProfile))
        {
            OnPropertyChanged(nameof(AircraftName));
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

    private string OffsetDeparture(int minutes)
    {
        var departure = TimeOnly.TryParse(ScheduledDeparture, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : new TimeOnly(18, 30);
        return departure.AddMinutes(minutes).ToString("HH:mm", CultureInfo.InvariantCulture);
    }

    private static string NormalizeTime(string value, string fallback) =>
        TimeOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed.ToString("HH:mm", CultureInfo.InvariantCulture)
            : fallback;

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

public sealed class GatePassengerViewModel : ObservableObject
{
    private readonly PassengerManifestEntryViewModel _source;
    private IReadOnlyList<TicketQrCell>? _qrCells;
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
