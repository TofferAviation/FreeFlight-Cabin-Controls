using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using FreeFlight.CabinControl.App.Infrastructure;

namespace FreeFlight.CabinControl.App.ViewModels;

public sealed class IportDcsViewModel : PageViewModel, IDisposable
{
    public const string CheckInModule = "Check-In";
    public const string BoardingModule = "Boarding";
    public const string SeatmapModule = "Seatmap";
    public const string LoadControlModule = "Load Control";
    public const string LoadPassengerModule = "Load Control — Passenger";
    public const string LoadDeadloadModule = "Load Control — Deadload";
    public const string LoadDistributionModule = "Load Control — Load Distribution";
    public const string LoadDocumentsModule = "Load Control — Documents";
    public const string LoadPwSummaryModule = "Load Control — P/W summary";
    public const string FlightMonitorModule = "Flight Monitor";
    public const string GateControlModule = "Gate Control";
    public const string LostAndFoundModule = "Lost & Found";
    public const string DatabaseManagementModule = "Database mgt";
    public const string AutomationModule = "Autoinformation";
    public const string MyAccountModule = "My Account";
    public const string SupportModule = "Support";
    public const string Res2SupportModule = "Res2 Support";
    public const string MessagesModule = "Messages";

    private readonly GateOperationsViewModel _operations;
    private readonly GateLoginViewModel _gateLogin;
    private readonly Dictionary<string, IportLoadFlightState> _flightStates = new(StringComparer.OrdinalIgnoreCase);
    private string _activeModule = CheckInModule;
    private string _activeRole = "Customer Services";
    private string _passengerLookup = string.Empty;
    private string _manualBoardingInput = string.Empty;
    private string _commandStatus = "Select a passenger or use an action key.";
    private bool _isServiceMenuOpen;
    private IportFlightSummary? _selectedFlight;
    private string _boardingPoint;
    private string _destination;
    private int _dryOperatingWeightKg = 167_800;
    private double _dryOperatingIndex = 45.20;
    private int _takeoffFuelKg = 14_400;
    private int _taxiFuelKg = 1_200;
    private int _tripFuelKg = 11_340;
    private int _additionalWeightKg;
    private bool _isLoadingFlightState;

    public IportDcsViewModel(GateOperationsViewModel operations, GateLoginViewModel gateLogin)
        : base("Iport DCS", "Advanced coded departure-control workspace")
    {
        _operations = operations;
        _gateLogin = gateLogin;
        _boardingPoint = _gateLogin.SelectedStation.Code;
        _destination = _operations.DestinationIata;
        InitializeFlightStates();
        Roles = ["Customer Services", "Load Control", "Flight Control"];
        Modules =
        [
            CheckInModule,
            GateControlModule,
            BoardingModule,
            LostAndFoundModule,
            SeatmapModule,
            LoadControlModule,
            LoadPassengerModule,
            LoadDeadloadModule,
            LoadDistributionModule,
            LoadDocumentsModule,
            LoadPwSummaryModule,
            FlightMonitorModule,
            DatabaseManagementModule,
            AutomationModule,
            MyAccountModule,
            SupportModule,
            Res2SupportModule,
            MessagesModule
        ];
        ServiceMenuEntries =
        [
            IportServiceMenuEntry.Header("Customer Services"),
            IportServiceMenuEntry.Service("Check-In", "Alt + C", CheckInModule),
            IportServiceMenuEntry.Service("Gate Control", "Alt + G", GateControlModule),
            IportServiceMenuEntry.Service("Boarding", "Alt + B", BoardingModule),
            IportServiceMenuEntry.Service("Lost & Found", "Alt + W", LostAndFoundModule),
            IportServiceMenuEntry.Header("Flight Services"),
            IportServiceMenuEntry.Service("Load Control", "Alt + L", LoadControlModule),
            IportServiceMenuEntry.Header("Core Services"),
            IportServiceMenuEntry.Service("Flight Control", "Alt + F", FlightMonitorModule),
            IportServiceMenuEntry.Service("Database mgt", "Alt + D", DatabaseManagementModule),
            IportServiceMenuEntry.Service("Autoinformation", "Alt + A", AutomationModule),
            IportServiceMenuEntry.Service("My Account", "Alt + M", MyAccountModule),
            IportServiceMenuEntry.Service("Support", "Alt + S", SupportModule),
            IportServiceMenuEntry.Service("Res2 Support", "Alt + R", Res2SupportModule),
            IportServiceMenuEntry.Service("Messages", "Alt + T", MessagesModule)
        ];
        SelectModuleCommand = new RelayCommand(SelectModule);
        SelectServiceCommand = new RelayCommand(SelectService);
        ToggleServiceMenuCommand = new RelayCommand(_ => IsServiceMenuOpen = !IsServiceMenuOpen);
        LoadActionCommand = new RelayCommand(parameter =>
        {
            CommandStatus = parameter as string ?? "Load Control action completed.";
        });
        SelectPassengerCommand = new RelayCommand(SelectPassenger);
        FindPassengerCommand = new RelayCommand(_ => FindPassenger(PassengerLookup));
        ManualBoardCommand = new RelayCommand(_ => ManualBoard());
        CheckInCommand = new RelayCommand(_ => RunForSelected(_operations.CheckInPassengerCommand, "Passenger checked in."));
        BoardCommand = new RelayCommand(_ => RunForSelected(_operations.BoardPassengerCommand, "Passenger sent to boarding."));
        PrintCommand = new RelayCommand(_ => RunForSelected(_operations.PrintBoardingPassCommand, "Boarding pass sent to the selected Windows printer."));
        RefreshCommand = new RelayCommand(_ => RefreshAll("Flight and passenger data refreshed."));
        ToggleGateCommand = new RelayCommand(_ =>
        {
            _operations.ToggleGateCommand.Execute(null);
            RefreshAll(_operations.OperationMessage);
        });

        _operations.PropertyChanged += HandleOperationsPropertyChanged;
        _operations.PassengerRecords.CollectionChanged += HandlePassengerRecordsChanged;
        _gateLogin.PropertyChanged += HandleGateLoginPropertyChanged;
        RefreshFlightList();
        RefreshMonitorEvents();
    }

    public GateOperationsViewModel Operations => _operations;

    public IReadOnlyList<string> Roles { get; }

    public IReadOnlyList<string> Modules { get; }

    public IReadOnlyList<IportServiceMenuEntry> ServiceMenuEntries { get; }

    public ObservableCollection<IportFlightSummary> Flights { get; } = [];

    public ObservableCollection<IportMonitorEventViewModel> MonitorEvents { get; } = [];

    public ICommand SelectModuleCommand { get; }

    public ICommand SelectServiceCommand { get; }

    public ICommand ToggleServiceMenuCommand { get; }

    public ICommand LoadActionCommand { get; }

    public ICommand SelectPassengerCommand { get; }

    public ICommand FindPassengerCommand { get; }

    public ICommand ManualBoardCommand { get; }

    public ICommand CheckInCommand { get; }

    public ICommand BoardCommand { get; }

    public ICommand PrintCommand { get; }

    public ICommand RefreshCommand { get; }

    public ICommand ToggleGateCommand { get; }

    public string ActiveModule
    {
        get => _activeModule;
        set
        {
            if (SetProperty(ref _activeModule, value))
            {
                var serviceLabel = ResolveServiceLabel(value);
                if (!string.Equals(_activeRole, serviceLabel, StringComparison.Ordinal))
                {
                    _activeRole = serviceLabel;
                    OnPropertyChanged(nameof(ActiveRole));
                }

                CommandStatus = $"{value} workspace active.";
                OnPropertyChanged(nameof(ActiveServiceLabel));
                OnPropertyChanged(nameof(IportProductLabel));
                OnPropertyChanged(nameof(IportHeaderHeight));
                OnPropertyChanged(nameof(IsLoadControlService));
                OnPropertyChanged(nameof(IsCustomerServiceTabsVisible));
                OnPropertyChanged(nameof(IsLoadControlPlaceholder));
                OnPropertyChanged(nameof(IsServicePlaceholder));
                OnPropertyChanged(nameof(PlaceholderTitle));
                OnPropertyChanged(nameof(PlaceholderDescription));
            }
        }
    }

    public string ActiveRole
    {
        get => _activeRole;
        set
        {
            if (!SetProperty(ref _activeRole, value))
            {
                return;
            }

            ActiveModule = value switch
            {
                "Load Control" => LoadControlModule,
                "Flight Control" => FlightMonitorModule,
                _ => CheckInModule
            };
        }
    }

    public string ActiveServiceLabel => ResolveServiceLabel(ActiveModule);

    public string IportProductLabel => IsLoadControlService ? "flight" : "customer";

    public GridLength IportHeaderHeight => new(IsLoadControlService ? 163d : 118d);

    public bool IsServiceMenuOpen
    {
        get => _isServiceMenuOpen;
        set => SetProperty(ref _isServiceMenuOpen, value);
    }

    public bool IsLoadControlService => IsLoadControlModule(ActiveModule);

    public bool IsCustomerServiceTabsVisible => !IsLoadControlService;

    public bool IsLoadControlPlaceholder => IsLoadControlService && ActiveModule != LoadControlModule;

    public bool IsServicePlaceholder => ActiveModule is GateControlModule or LostAndFoundModule or
        DatabaseManagementModule or AutomationModule or MyAccountModule or SupportModule or
        Res2SupportModule or MessagesModule;

    public string PlaceholderTitle => ActiveModule;

    public string PlaceholderDescription => IsLoadControlPlaceholder
        ? "The genuine iPortflight page is retained in the Load Control workspace and will be completed when its operational reference is supplied."
        : "This service remains available in the authentic Res2 menu. Its operational screen will be added when the real reference page is supplied.";

    public string PassengerLookup
    {
        get => _passengerLookup;
        set => SetProperty(ref _passengerLookup, value);
    }

    public string ManualBoardingInput
    {
        get => _manualBoardingInput;
        set => SetProperty(ref _manualBoardingInput, value);
    }

    public string CommandStatus
    {
        get => _commandStatus;
        private set => SetProperty(ref _commandStatus, value);
    }

    public IportFlightSummary? SelectedFlight
    {
        get => _selectedFlight;
        set
        {
            if (ReferenceEquals(_selectedFlight, value))
            {
                return;
            }

            SaveCurrentFlightState();
            if (_selectedFlight is not null)
            {
                _selectedFlight.IsSelected = false;
            }
            if (!SetProperty(ref _selectedFlight, value) || value is null)
            {
                return;
            }

            value.IsSelected = true;
            LoadFlightState(value.FlightNumber);
            CommandStatus = value.IsLive
                ? $"{value.FlightNumber} live Load Control workspace opened."
                : $"{value.FlightNumber} dispatcher flight workspace opened for inspection and editing.";
            OnPropertyChanged(nameof(SelectedFlightStatusLabel));
            OnPropertyChanged(nameof(SelectedCheckedInPassengers));
            OnPropertyChanged(nameof(SelectedBookedPassengers));
            RefreshDerivedProperties();
        }
    }

    public GatePassengerViewModel? SelectedPassenger
    {
        get => _operations.SelectedPassenger;
        set
        {
            if (ReferenceEquals(_operations.SelectedPassenger, value))
            {
                return;
            }

            _operations.SelectedPassenger = value;
            OnPropertyChanged();
        }
    }

    public bool IsAvailable => _gateLogin.IsAuthenticated;

    public string SignedInUser => string.IsNullOrWhiteSpace(_gateLogin.SignedInStaff) ? "PREVIEW" : _gateLogin.SignedInStaff;

    public string BoardingPoint
    {
        get => _boardingPoint;
        set
        {
            var normalized = NormalizeAirportEntry(value);
            if (SetProperty(ref _boardingPoint, normalized))
            {
                if (!_isLoadingFlightState && SelectedFlight is not null)
                {
                    SelectedFlight.Origin = normalized;
                    SaveCurrentFlightState();
                }
                OnPropertyChanged(nameof(BoardingPointLabel));
            }
        }
    }

    public string Destination
    {
        get => _destination;
        set
        {
            var normalized = NormalizeAirportEntry(value);
            if (SetProperty(ref _destination, normalized))
            {
                if (!_isLoadingFlightState && SelectedFlight is not null)
                {
                    SelectedFlight.Destination = normalized;
                    SaveCurrentFlightState();
                }
            }
        }
    }

    public string BoardingPointLabel => string.Equals(BoardingPoint, _gateLogin.SelectedStation.Code, StringComparison.OrdinalIgnoreCase)
        ? $"{_gateLogin.SelectedStation.DisplayName} / {_operations.GateNumber}"
        : $"{BoardingPoint} / {_operations.GateNumber}";

    public string CurrentClock => _operations.CurrentClockTime;

    public string SelectedFlightStatusLabel => SelectedFlight?.StatusLabel ?? "Flight open";

    public int SelectedCheckedInPassengers => SelectedFlight?.CheckedInPassengers ?? _operations.CheckedInPassengers;

    public int SelectedBookedPassengers => SelectedFlight?.BookedPassengers ?? _operations.TotalPassengers;

    public int SelectedLoadedBags => SelectedFlight?.LoadedBags ?? _operations.LoadedBags;

    public int NotBoardedPassengers => Math.Max(0, SelectedBookedPassengers - _operations.BoardedPassengers);

    public int StandbyPassengers => Math.Max(0, SelectedBookedPassengers - SelectedCheckedInPassengers);

    public int DryOperatingWeightKg
    {
        get => _dryOperatingWeightKg;
        set
        {
            if (SetProperty(ref _dryOperatingWeightKg, Math.Clamp(value, 0, 600_000)))
            {
                SaveCurrentFlightState();
                RefreshLoadCalculations();
            }
        }
    }

    public int TrafficLoadKg => (SelectedCheckedInPassengers * 84) + (SelectedLoadedBags * 18);

    public int ZeroFuelWeightKg => DryOperatingWeightKg + TrafficLoadKg;

    public int TakeoffFuelKg
    {
        get => _takeoffFuelKg;
        set
        {
            if (SetProperty(ref _takeoffFuelKg, Math.Clamp(value, 0, 200_000)))
            {
                SaveCurrentFlightState();
                RefreshLoadCalculations();
            }
        }
    }

    public int TakeoffWeightKg => ZeroFuelWeightKg + TakeoffFuelKg + AdditionalWeightKg;

    public int TaxiFuelKg
    {
        get => _taxiFuelKg;
        set
        {
            if (SetProperty(ref _taxiFuelKg, Math.Clamp(value, 0, 100_000)))
            {
                SaveCurrentFlightState();
                RefreshLoadCalculations();
            }
        }
    }

    public int TripFuelKg
    {
        get => _tripFuelKg;
        set
        {
            if (SetProperty(ref _tripFuelKg, Math.Clamp(value, 0, 200_000)))
            {
                SaveCurrentFlightState();
                RefreshLoadCalculations();
            }
        }
    }

    public int AdditionalWeightKg
    {
        get => _additionalWeightKg;
        set
        {
            if (SetProperty(ref _additionalWeightKg, Math.Clamp(value, 0, 100_000)))
            {
                SaveCurrentFlightState();
                RefreshLoadCalculations();
            }
        }
    }

    public int LandingWeightKg => Math.Max(0, TakeoffWeightKg - TripFuelKg);

    public int RampWeightKg => TakeoffWeightKg + TaxiFuelKg;

    public int MaxZeroFuelWeightKg => IsBoeing777300 ? 237_682 : 208_652;

    public int MaxTakeoffWeightKg => IsBoeing777300 ? 351_534 : 297_550;

    public int MaxLandingWeightKg => IsBoeing777300 ? 251_290 : 213_188;

    public int MaxRampWeightKg => MaxTakeoffWeightKg + 1_100;

    public int EstimatedZeroFuelWeightKg => ZeroFuelWeightKg;

    public int RegulatedRampWeightKg => Math.Min(RampWeightKg, MaxRampWeightKg);

    public int AllowedTrafficLoadKg => Math.Max(0, MaxZeroFuelWeightKg - DryOperatingWeightKg);

    public int UnderloadKg => Math.Max(0, AllowedTrafficLoadKg - TrafficLoadKg);

    public int CargoWeightKg => SelectedLoadedBags * 18;

    public double DryOperatingIndex
    {
        get => _dryOperatingIndex;
        set
        {
            if (SetProperty(ref _dryOperatingIndex, Math.Clamp(value, 0d, 120d)))
            {
                SaveCurrentFlightState();
                RefreshEnvelopePositions();
            }
        }
    }

    public double EnvelopeIndexX => 45d + ((Math.Clamp(DryOperatingIndex, 15d, 95d) - 15d) / 80d * 410d);

    public double EnvelopeMarkerLeft => EnvelopeIndexX - 8d;

    public double EnvelopeZeroFuelMarkerTop => WeightToEnvelopeY(ZeroFuelWeightKg) - 11d;

    public double EnvelopeTakeoffMarkerTop => WeightToEnvelopeY(TakeoffWeightKg) - 11d;

    public double EnvelopeLandingMarkerTop => WeightToEnvelopeY(LandingWeightKg) - 11d;

    public string FlightTimeLabel => "08:20";

    public string LoadPlanLabel => "Load plan No. 3";

    public string LoadSheetLabel => "Load sheet No. 2";

    public string LoadDistributionLabel => $"0A{_operations.FirstCount + _operations.ClubWorldCount}.0B{_operations.WorldTravellerPlusCount + _operations.WorldTravellerCount}";

    public string ActualCountsLabel => $"M {_operations.FirstCount + _operations.ClubWorldCount} / F {_operations.WorldTravellerPlusCount} / C 0 / O 0 | TTL: {SelectedCheckedInPassengers}+0";

    public string FlightVariationsLabel => "BRITISH AIRWAYS STANDARD (84/18/35/0)";

    public string PaxWeightsLabel => "84/18/35/0";

    public string LoadFactorLabel => SelectedBookedPassengers == 0
        ? "0%"
        : $"{Math.Round(SelectedCheckedInPassengers * 100d / SelectedBookedPassengers):0}%";

    public string BoardingCounterSummary => $"J {_operations.ClubWorldCount + _operations.FirstCount}   W {_operations.WorldTravellerPlusCount}   Y {_operations.WorldTravellerCount}";

    public string StatusLabel => SelectedFlight?.StatusLabel ?? (_operations.IsGateOpen ? "Open for Boarding" : "Open for Check-In");

    public string WbStatusLabel => ZeroFuelWeightKg > MaxZeroFuelWeightKg ||
        TakeoffWeightKg > MaxTakeoffWeightKg ||
        LandingWeightKg > MaxLandingWeightKg
        ? "CHECK LOAD LIMITS"
        : "W&B READY";

    public void Dispose()
    {
        _operations.PropertyChanged -= HandleOperationsPropertyChanged;
        _operations.PassengerRecords.CollectionChanged -= HandlePassengerRecordsChanged;
        _gateLogin.PropertyChanged -= HandleGateLoginPropertyChanged;
        GC.SuppressFinalize(this);
    }

    private void SelectModule(object? parameter)
    {
        if (parameter is string module && Modules.Contains(module, StringComparer.Ordinal))
        {
            ActiveModule = module;
        }
    }

    private void SelectService(object? parameter)
    {
        if (parameter is not IportServiceMenuEntry { IsHeader: false } entry)
        {
            return;
        }

        IsServiceMenuOpen = false;
        ActiveModule = entry.Module;
        CommandStatus = $"{entry.Label} selected from the Res2 services menu.";
    }

    private void SelectPassenger(object? parameter)
    {
        if (parameter is not GatePassengerViewModel passenger)
        {
            return;
        }

        SelectedPassenger = passenger;
        PassengerLookup = passenger.BookingReference;
        CommandStatus = $"{passenger.FullName}, seat {passenger.SeatNumber}, selected.";
    }

    private GatePassengerViewModel? FindPassenger(string key)
    {
        var normalized = key.Trim();
        if (normalized.Length == 0)
        {
            CommandStatus = "Enter a passenger name, booking reference, passenger number, or seat.";
            return null;
        }

        var passenger = _operations.PassengerRecords.FirstOrDefault(candidate =>
            candidate.FullName.Contains(normalized, StringComparison.OrdinalIgnoreCase) ||
            candidate.BookingReference.Equals(normalized, StringComparison.OrdinalIgnoreCase) ||
            candidate.PassengerNumber.Equals(normalized, StringComparison.OrdinalIgnoreCase) ||
            candidate.SeatNumber.Equals(normalized, StringComparison.OrdinalIgnoreCase));
        if (passenger is null)
        {
            CommandStatus = $"No passenger matched '{normalized}'.";
            return null;
        }

        SelectPassenger(passenger);
        return passenger;
    }

    private void ManualBoard()
    {
        var passenger = FindPassenger(ManualBoardingInput);
        if (passenger is null)
        {
            return;
        }

        if (!passenger.IsCheckedIn)
        {
            _operations.CheckInPassengerCommand.Execute(passenger);
        }

        _operations.BoardPassengerCommand.Execute(passenger);
        CommandStatus = _operations.OperationMessage;
        RefreshDerivedProperties();
    }

    private void RunForSelected(ICommand command, string successMessage)
    {
        if (SelectedPassenger is not { } passenger)
        {
            CommandStatus = "Select a passenger first.";
            return;
        }

        command.Execute(passenger);
        CommandStatus = string.IsNullOrWhiteSpace(_operations.OperationMessage)
            ? successMessage
            : _operations.OperationMessage;
        RefreshDerivedProperties();
    }

    private void RefreshAll(string message)
    {
        _operations.ApplySettings();
        RefreshFlightList();
        RefreshMonitorEvents();
        RefreshDerivedProperties();
        CommandStatus = message;
    }

    private void InitializeFlightStates()
    {
        _flightStates[_operations.FlightNumber] = new IportLoadFlightState(
            _operations.FlightNumber,
            _operations.FlightDateShort,
            _operations.ScheduledDeparture,
            _boardingPoint,
            _destination,
            _operations.GateNumber,
            _operations.AircraftName,
            _operations.DetectedAircraftIcao,
            _operations.BoardingBeginsAt,
            "Flight open",
            _operations.CheckedInPassengers,
            _operations.TotalPassengers,
            _operations.LoadedBags,
            _dryOperatingWeightKg,
            _dryOperatingIndex,
            _takeoffFuelKg,
            _tripFuelKg,
            _taxiFuelKg,
            _additionalWeightKg,
            true);
        _flightStates["BA281"] = new IportLoadFlightState(
            "BA281", _operations.FlightDateShort, AddMinutes(_operations.ScheduledDeparture, 40),
            "LHR", "LAX", "C55", "Boeing 777-200ER", "B772", AddMinutes(_operations.ScheduledDeparture, 15),
            "Dispatcher assigned", 198, 244, 174, 165_400, 44.10, 96_000, 84_600, 1_100, 0, false);
        _flightStates["BA274"] = new IportLoadFlightState(
            "BA274", _operations.FlightDateShort, AddMinutes(_operations.ScheduledDeparture, 75),
            "LHR", "LAS", "B36", "Boeing 777-200ER", "B772", AddMinutes(_operations.ScheduledDeparture, 50),
            "Dispatcher assigned", 162, 231, 139, 166_250, 46.35, 88_500, 76_900, 1_050, 0, false);
    }

    private void SaveCurrentFlightState()
    {
        if (_isLoadingFlightState || SelectedFlight is null || !_flightStates.TryGetValue(SelectedFlight.FlightNumber, out var state))
        {
            return;
        }

        state.Origin = BoardingPoint;
        state.Destination = Destination;
        state.DryOperatingWeightKg = DryOperatingWeightKg;
        state.DryOperatingIndex = DryOperatingIndex;
        state.TakeoffFuelKg = TakeoffFuelKg;
        state.TripFuelKg = TripFuelKg;
        state.TaxiFuelKg = TaxiFuelKg;
        state.AdditionalWeightKg = AdditionalWeightKg;
    }

    private void LoadFlightState(string flightNumber)
    {
        if (!_flightStates.TryGetValue(flightNumber, out var state))
        {
            return;
        }

        _isLoadingFlightState = true;
        _boardingPoint = state.Origin;
        _destination = state.Destination;
        _dryOperatingWeightKg = state.DryOperatingWeightKg;
        _dryOperatingIndex = state.DryOperatingIndex;
        _takeoffFuelKg = state.TakeoffFuelKg;
        _tripFuelKg = state.TripFuelKg;
        _taxiFuelKg = state.TaxiFuelKg;
        _additionalWeightKg = state.AdditionalWeightKg;
        _isLoadingFlightState = false;

        OnPropertyChanged(nameof(BoardingPoint));
        OnPropertyChanged(nameof(Destination));
        OnPropertyChanged(nameof(BoardingPointLabel));
        OnPropertyChanged(nameof(DryOperatingWeightKg));
        OnPropertyChanged(nameof(DryOperatingIndex));
        OnPropertyChanged(nameof(TakeoffFuelKg));
        OnPropertyChanged(nameof(TripFuelKg));
        OnPropertyChanged(nameof(TaxiFuelKg));
        OnPropertyChanged(nameof(AdditionalWeightKg));
        OnPropertyChanged(nameof(MaxZeroFuelWeightKg));
        OnPropertyChanged(nameof(MaxTakeoffWeightKg));
        OnPropertyChanged(nameof(MaxLandingWeightKg));
        OnPropertyChanged(nameof(MaxRampWeightKg));
    }

    private void RefreshFlightList()
    {
        var selectedFlightNumber = SelectedFlight?.FlightNumber ?? _operations.FlightNumber;
        SaveCurrentFlightState();
        if (_flightStates.TryGetValue(_operations.FlightNumber, out var liveState))
        {
            liveState.CheckedInPassengers = _operations.CheckedInPassengers;
            liveState.BookedPassengers = _operations.TotalPassengers;
            liveState.LoadedBags = _operations.LoadedBags;
            liveState.AircraftName = _operations.AircraftName;
            liveState.AircraftIcao = _operations.DetectedAircraftIcao;
            liveState.StatusLabel = _operations.IsGateOpen ? "Open for Boarding" : "Flight open";
        }

        Flights.Clear();
        foreach (var flightNumber in new[] { _operations.FlightNumber, "BA281", "BA274" }.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (_flightStates.TryGetValue(flightNumber, out var state))
            {
                Flights.Add(state.ToSummary());
            }
        }

        SelectedFlight = Flights.FirstOrDefault(flight => string.Equals(flight.FlightNumber, selectedFlightNumber, StringComparison.OrdinalIgnoreCase))
            ?? Flights.FirstOrDefault();
    }

    private void RefreshMonitorEvents()
    {
        var date = _operations.CurrentClockDate;
        var time = _operations.CurrentClockTime;
        MonitorEvents.Clear();
        MonitorEvents.Add(new IportMonitorEventViewModel(date, time, _operations.GateStatusLabel, "System", "OK"));
        MonitorEvents.Add(new IportMonitorEventViewModel(date, _operations.BoardingBeginsAt, "Boarding window scheduled", SignedInUser, "OK"));
        MonitorEvents.Add(new IportMonitorEventViewModel(date, _operations.GateOpensAt, "Flight open for check-in", SignedInUser, "OK"));
        MonitorEvents.Add(new IportMonitorEventViewModel(date, _operations.TurnaroundStartsAt, "Flight preparation", "System", "OK"));
        MonitorEvents.Add(new IportMonitorEventViewModel(date, _operations.SimBriefImportLabel, "Passenger manifest synchronized", "SimBrief", "OK"));
    }

    private void RefreshDerivedProperties()
    {
        OnPropertyChanged(nameof(SelectedPassenger));
        OnPropertyChanged(nameof(CurrentClock));
        OnPropertyChanged(nameof(NotBoardedPassengers));
        OnPropertyChanged(nameof(StandbyPassengers));
        OnPropertyChanged(nameof(TrafficLoadKg));
        OnPropertyChanged(nameof(ZeroFuelWeightKg));
        OnPropertyChanged(nameof(TakeoffWeightKg));
        OnPropertyChanged(nameof(LandingWeightKg));
        OnPropertyChanged(nameof(RampWeightKg));
        OnPropertyChanged(nameof(EstimatedZeroFuelWeightKg));
        OnPropertyChanged(nameof(RegulatedRampWeightKg));
        OnPropertyChanged(nameof(AllowedTrafficLoadKg));
        OnPropertyChanged(nameof(UnderloadKg));
        OnPropertyChanged(nameof(CargoWeightKg));
        OnPropertyChanged(nameof(LoadDistributionLabel));
        OnPropertyChanged(nameof(ActualCountsLabel));
        OnPropertyChanged(nameof(LoadFactorLabel));
        OnPropertyChanged(nameof(BoardingCounterSummary));
        OnPropertyChanged(nameof(StatusLabel));
        OnPropertyChanged(nameof(WbStatusLabel));
        OnPropertyChanged(nameof(BoardingPointLabel));
        RefreshEnvelopePositions();
    }

    private void RefreshLoadCalculations()
    {
        OnPropertyChanged(nameof(ZeroFuelWeightKg));
        OnPropertyChanged(nameof(TakeoffWeightKg));
        OnPropertyChanged(nameof(LandingWeightKg));
        OnPropertyChanged(nameof(RampWeightKg));
        OnPropertyChanged(nameof(EstimatedZeroFuelWeightKg));
        OnPropertyChanged(nameof(RegulatedRampWeightKg));
        OnPropertyChanged(nameof(AllowedTrafficLoadKg));
        OnPropertyChanged(nameof(UnderloadKg));
        OnPropertyChanged(nameof(WbStatusLabel));
        RefreshEnvelopePositions();
    }

    private void RefreshEnvelopePositions()
    {
        OnPropertyChanged(nameof(EnvelopeIndexX));
        OnPropertyChanged(nameof(EnvelopeMarkerLeft));
        OnPropertyChanged(nameof(EnvelopeZeroFuelMarkerTop));
        OnPropertyChanged(nameof(EnvelopeTakeoffMarkerTop));
        OnPropertyChanged(nameof(EnvelopeLandingMarkerTop));
    }

    private void HandleOperationsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GateOperationsViewModel.SelectedPassenger))
        {
            OnPropertyChanged(nameof(SelectedPassenger));
        }

        RefreshDerivedProperties();
    }

    private void HandlePassengerRecordsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshFlightList();
        RefreshDerivedProperties();
    }

    private void HandleGateLoginPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(IsAvailable));
        OnPropertyChanged(nameof(SignedInUser));
        OnPropertyChanged(nameof(BoardingPoint));
        OnPropertyChanged(nameof(BoardingPointLabel));
    }

    private static double WeightToEnvelopeY(int weightKg)
    {
        var clampedWeight = Math.Clamp(weightKg, 160_000, 360_000);
        return 225d - ((clampedWeight - 160_000d) / 200_000d * 205d);
    }

    private static string NormalizeAirportEntry(string? value)
    {
        var normalized = new string((value ?? string.Empty)
            .Where(char.IsLetterOrDigit)
            .Take(4)
            .ToArray());
        return normalized.ToUpperInvariant();
    }

    private static string AddMinutes(string time, int minutes)
    {
        return TimeOnly.TryParse(time, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed.AddMinutes(minutes).ToString("HH:mm", CultureInfo.InvariantCulture)
            : time;
    }

    private bool IsBoeing777300 => (SelectedFlight?.AircraftName ?? _operations.AircraftName).Contains("300", StringComparison.OrdinalIgnoreCase) ||
        (SelectedFlight?.AircraftIcao ?? _operations.DetectedAircraftIcao).Contains("77W", StringComparison.OrdinalIgnoreCase);

    private static bool IsLoadControlModule(string module) => module is LoadControlModule or LoadPassengerModule or
        LoadDeadloadModule or LoadDistributionModule or LoadDocumentsModule or LoadPwSummaryModule;

    private static string ResolveServiceLabel(string module) => module switch
    {
        LoadControlModule or LoadPassengerModule or LoadDeadloadModule or LoadDistributionModule or
            LoadDocumentsModule or LoadPwSummaryModule => "Load Control",
        FlightMonitorModule => "Flight Control",
        GateControlModule => "Gate Control",
        BoardingModule => "Boarding",
        LostAndFoundModule => "Lost & Found",
        DatabaseManagementModule => "Database mgt",
        AutomationModule => "Autoinformation",
        MyAccountModule => "My Account",
        SupportModule => "Support",
        Res2SupportModule => "Res2 Support",
        MessagesModule => "Messages",
        _ => "Customer Services"
    };
}

public sealed record IportServiceMenuEntry(
    string Label,
    string Shortcut,
    string Module,
    bool IsHeader)
{
    public bool IsService => !IsHeader;

    public static IportServiceMenuEntry Header(string label) => new(label, string.Empty, string.Empty, true);

    public static IportServiceMenuEntry Service(string label, string shortcut, string module) => new(label, shortcut, module, false);
}

public sealed class IportFlightSummary : ObservableObject
{
    private string _origin;
    private string _destination;
    private bool _isSelected;

    public IportFlightSummary(
        string flightNumber,
        string date,
        string departureTime,
        string origin,
        string destination,
        string gate,
        string aircraftName,
        string aircraftIcao,
        string boardingTime,
        string statusLabel,
        int checkedInPassengers,
        int bookedPassengers,
        int loadedBags,
        bool isLive)
    {
        FlightNumber = flightNumber;
        Date = date;
        DepartureTime = departureTime;
        _origin = origin;
        _destination = destination;
        Gate = gate;
        AircraftName = aircraftName;
        AircraftIcao = aircraftIcao;
        BoardingTime = boardingTime;
        StatusLabel = statusLabel;
        CheckedInPassengers = checkedInPassengers;
        BookedPassengers = bookedPassengers;
        LoadedBags = loadedBags;
        IsLive = isLive;
    }

    public string FlightNumber { get; }

    public string Date { get; }

    public string DepartureTime { get; }

    public string Origin
    {
        get => _origin;
        set
        {
            if (SetProperty(ref _origin, value))
            {
                OnPropertyChanged(nameof(RouteLabel));
                OnPropertyChanged(nameof(DisplayLine));
            }
        }
    }

    public string Destination
    {
        get => _destination;
        set
        {
            if (SetProperty(ref _destination, value))
            {
                OnPropertyChanged(nameof(RouteLabel));
                OnPropertyChanged(nameof(DisplayLine));
            }
        }
    }

    public string Gate { get; }

    public string AircraftName { get; }

    public string AircraftIcao { get; }

    public string BoardingTime { get; }

    public string StatusLabel { get; }

    public int CheckedInPassengers { get; }

    public int BookedPassengers { get; }

    public int LoadedBags { get; }

    public bool IsLive { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetProperty(ref _isSelected, value))
            {
                OnPropertyChanged(nameof(RowBackground));
                OnPropertyChanged(nameof(HeaderRowBackground));
            }
        }
    }

    public string StatusGlyph => IsLive ? "●" : "■";

    public string StatusColor => IsLive ? "#12B8CF" : "#E7B225";

    public string RowBackground => IsSelected ? "#20B7D0" : "#FFFFFF";

    public string HeaderRowBackground => IsSelected ? "#E7E7E7" : "#FFFFFF";

    public string RouteLabel => $"{Origin}-{Destination}";

    public string DisplayLine => $"{FlightNumber}   {Date}-{DepartureTime}   {Destination}   {Gate}";
}

internal sealed class IportLoadFlightState(
    string flightNumber,
    string date,
    string departureTime,
    string origin,
    string destination,
    string gate,
    string aircraftName,
    string aircraftIcao,
    string boardingTime,
    string statusLabel,
    int checkedInPassengers,
    int bookedPassengers,
    int loadedBags,
    int dryOperatingWeightKg,
    double dryOperatingIndex,
    int takeoffFuelKg,
    int tripFuelKg,
    int taxiFuelKg,
    int additionalWeightKg,
    bool isLive)
{
    public string FlightNumber { get; } = flightNumber;
    public string Date { get; } = date;
    public string DepartureTime { get; } = departureTime;
    public string Origin { get; set; } = origin;
    public string Destination { get; set; } = destination;
    public string Gate { get; } = gate;
    public string AircraftName { get; set; } = aircraftName;
    public string AircraftIcao { get; set; } = aircraftIcao;
    public string BoardingTime { get; } = boardingTime;
    public string StatusLabel { get; set; } = statusLabel;
    public int CheckedInPassengers { get; set; } = checkedInPassengers;
    public int BookedPassengers { get; set; } = bookedPassengers;
    public int LoadedBags { get; set; } = loadedBags;
    public int DryOperatingWeightKg { get; set; } = dryOperatingWeightKg;
    public double DryOperatingIndex { get; set; } = dryOperatingIndex;
    public int TakeoffFuelKg { get; set; } = takeoffFuelKg;
    public int TripFuelKg { get; set; } = tripFuelKg;
    public int TaxiFuelKg { get; set; } = taxiFuelKg;
    public int AdditionalWeightKg { get; set; } = additionalWeightKg;
    public bool IsLive { get; } = isLive;

    public IportFlightSummary ToSummary() => new(
        FlightNumber,
        Date,
        DepartureTime,
        Origin,
        Destination,
        Gate,
        AircraftName,
        AircraftIcao,
        BoardingTime,
        StatusLabel,
        CheckedInPassengers,
        BookedPassengers,
        LoadedBags,
        IsLive);
}

public sealed record IportMonitorEventViewModel(
    string Date,
    string Time,
    string Event,
    string Agent,
    string Status);
