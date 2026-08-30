using System.Collections.ObjectModel;
using System.Globalization;
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

public sealed class PassengerFlowViewModel : PageViewModel, IDisposable
{
    private readonly AppSettings _settings;
    private readonly ISettingsStore? _settingsStore;
    private readonly ISimBriefClient _simBriefClient;
    private readonly IOperationsClock _operationsClock;
    private PassengerBoardingEngine _engine;
    private readonly DispatcherTimer _animationTimer;
    private readonly DispatcherTimer _cabinActivityTimer;
    private readonly Dictionary<int, PassengerMarkerViewModel> _markersByPassengerId = [];
    private readonly Dictionary<int, PassengerManifestEntryViewModel> _manifestByPassengerId = [];
    private readonly HashSet<int> _loggedPassengerIds = [];
    private int _bookedPassengerCount;
    private BoardingSpeedOption _selectedSpeedOption;
    private DateTime _lastAnimationTick;
    private bool _isManifestOpen;
    private bool _isPassengerDetailsOpen;
    private PassengerManifestEntryViewModel? _selectedPassenger;
    private string _simBriefPilotId;
    private bool _simBriefAutoSync;
    private string _simBriefStatus = "Enter your numeric SimBrief Pilot ID to import the latest OFP.";
    private string _simBriefFlightSummary = "No SimBrief flight imported";
    private bool _isSimBriefSyncing;
    private bool _hasSimBriefFlight;
    private string _importedFlightNumber = string.Empty;
    private string _importedOrigin = string.Empty;
    private string _importedDestination = string.Empty;
    private string _importedAircraftIcao = string.Empty;
    private DateTimeOffset? _importedScheduledDepartureLocal;
    private DateTimeOffset? _importedScheduledArrivalLocal;
    private DateTimeOffset? _lastSimBriefSyncTime;
    private CabinLayoutProfileOption _selectedCabinLayoutProfile;
    private bool _seatbeltSignOn = true;
    private string _liveFlightPhase = "PREFLIGHT";
    private int _activityPulseTicks;
    private bool _isAircraftMoving;
    private bool _isPushbackActive;
    private DateTimeOffset? _crewRestCycleStartedAt;
    private CabinCrewRestAssignment _crewRestAssignment;
    private int _lastAnnouncedCrewRestGroup;
    private bool _seatbeltSignalAvailable;
    private bool _preDepartureDrinksStarted;
    private bool _preDepartureDrinksActive;
    private bool _isArrivalPreparation;
    private string _crewRestStatusOverride = "CREW REST · staged long-haul rotation";

    public PassengerFlowViewModel(
        AppSettings settings,
        SharedStatusViewModel status,
        ISettingsStore? settingsStore = null,
        ISimBriefClient? simBriefClient = null,
        IOperationsClock? operationsClock = null)
        : base("Passenger Flow", "Simulator-free 777 boarding, deboarding and passenger manifest")
    {
        _settings = settings;
        _settingsStore = settingsStore;
        _simBriefClient = simBriefClient ?? new SimBriefClient();
        _operationsClock = operationsClock ?? new LocalOperationsClock();
        _selectedCabinLayoutProfile = CabinLayoutProfileCatalog.Resolve(settings.PassengerCabinLayoutId);
        _settings.PassengerCabinLayoutId = _selectedCabinLayoutProfile.Id;
        _simBriefPilotId = settings.SimBriefPilotId;
        _simBriefAutoSync = settings.SimBriefAutoSync;
        Status = status;
        _engine = new PassengerBoardingEngine(0, _selectedCabinLayoutProfile.Layout);
        _bookedPassengerCount = 0;
        SpeedOptions =
        [
            new BoardingSpeedOption("Real Ops · 30–45 min", 0.06d),
            new BoardingSpeedOption("1× Preview", 1d),
            new BoardingSpeedOption("2× Preview", 2d),
            new BoardingSpeedOption("4× Fast Preview", 4d)
        ];
        _selectedSpeedOption = SpeedOptions.MinBy(option =>
            Math.Abs(option.Multiplier - settings.PassengerPreviewSpeed)) ?? SpeedOptions[1];

        ActivityLog.Add("No passenger list loaded — import SimBrief or enter a manual passenger count");
        StartPauseCommand = new RelayCommand(_ => StartPauseOperation());
        ResetCommand = new RelayCommand(_ => ResetPreview());
        SetLoadPresetCommand = new RelayCommand(SetLoadPreset);
        OpenManifestCommand = new RelayCommand(_ => IsManifestOpen = true);
        CloseManifestCommand = new RelayCommand(_ => IsManifestOpen = false);
        SelectPassengerCommand = new RelayCommand(SelectPassenger);
        ClosePassengerDetailsCommand = new RelayCommand(_ => ClearPassengerSelection());
        SyncSimBriefCommand = new AsyncRelayCommand(SyncSimBriefAsync, ShowSimBriefError);
        ToggleSeatbeltSignCommand = new RelayCommand(_ => ToggleSeatbeltFailSafe());

        _animationTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(50)
        };
        _animationTimer.Tick += HandleAnimationTick;
        _cabinActivityTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _cabinActivityTimer.Tick += HandleCabinActivityTick;
        _cabinActivityTimer.Start();
        RebuildManifest();
        RefreshFromEngine();

        if (_simBriefAutoSync && !string.IsNullOrWhiteSpace(_simBriefPilotId))
        {
            _ = AutoSyncSimBriefAsync();
        }
    }

    public SharedStatusViewModel Status { get; }
    public ICommand StartPauseCommand { get; }
    public ICommand ResetCommand { get; }
    public ICommand SetLoadPresetCommand { get; }
    public ICommand OpenManifestCommand { get; }
    public ICommand CloseManifestCommand { get; }
    public ICommand SelectPassengerCommand { get; }
    public ICommand ClosePassengerDetailsCommand { get; }
    public ICommand SyncSimBriefCommand { get; }
    public ICommand ToggleSeatbeltSignCommand { get; }
    public ObservableCollection<PassengerMarkerViewModel> PassengerMarkers { get; } = [];
    public ObservableCollection<CabinCrewMarkerViewModel> CabinCrewMarkers { get; } = [];
    public BulkObservableCollection<PassengerManifestEntryViewModel> PassengerManifest { get; } = [];
    public ObservableCollection<string> ActivityLog { get; } = [];
    public IReadOnlyList<BoardingSpeedOption> SpeedOptions { get; }
    public IReadOnlyList<CabinLayoutProfileOption> CabinLayoutProfiles => CabinLayoutProfileCatalog.All;
    public bool IsOperationalCabinLayout => SelectedCabinLayoutProfile.IsOperational;
    public bool IsReferenceCabinLayout => !IsOperationalCabinLayout;
    public bool IsFlightFactorCabinLayout => SelectedCabinLayoutProfile.Layout == PassengerCabinLayout.FlightFactor777V2;
    public bool IsAirlineCabinLayout => !IsFlightFactorCabinLayout;
    public bool IsBritishAirways777200Er => SelectedCabinLayoutProfile.Layout == PassengerCabinLayout.BritishAirways777200Er;
    public bool IsBritishAirways777300 => SelectedCabinLayoutProfile.Layout == PassengerCabinLayout.BritishAirways777300;
    public bool SeatbeltSignOn => _seatbeltSignOn;
    public string SeatbeltSignLabel => SeatbeltSignOn ? "SEAT BELTS ON" : "SEAT BELTS OFF";
    public bool SeatbeltSignalAvailable => _seatbeltSignalAvailable;
    public bool CanManuallyToggleSeatbeltSign => !_settings.SyncSimulatorSeatbeltSign || !SeatbeltSignalAvailable;
    public string SeatbeltControlStatus => CanManuallyToggleSeatbeltSign
        ? "Manual fail-safe · click to toggle"
        : "Live simulator annunciator";
    public string LiveFlightPhase => _liveFlightPhase;
    public string AircraftMovementLabel => _isPushbackActive ? "PUSHBACK ACTIVE" : _isAircraftMoving ? "AIRCRAFT MOVING" : "STATIONARY";
    public string LiveCabinStatus => $"{LiveFlightPhase.ToUpperInvariant()} · {AircraftMovementLabel} · {SeatbeltSignLabel}";
    private int ExpectedCabinCrewCount => SelectedCabinLayoutProfile.Layout == PassengerCabinLayout.BritishAirways777300 ? 12 : 10;
    public int RestingCrewCount => _crewRestAssignment.IsActive ? _crewRestAssignment.RestingCrewCount : 0;
    public string CrewRestStatus
    {
        get
        {
            if (!_crewRestAssignment.IsActive)
            {
                return _crewRestStatusOverride;
            }

            var hours = (int)_crewRestAssignment.Remaining.TotalHours;
            return $"CREW REST {_crewRestAssignment.RestGroup} · {RestingCrewCount} resting · {hours}:{_crewRestAssignment.Remaining.Minutes:00} remaining";
        }
    }
    public string CabinActivitySummary
    {
        get
        {
            var passengers = _engine.Passengers.Where(passenger => passenger.MovementState == PassengerMovementState.Seated).ToArray();
            var entertainment = passengers.Count(passenger => passenger.CabinActivity is PassengerCabinActivity.WatchingMovie or PassengerCabinActivity.Gaming or PassengerCabinActivity.UsingPhone);
            var resting = passengers.Count(passenger => passenger.CabinActivity == PassengerCabinActivity.Sleeping);
            var moving = passengers.Count(passenger => passenger.CabinActivity is PassengerCabinActivity.WalkingToLavatory or PassengerCabinActivity.UsingLavatory or PassengerCabinActivity.ReturningToSeat);
            var activeCrew = CabinCrewMarkers.Count(marker => !marker.IsSecured && !marker.IsResting);
            return $"{entertainment} entertainment · {resting} resting · {moving} moving · {activeCrew} crew active · {RestingCrewCount} crew resting";
        }
    }
    public double L1DoorCanvasLeft => SelectedCabinLayoutProfile.Layout switch
    {
        PassengerCabinLayout.BritishAirways777200Er => 17d,
        PassengerCabinLayout.BritishAirways777300 => 15d,
        _ => 148d
    };
    public double L2DoorCanvasLeft => SelectedCabinLayoutProfile.Layout switch
    {
        PassengerCabinLayout.BritishAirways777200Er => 260d,
        PassengerCabinLayout.BritishAirways777300 => 193d,
        _ => 391d
    };
    public CabinLayoutProfileOption SelectedCabinLayoutProfile
    {
        get => _selectedCabinLayoutProfile;
        set => SetCabinLayoutProfile(value, persist: true);
    }

    public int CabinCapacity => _engine.Capacity;
    public int MappedPassengerCount => _engine.TargetPassengerCount;
    public int UnmappedPassengerCount => Math.Max(0, BookedPassengerCount - MappedPassengerCount);
    public bool HasCapacityOverflow => UnmappedPassengerCount > 0;
    public bool HasPassengerManifest => PassengerManifest.Count > 0;
    public int PassengerInputMaximum => Math.Max(CabinCapacity, BookedPassengerCount);
    public string CapacitySummary => HasCapacityOverflow
        ? $"{MappedPassengerCount} mapped · {UnmappedPassengerCount} unmapped"
        : $"of {CabinCapacity} seats";
    public string ManifestSummary => !HasPassengerManifest
        ? "No passenger list loaded — import SimBrief or enter a manual passenger count"
        : HasCapacityOverflow
        ? $"{MappedPassengerCount} mapped passengers · {BookedPassengerCount} booked by SimBrief · {UnmappedPassengerCount} awaiting a compatible cabin layout"
        : $"{PassengerManifest.Count} passengers · ordered by boarding group";

    public int BookedPassengerCount
    {
        get => _bookedPassengerCount;
        set
        {
            if (!CanAdjustPassengerLoad)
            {
                return;
            }

            ApplyBookedPassengerCount(value, simBriefPriority: false);
        }
    }

    public void ApplyCabinLayoutSelection(string? profileId) =>
        SetCabinLayoutProfile(CabinLayoutProfileCatalog.Resolve(profileId), persist: false);

    public void ApplyCabinTelemetry(CabinTelemetrySnapshot snapshot)
    {
        var moving = snapshot.Signals.GetValueOrDefault("groundspeed_mps") >= 0.35d;
        var pushback = snapshot.Signals.GetValueOrDefault("pushback_active") >= 0.5d;
        if (_isAircraftMoving != moving || _isPushbackActive != pushback)
        {
            _isAircraftMoving = moving;
            _isPushbackActive = pushback;
            OnPropertyChanged(nameof(AircraftMovementLabel));
            OnPropertyChanged(nameof(LiveCabinStatus));
        }

        var seatbeltSignalAvailable = snapshot.Signals.GetValueOrDefault("seatbelt_signal_available") >= 0.5d;
        if (_seatbeltSignalAvailable != seatbeltSignalAvailable)
        {
            _seatbeltSignalAvailable = seatbeltSignalAvailable;
            OnPropertyChanged(nameof(SeatbeltSignalAvailable));
            OnPropertyChanged(nameof(CanManuallyToggleSeatbeltSign));
            OnPropertyChanged(nameof(SeatbeltControlStatus));
        }

        if (_settings.SyncSimulatorSeatbeltSign && seatbeltSignalAvailable &&
            _seatbeltSignOn != snapshot.SeatbeltSignOn)
        {
            _seatbeltSignOn = snapshot.SeatbeltSignOn;
            OnPropertyChanged(nameof(SeatbeltSignOn));
            OnPropertyChanged(nameof(SeatbeltSignLabel));
            OnPropertyChanged(nameof(LiveCabinStatus));
        }

        if (!string.Equals(_liveFlightPhase, snapshot.FlightPhase, StringComparison.Ordinal))
        {
            _liveFlightPhase = snapshot.FlightPhase;
            OnPropertyChanged(nameof(LiveFlightPhase));
            OnPropertyChanged(nameof(LiveCabinStatus));
        }

        UpdatePreDepartureWelcomeService();
        UpdateCabinCrewRest();
        RefreshCrewMarkers();
    }

    public void ApplyPerformanceMode(string mode)
    {
        _animationTimer.Interval = mode switch
        {
            "Quality" => TimeSpan.FromMilliseconds(33),
            "Low Impact" => TimeSpan.FromMilliseconds(100),
            _ => TimeSpan.FromMilliseconds(50)
        };
        _cabinActivityTimer.Interval = mode == "Low Impact"
            ? TimeSpan.FromSeconds(2)
            : TimeSpan.FromSeconds(1);
    }

    public BoardingSpeedOption SelectedSpeedOption
    {
        get => _selectedSpeedOption;
        set
        {
            if (value is null || !SetProperty(ref _selectedSpeedOption, value))
            {
                return;
            }

            _settings.PassengerPreviewSpeed = value.Multiplier;
            OnPropertyChanged(nameof(OperationEta));
        }
    }

    public bool L1DoorOpen
    {
        get => _engine.IsDoorOpen(BoardingDoor.L1);
        set
        {
            if (value == _engine.IsDoorOpen(BoardingDoor.L1))
            {
                return;
            }

            _engine.SetDoorOpen(BoardingDoor.L1, value);
            AddActivity($"L1 passenger door {(value ? "opened" : "closed")}");
            OnPropertyChanged();
            ResumeTimerIfNeeded();
            RefreshFromEngine();
        }
    }

    public bool L2DoorOpen
    {
        get => _engine.IsDoorOpen(BoardingDoor.L2);
        set
        {
            if (value == _engine.IsDoorOpen(BoardingDoor.L2))
            {
                return;
            }

            _engine.SetDoorOpen(BoardingDoor.L2, value);
            AddActivity($"L2 passenger door {(value ? "opened" : "closed")}");
            OnPropertyChanged();
            ResumeTimerIfNeeded();
            RefreshFromEngine();
        }
    }

    public string SimBriefPilotId
    {
        get => _simBriefPilotId;
        set
        {
            if (SetProperty(ref _simBriefPilotId, value))
            {
                _settings.SimBriefPilotId = value.Trim();
            }
        }
    }

    public bool SimBriefAutoSync
    {
        get => _simBriefAutoSync;
        set
        {
            if (SetProperty(ref _simBriefAutoSync, value))
            {
                _settings.SimBriefAutoSync = value;
                _ = SaveSettingsQuietlyAsync();
            }
        }
    }

    public string SimBriefStatus
    {
        get => _simBriefStatus;
        private set => SetProperty(ref _simBriefStatus, value);
    }

    public string SimBriefFlightSummary
    {
        get => _simBriefFlightSummary;
        private set => SetProperty(ref _simBriefFlightSummary, value);
    }

    public bool IsSimBriefSyncing
    {
        get => _isSimBriefSyncing;
        private set => SetProperty(ref _isSimBriefSyncing, value);
    }

    public bool IsManifestOpen
    {
        get => _isManifestOpen;
        set => SetProperty(ref _isManifestOpen, value);
    }

    public bool IsPassengerDetailsOpen
    {
        get => _isPassengerDetailsOpen;
        set => SetProperty(ref _isPassengerDetailsOpen, value);
    }

    public PassengerManifestEntryViewModel? SelectedPassenger
    {
        get => _selectedPassenger;
        private set
        {
            if (!SetProperty(ref _selectedPassenger, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsSeatHighlightVisible));
            OnPropertyChanged(nameof(SelectedSeatCanvasLeft));
            OnPropertyChanged(nameof(SelectedSeatCanvasTop));
            OnPropertyChanged(nameof(SelectedSeatLabel));
        }
    }

    public bool IsSeatHighlightVisible => SelectedPassenger is not null;
    public double SelectedSeatCanvasLeft => (SelectedPassenger?.SeatX ?? 0d) - 12d;
    public double SelectedSeatCanvasTop => (SelectedPassenger?.SeatY ?? 0d) - 12d;
    public string SelectedSeatLabel => SelectedPassenger is null ? string.Empty : $"SEAT {SelectedPassenger.SeatNumber}";

    public int BoardedPassengerCount => _engine.BoardedCount;
    public int NoShowPassengerCount => _engine.NoShowCount;
    public int DeboardedPassengerCount => _engine.DeboardedCount;
    public int WalkingPassengerCount => _engine.WalkingCount;
    public int OccupyingPassengerCount => _engine.OccupyingCount;
    public int InCabinPassengerCount => _engine.Operation == PassengerOperation.Deboarding
        ? _engine.OnBoardCount
        : _engine.InCabinCount;
    public string InCabinSummary => _engine.Operation == PassengerOperation.Deboarding
        ? "Still on aircraft"
        : "Walking / settling";
    public int RemainingPassengerCount => _engine.Operation == PassengerOperation.Deboarding
        ? _engine.OnBoardCount
        : _engine.RemainingCount + UnmappedPassengerCount;
    public int WaitingPassengerCount => _engine.WaitingCount;
    public string WaitingPassengerSummary => _engine.Operation == PassengerOperation.Deboarding
        ? $"{_engine.DeboardedCount} off aircraft"
        : $"{_engine.WaitingCount + UnmappedPassengerCount} outside";
    public double OperationProgress => (_engine.Operation == PassengerOperation.Deboarding
        ? _engine.DeboardingProgress
        : _engine.Progress) * 100d;
    public double BoardingProgress => OperationProgress;
    public string OperationProgressLabel => _engine.Operation == PassengerOperation.Deboarding
        ? "DEBOARDING PROGRESS"
        : HasCapacityOverflow ? "MAPPED BOARDING PROGRESS" : "BOARDING PROGRESS";
    public BoardingRunState BoardingState => _engine.State;
    public int CurrentBoardingGroup => _engine.CurrentBoardingGroup;

    public bool HasSimBriefFlight
    {
        get => _hasSimBriefFlight;
        private set
        {
            if (SetProperty(ref _hasSimBriefFlight, value))
            {
                OnPropertyChanged(nameof(CanAdjustPassengerLoad));
                OnPropertyChanged(nameof(PassengerLoadSourceLabel));
            }
        }
    }

    public string ImportedFlightNumber
    {
        get => _importedFlightNumber;
        private set => SetProperty(ref _importedFlightNumber, value);
    }

    public string ImportedOrigin
    {
        get => _importedOrigin;
        private set => SetProperty(ref _importedOrigin, value);
    }

    public string ImportedDestination
    {
        get => _importedDestination;
        private set => SetProperty(ref _importedDestination, value);
    }

    public string ImportedAircraftIcao
    {
        get => _importedAircraftIcao;
        private set => SetProperty(ref _importedAircraftIcao, value);
    }

    public DateTimeOffset? ImportedScheduledDepartureLocal
    {
        get => _importedScheduledDepartureLocal;
        private set => SetProperty(ref _importedScheduledDepartureLocal, value);
    }

    public DateTimeOffset? LastSimBriefSyncTime
    {
        get => _lastSimBriefSyncTime;
        private set
        {
            if (SetProperty(ref _lastSimBriefSyncTime, value))
            {
                OnPropertyChanged(nameof(LastSimBriefSyncLabel));
            }
        }
    }

    public string LastSimBriefSyncLabel => LastSimBriefSyncTime is null
        ? "Not imported in this session"
        : $"Imported {LastSimBriefSyncTime:HH:mm}";

    public string BoardingGroupStatus => _engine.State switch
    {
        BoardingRunState.Ready => $"NEXT · GROUP {_engine.CurrentBoardingGroup}",
        BoardingRunState.Boarding => $"GROUP {_engine.CurrentBoardingGroup} BOARDING",
        BoardingRunState.Paused when _engine.Operation == PassengerOperation.Boarding => $"GROUP {_engine.CurrentBoardingGroup} PAUSED",
        BoardingRunState.WaitingForDoor when _engine.Operation == PassengerOperation.Boarding => $"GROUP {_engine.CurrentBoardingGroup} HELD",
        BoardingRunState.Complete => _engine.NoShowCount == 0
            ? "ALL MAPPED GROUPS BOARDED"
            : "BOARDING CLOSED · NO-SHOWS RECORDED",
        BoardingRunState.Deboarding => "DEBOARDING",
        BoardingRunState.Paused => "DEBOARDING PAUSED",
        BoardingRunState.WaitingForDoor => "DEBOARDING HELD",
        BoardingRunState.DeboardingComplete => "CABIN EMPTY",
        _ => "BOARDING GROUPS READY"
    };

    public string BoardingGroupDetail => _engine.State switch
    {
        BoardingRunState.Complete when _engine.NoShowCount > 0 =>
            $"{_engine.BoardedCount} boarded · {_engine.NoShowCount} no-show",
        BoardingRunState.Complete => $"{_engine.BoardedCount} mapped passengers boarded",
        BoardingRunState.Deboarding or BoardingRunState.DeboardingComplete => $"{_engine.OnBoardCount} passengers still onboard",
        _ => $"{_engine.WaitingInCurrentBoardingGroup} waiting in Group {_engine.CurrentBoardingGroup}"
    };

    public string BoardingStateLabel => _engine.State switch
    {
        BoardingRunState.Boarding => "BOARDING IN PROGRESS",
        BoardingRunState.Deboarding => "DEBOARDING IN PROGRESS",
        BoardingRunState.Paused when _engine.Operation == PassengerOperation.Deboarding => "DEBOARDING PAUSED",
        BoardingRunState.Paused => "BOARDING PAUSED",
        BoardingRunState.WaitingForDoor => "WAITING FOR AN OPEN DOOR",
        BoardingRunState.Complete => "BOARDING COMPLETE",
        BoardingRunState.DeboardingComplete => "CABIN EMPTY",
        _ => "READY TO BOARD"
    };

    public string BoardingStateColor => _engine.State switch
    {
        BoardingRunState.Boarding or BoardingRunState.Deboarding or
            BoardingRunState.Complete or BoardingRunState.DeboardingComplete => "#58E68A",
        BoardingRunState.Paused => "#F0C64E",
        BoardingRunState.WaitingForDoor => "#FF9D45",
        _ => "#52A8FF"
    };

    public string PrimaryActionLabel => _engine.State switch
    {
        BoardingRunState.Boarding or BoardingRunState.WaitingForDoor
            when _engine.Operation == PassengerOperation.Boarding => "Pause Boarding",
        BoardingRunState.Deboarding or BoardingRunState.WaitingForDoor => "Pause Deboarding",
        BoardingRunState.Paused when _engine.Operation == PassengerOperation.Deboarding => "Resume Deboarding",
        BoardingRunState.Paused => "Resume Boarding",
        BoardingRunState.Complete => "Start Deboarding",
        BoardingRunState.DeboardingComplete => "Board Again",
        _ => "Start Boarding"
    };

    public string PrimaryActionGlyph => _engine.State is BoardingRunState.Boarding or BoardingRunState.Deboarding or BoardingRunState.WaitingForDoor
        ? "\uE769"
        : "\uE768";
    public bool CanEditPassengerLoad => _engine.State is BoardingRunState.Ready or BoardingRunState.DeboardingComplete;
    public bool CanAdjustPassengerLoad => CanEditPassengerLoad && !HasSimBriefFlight;
    public string PassengerLoadSourceLabel => !HasPassengerManifest
        ? "NO PASSENGER LIST"
        : HasSimBriefFlight ? "SIMBRIEF PRIORITY" : "MANUAL LOAD";

    public string ActiveDoorSummary => _engine.OpenDoorCount switch
    {
        2 => "L1 + L2 OPEN",
        1 when L1DoorOpen => "L1 OPEN",
        1 => "L2 OPEN",
        _ => "ALL DOORS CLOSED"
    };

    public string DoorRoutingSummary => _engine.OpenDoorCount switch
    {
        2 when _engine.Operation == PassengerOperation.Deboarding => "Ticket routing active • First exits through L1 • all other cabins use L2",
        2 => "Ticket routing active • First uses L1 • all other cabins use L2",
        1 when L1DoorOpen => $"All passengers are {OperationVerb} through L1",
        1 => $"All passengers are {OperationVerb} through L2",
        _ => $"{OperationName} is held until L1 or L2 is opened"
    };

    public string OperationEta
    {
        get
        {
            if (_engine.State is BoardingRunState.Complete or BoardingRunState.DeboardingComplete)
            {
                return "Complete";
            }

            if (_engine.OpenDoorCount == 0)
            {
                return "--:--";
            }

            var passengersPerMinute = (60d / 0.55d) * _engine.OpenDoorCount * SelectedSpeedOption.Multiplier;
            var remaining = _engine.Operation == PassengerOperation.Deboarding
                ? _engine.OnBoardCount
                : _engine.RemainingCount;
            var duration = TimeSpan.FromMinutes(remaining / passengersPerMinute);
            return duration.TotalHours >= 1d
                ? $"{(int)duration.TotalHours}:{duration.Minutes:00}:{duration.Seconds:00}"
                : $"{duration.Minutes:00}:{duration.Seconds:00}";
        }
    }

    public string BoardingEta => OperationEta;

    public int FirstClassCount => _engine.Passengers.Count(passenger => passenger.Seat.CabinClass == PassengerCabinClass.First);
    public int BusinessClassCount => _engine.Passengers.Count(passenger => passenger.Seat.CabinClass == PassengerCabinClass.Business);
    public int PremiumEconomyClassCount => _engine.Passengers.Count(passenger => passenger.Seat.CabinClass == PassengerCabinClass.PremiumEconomy);
    public int EconomyClassCount => _engine.Passengers.Count(passenger => passenger.Seat.CabinClass == PassengerCabinClass.Economy);
    public int L1PassengerCount => _engine.Passengers.Count(passenger => passenger.Door == BoardingDoor.L1);
    public int L2PassengerCount => _engine.Passengers.Count(passenger => passenger.Door == BoardingDoor.L2);
    public bool IsFlightCompleted => _engine.State == BoardingRunState.DeboardingComplete;

    public FlightSessionSnapshot CaptureFlightSession() => new(
        DateTimeOffset.UtcNow,
        SelectedCabinLayoutProfile.Id,
        BookedPassengerCount,
        HasSimBriefFlight,
        SimBriefFlightSummary,
        ImportedFlightNumber,
        ImportedOrigin,
        ImportedDestination,
        ImportedAircraftIcao,
        ImportedScheduledDepartureLocal,
        LastSimBriefSyncTime,
        _engine.CaptureSession(),
        _crewRestCycleStartedAt,
        LiveFlightPhase,
        _importedScheduledArrivalLocal);

    public bool RestoreFlightSession(FlightSessionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var profile = CabinLayoutProfileCatalog.Resolve(snapshot.CabinLayoutProfileId);
        SetCabinLayoutProfile(profile, persist: false);
        ApplyBookedPassengerCount(snapshot.BookedPassengerCount, simBriefPriority: snapshot.HasSimBriefFlight);
        if (!_engine.RestoreSession(snapshot.Boarding))
        {
            return false;
        }

        HasSimBriefFlight = snapshot.HasSimBriefFlight;
        SimBriefFlightSummary = snapshot.SimBriefFlightSummary;
        ImportedFlightNumber = snapshot.ImportedFlightNumber;
        ImportedOrigin = snapshot.ImportedOrigin;
        ImportedDestination = snapshot.ImportedDestination;
        ImportedAircraftIcao = snapshot.ImportedAircraftIcao;
        ImportedScheduledDepartureLocal = snapshot.ImportedScheduledDepartureLocal;
        LastSimBriefSyncTime = snapshot.LastSimBriefSyncTime;
        _importedScheduledArrivalLocal = snapshot.ImportedScheduledArrivalLocal;
        _crewRestCycleStartedAt = snapshot.CrewRestCycleStartedAt;
        _liveFlightPhase = string.IsNullOrWhiteSpace(snapshot.LiveFlightPhase)
            ? "Preflight"
            : snapshot.LiveFlightPhase;
        RebuildManifest();
        ClearPassengerVisuals();
        AddActivity($"Previous unfinished flight restored · saved {snapshot.SavedAt.ToLocalTime():dd MMM HH:mm}");
        OnPropertyChanged(nameof(L1DoorOpen));
        OnPropertyChanged(nameof(L2DoorOpen));
        OnPropertyChanged(nameof(LiveFlightPhase));
        OnPropertyChanged(nameof(LiveCabinStatus));
        RefreshFromEngine();
        return true;
    }

    public void AdvancePreview(TimeSpan elapsed)
    {
        _engine.Tick(elapsed, SelectedSpeedOption.Multiplier);
        RefreshFromEngine();
    }

    public bool BoardPassengerFromGate(int passengerId)
    {
        var boarded = _engine.TryBoardPassenger(passengerId);
        if (boarded)
        {
            RefreshFromEngine();
        }

        return boarded;
    }

    public bool MarkPassengerNoShow(int passengerId)
    {
        var marked = _engine.MarkPassengerNoShow(passengerId);
        if (marked)
        {
            RefreshFromEngine();
        }

        return marked;
    }

    public void SetPassengerBoardingHold(int passengerId, bool isHeld) =>
        _engine.SetPassengerBoardingHold(passengerId, isHeld);

    public async Task SyncSimBriefAsync()
    {
        if (!CanEditPassengerLoad)
        {
            SimBriefStatus = "Finish or reset the current cabin operation before importing another OFP.";
            return;
        }

        IsSimBriefSyncing = true;
        SimBriefStatus = "Reading latest generated SimBrief OFP…";
        try
        {
            var summary = await _simBriefClient.FetchLatestOfpAsync(SimBriefPilotId);
            var passengerCount = Math.Max(0, summary.PassengerCount);
            HasSimBriefFlight = true;
            ImportedFlightNumber = summary.FlightNumber;
            ImportedOrigin = summary.Origin;
            ImportedDestination = summary.Destination;
            ImportedAircraftIcao = summary.AircraftIcao;
            ApplyImportedAircraftCabinProfile(summary.AircraftIcao);
            ImportedScheduledDepartureLocal = summary.ScheduledDepartureUtc?.ToLocalTime();
            _importedScheduledArrivalLocal = summary.EstimatedArrivalUtc?.ToLocalTime();
            if (ImportedScheduledDepartureLocal is { } scheduledDeparture)
            {
                _settings.ScheduledDepartureLocal = scheduledDeparture.ToString("HH:mm", CultureInfo.InvariantCulture);
            }

            LastSimBriefSyncTime = _operationsClock.Now;
            ApplyBookedPassengerCount(passengerCount, simBriefPriority: true);
            _settings.SimBriefPilotId = SimBriefPilotId.Trim();
            SimBriefFlightSummary = BuildFlightSummary(summary);
            SimBriefStatus = passengerCount > CabinCapacity
                ? $"Synced {passengerCount} planned passengers. {MappedPassengerCount} have mapped seats; {UnmappedPassengerCount} require a compatible cabin layout."
                : $"Synced {passengerCount} passengers from the latest OFP.";
            AddActivity($"SimBrief sync — {SimBriefFlightSummary} — {passengerCount} passengers");
            await SaveSettingsQuietlyAsync();
        }
        finally
        {
            IsSimBriefSyncing = false;
        }
    }

    public void Dispose()
    {
        _animationTimer.Stop();
        _animationTimer.Tick -= HandleAnimationTick;
        _cabinActivityTimer.Stop();
        _cabinActivityTimer.Tick -= HandleCabinActivityTick;
        GC.SuppressFinalize(this);
    }

    private void HandleCabinActivityTick(object? sender, EventArgs e)
    {
        if (_engine.BoardedCount == 0 && _engine.Operation == PassengerOperation.Boarding)
        {
            return;
        }

        _engine.UpdateCabinActivities(_cabinActivityTimer.Interval, _seatbeltSignOn, _liveFlightPhase);
        UpdatePreDepartureWelcomeService();
        UpdateCabinCrewRest();
        RefreshFromEngine();
        _activityPulseTicks++;
        if (_activityPulseTicks % 15 == 0 && _engine.BoardedCount > 0)
        {
            AddActivity($"Cabin pulse · {CabinActivitySummary}");
        }
    }

    private string OperationName => _engine.Operation == PassengerOperation.Deboarding ? "Deboarding" : "Boarding";
    private string OperationVerb => _engine.Operation == PassengerOperation.Deboarding ? "deboarding" : "boarding";

    private async Task AutoSyncSimBriefAsync()
    {
        try
        {
            await SyncSimBriefAsync();
        }
        catch (Exception exception)
        {
            ShowSimBriefError(exception);
        }
    }

    private void StartPauseOperation()
    {
        if (!HasPassengerManifest)
        {
            AddActivity("Boarding cannot start — import SimBrief or enter a manual passenger count first");
            RefreshFromEngine();
            return;
        }

        if (_engine.State is BoardingRunState.Boarding or BoardingRunState.Deboarding or BoardingRunState.WaitingForDoor)
        {
            var operationName = OperationName;
            _engine.Pause();
            _animationTimer.Stop();
            AddActivity($"{operationName} paused by the user");
            RefreshFromEngine();
            return;
        }

        if (_engine.State == BoardingRunState.Complete)
        {
            _engine.StartDeboarding();
            _lastAnimationTick = DateTime.UtcNow;
            _animationTimer.Start();
            AddActivity(_engine.OpenDoorCount == 0
                ? "Deboarding requested — waiting for an open passenger door"
                : $"Deboarding started through {ActiveDoorSummary.Replace(" OPEN", string.Empty, StringComparison.Ordinal)}");
            RefreshFromEngine();
            return;
        }

        if (_engine.State == BoardingRunState.DeboardingComplete)
        {
            _engine.Reset();
            ClearPassengerVisuals();
            RebuildManifest();
            AddActivity("New boarding run prepared with the current manifest");
        }

        _engine.Start();
        _lastAnimationTick = DateTime.UtcNow;
        _animationTimer.Start();
        AddActivity(_engine.OpenDoorCount == 0
            ? $"{OperationName} requested — waiting for an open passenger door"
            : $"{OperationName} started through {ActiveDoorSummary.Replace(" OPEN", string.Empty, StringComparison.Ordinal)}");
        RefreshFromEngine();
    }

    private void ResetPreview()
    {
        _animationTimer.Stop();
        _engine.Reset();
        ResetCabinServiceState();
        ClearPassengerVisuals();
        RebuildManifest();
        ActivityLog.Clear();
        ActivityLog.Add("Passenger preview reset — manifest ready");
        RefreshFromEngine();
    }

    private void SetCabinLayoutProfile(CabinLayoutProfileOption? profile, bool persist)
    {
        if (profile is null || !SetProperty(ref _selectedCabinLayoutProfile, profile, nameof(SelectedCabinLayoutProfile)))
        {
            return;
        }

        var l1WasOpen = _engine.IsDoorOpen(BoardingDoor.L1);
        var l2WasOpen = _engine.IsDoorOpen(BoardingDoor.L2);
        _animationTimer.Stop();
        _engine = new PassengerBoardingEngine(BookedPassengerCount, profile.Layout);
        _engine.SetDoorOpen(BoardingDoor.L1, l1WasOpen);
        _engine.SetDoorOpen(BoardingDoor.L2, l2WasOpen);
        ClearPassengerVisuals();
        ResetCabinServiceState();
        RebuildManifest();

        _settings.PassengerCabinLayoutId = profile.Id;
        OnPropertyChanged(nameof(IsOperationalCabinLayout));
        OnPropertyChanged(nameof(IsReferenceCabinLayout));
        OnPropertyChanged(nameof(IsFlightFactorCabinLayout));
        OnPropertyChanged(nameof(IsAirlineCabinLayout));
        OnPropertyChanged(nameof(IsBritishAirways777200Er));
        OnPropertyChanged(nameof(IsBritishAirways777300));
        OnPropertyChanged(nameof(L1DoorCanvasLeft));
        OnPropertyChanged(nameof(L2DoorCanvasLeft));
        OnPropertyChanged(nameof(L1DoorOpen));
        OnPropertyChanged(nameof(L2DoorOpen));
        OnPropertyChanged(nameof(CabinCapacity));
        OnPropertyChanged(nameof(MappedPassengerCount));
        OnPropertyChanged(nameof(UnmappedPassengerCount));
        OnPropertyChanged(nameof(HasCapacityOverflow));
        OnPropertyChanged(nameof(PassengerInputMaximum));
        OnPropertyChanged(nameof(CapacitySummary));
        OnPropertyChanged(nameof(ManifestSummary));
        OnPropertyChanged(nameof(CabinActivitySummary));
        AddActivity($"Live cabin layout changed to {profile.Name} — {_engine.Capacity} seats mapped");
        RefreshCrewMarkers();
        RefreshFromEngine();
        if (persist)
        {
            _ = SaveSettingsQuietlyAsync();
        }
    }

    private void SetLoadPreset(object? parameter)
    {
        if (CanAdjustPassengerLoad && parameter is string text && int.TryParse(text, out var percentage))
        {
            BookedPassengerCount = Math.Max(0, (int)Math.Round(CabinCapacity * (percentage / 100d)));
        }
    }

    private void ApplyBookedPassengerCount(int value, bool simBriefPriority)
    {
        var bookedCount = simBriefPriority
            ? Math.Max(0, value)
            : Math.Clamp(value, 0, CabinCapacity);
        if (!SetProperty(ref _bookedPassengerCount, bookedCount, nameof(BookedPassengerCount)))
        {
            return;
        }

        _settings.PassengerPreviewBookedCount = bookedCount;
        _engine.ConfigurePassengerCount(Math.Min(bookedCount, CabinCapacity));
        ClearPassengerVisuals();
        ResetCabinServiceState();
        RebuildManifest();
        AddActivity(simBriefPriority
            ? $"SimBrief set the booked load to {bookedCount} passengers"
            : $"Manifest changed to {bookedCount} booked passengers");
        OnPropertyChanged(nameof(MappedPassengerCount));
        OnPropertyChanged(nameof(UnmappedPassengerCount));
        OnPropertyChanged(nameof(HasCapacityOverflow));
        OnPropertyChanged(nameof(PassengerInputMaximum));
        OnPropertyChanged(nameof(CapacitySummary));
        OnPropertyChanged(nameof(ManifestSummary));
        OnPropertyChanged(nameof(HasPassengerManifest));
        OnPropertyChanged(nameof(PassengerLoadSourceLabel));
        RefreshFromEngine();
    }

    private void SelectPassenger(object? parameter)
    {
        var markerSelection = parameter is PassengerMarkerViewModel;
        var passengerId = parameter switch
        {
            int id => id,
            PassengerManifestEntryViewModel entry => entry.PassengerId,
            PassengerMarkerViewModel marker => marker.PassengerId,
            _ => 0
        };
        if (_manifestByPassengerId.TryGetValue(passengerId, out var selected))
        {
            if (markerSelection && SelectedPassenger?.PassengerId == passengerId)
            {
                ClearPassengerSelection();
                return;
            }

            SelectedPassenger = selected;
            IsPassengerDetailsOpen = !markerSelection;
        }
    }

    private void ClearPassengerSelection()
    {
        IsPassengerDetailsOpen = false;
        SelectedPassenger = null;
    }

    private void HandleAnimationTick(object? sender, EventArgs e)
    {
        var now = DateTime.UtcNow;
        var elapsed = _lastAnimationTick == default ? _animationTimer.Interval : now - _lastAnimationTick;
        _lastAnimationTick = now;
        AdvancePreview(elapsed);
        if (_engine.State == BoardingRunState.Complete)
        {
            _animationTimer.Stop();
            AddActivity($"Boarding complete — {_engine.BoardedCount} passengers seated and secured");
        }
        else if (_engine.State == BoardingRunState.DeboardingComplete)
        {
            _animationTimer.Stop();
            AddActivity($"Deboarding complete — {_engine.DeboardedCount} passengers off the aircraft");
        }
    }

    private void ResumeTimerIfNeeded()
    {
        if (_engine.State is not (BoardingRunState.Boarding or BoardingRunState.Deboarding or BoardingRunState.WaitingForDoor))
        {
            return;
        }

        _lastAnimationTick = DateTime.UtcNow;
        _animationTimer.Start();
    }

    private void RebuildManifest()
    {
        _manifestByPassengerId.Clear();
        var entries = _engine.Passengers
            .OrderBy(passenger => passenger.BoardingGroup)
            .ThenBy(passenger => passenger.Id)
            .Select(passenger => new PassengerManifestEntryViewModel(passenger))
            .ToArray();
        foreach (var entry in entries)
        {
            _manifestByPassengerId.Add(entry.PassengerId, entry);
        }
        PassengerManifest.ReplaceAll(entries);

        SelectedPassenger = null;
        IsPassengerDetailsOpen = false;
        OnPropertyChanged(nameof(FirstClassCount));
        OnPropertyChanged(nameof(BusinessClassCount));
        OnPropertyChanged(nameof(PremiumEconomyClassCount));
        OnPropertyChanged(nameof(EconomyClassCount));
        OnPropertyChanged(nameof(HasPassengerManifest));
        OnPropertyChanged(nameof(PassengerLoadSourceLabel));
        OnPropertyChanged(nameof(ManifestSummary));
    }

    private void RefreshFromEngine()
    {
        UpdatePreDepartureWelcomeService();
        RefreshCrewMarkers();
        var visiblePassengers = _engine.Passengers
            .Where(passenger => passenger.MovementState is not (PassengerMovementState.Waiting or PassengerMovementState.Deboarded))
            .ToArray();
        var visibleIds = visiblePassengers.Select(passenger => passenger.Id).ToHashSet();
        foreach (var passenger in visiblePassengers)
        {
            if (!_markersByPassengerId.TryGetValue(passenger.Id, out var marker))
            {
                marker = new PassengerMarkerViewModel(passenger);
                _markersByPassengerId.Add(passenger.Id, marker);
                PassengerMarkers.Add(marker);
            }

            marker.Update(passenger, _engine.Operation);
        }

        foreach (var staleMarker in PassengerMarkers.Where(marker => !visibleIds.Contains(marker.PassengerId)).ToArray())
        {
            PassengerMarkers.Remove(staleMarker);
            _markersByPassengerId.Remove(staleMarker.PassengerId);
        }

        foreach (var passenger in _engine.Passengers)
        {
            if (_manifestByPassengerId.TryGetValue(passenger.Id, out var manifestEntry))
            {
                manifestEntry.Update(passenger, _engine.Operation);
            }
        }

        foreach (var passenger in _engine.LastSeatedPassengers.Where(passenger => _loggedPassengerIds.Add(passenger.Id)))
        {
            AddActivity($"{passenger.Profile.FullName} secured at {passenger.Seat.Number} via {passenger.Door}");
        }

        foreach (var passenger in _engine.LastDeboardedPassengers)
        {
            AddActivity($"{passenger.Profile.FullName} deboarded through {passenger.Door}");
        }

        OnPropertyChanged(nameof(BoardedPassengerCount));
        OnPropertyChanged(nameof(NoShowPassengerCount));
        OnPropertyChanged(nameof(DeboardedPassengerCount));
        OnPropertyChanged(nameof(WalkingPassengerCount));
        OnPropertyChanged(nameof(OccupyingPassengerCount));
        OnPropertyChanged(nameof(InCabinPassengerCount));
        OnPropertyChanged(nameof(InCabinSummary));
        OnPropertyChanged(nameof(RemainingPassengerCount));
        OnPropertyChanged(nameof(WaitingPassengerCount));
        OnPropertyChanged(nameof(WaitingPassengerSummary));
        OnPropertyChanged(nameof(OperationProgress));
        OnPropertyChanged(nameof(BoardingProgress));
        OnPropertyChanged(nameof(OperationProgressLabel));
        OnPropertyChanged(nameof(BoardingState));
        OnPropertyChanged(nameof(CurrentBoardingGroup));
        OnPropertyChanged(nameof(BoardingGroupStatus));
        OnPropertyChanged(nameof(BoardingGroupDetail));
        OnPropertyChanged(nameof(BoardingStateLabel));
        OnPropertyChanged(nameof(BoardingStateColor));
        OnPropertyChanged(nameof(PrimaryActionLabel));
        OnPropertyChanged(nameof(PrimaryActionGlyph));
        OnPropertyChanged(nameof(CanEditPassengerLoad));
        OnPropertyChanged(nameof(CanAdjustPassengerLoad));
        OnPropertyChanged(nameof(ActiveDoorSummary));
        OnPropertyChanged(nameof(DoorRoutingSummary));
        OnPropertyChanged(nameof(OperationEta));
        OnPropertyChanged(nameof(BoardingEta));
        OnPropertyChanged(nameof(L1PassengerCount));
        OnPropertyChanged(nameof(L2PassengerCount));
        OnPropertyChanged(nameof(ManifestSummary));
    }

    private void ClearPassengerVisuals()
    {
        PassengerMarkers.Clear();
        _markersByPassengerId.Clear();
        _loggedPassengerIds.Clear();
    }

    private void ApplyImportedAircraftCabinProfile(string? aircraftIcao)
    {
        var normalized = (aircraftIcao ?? string.Empty).Trim().ToUpperInvariant();
        var profileId = normalized switch
        {
            "B772" or "B77E" => "british-airways.777-200er",
            "B773" or "B77W" => "british-airways.777-300",
            _ => string.Empty
        };
        if (profileId.Length > 0)
        {
            SetCabinLayoutProfile(CabinLayoutProfileCatalog.Resolve(profileId), persist: true);
        }
    }

    private void RefreshCrewMarkers()
    {
        var crewCount = ExpectedCabinCrewCount;
        while (CabinCrewMarkers.Count < crewCount)
        {
            CabinCrewMarkers.Add(new CabinCrewMarkerViewModel(CabinCrewMarkers.Count + 1));
        }
        while (CabinCrewMarkers.Count > crewCount)
        {
            CabinCrewMarkers.RemoveAt(CabinCrewMarkers.Count - 1);
        }

        var l1X = L1DoorCanvasLeft + 35d;
        var l2X = L2DoorCanvasLeft + 35d;
        var entranceGreeting = _engine.Operation == PassengerOperation.Boarding &&
                               _engine.State is BoardingRunState.Boarding or BoardingRunState.WaitingForDoor or BoardingRunState.Ready;
        var secured = SeatbeltSignOn ||
                      _isAircraftMoving ||
                      _isPushbackActive ||
                      LiveFlightPhase.Contains("Taxi", StringComparison.OrdinalIgnoreCase) ||
                      LiveFlightPhase.Contains("Approach", StringComparison.OrdinalIgnoreCase) ||
                      LiveFlightPhase.Contains("Climb", StringComparison.OrdinalIgnoreCase) ||
                      LiveFlightPhase.Contains("Descent", StringComparison.OrdinalIgnoreCase);
        for (var index = 0; index < CabinCrewMarkers.Count; index++)
        {
            var crew = CabinCrewMarkers[index];
            if (CabinCrewRestSchedule.IsCrewMemberResting(index, CabinCrewMarkers.Count, _crewRestAssignment))
            {
                var restSlot = _crewRestAssignment.RestGroup == 1
                    ? index
                    : index - (CabinCrewMarkers.Count / 2);
                var restX = 820d + (restSlot * 30d);
                crew.Update(
                    Math.Clamp(restX, 820d, 998d),
                    109.5d,
                    $"Crew rest group {_crewRestAssignment.RestGroup} · {CrewRestStatus}",
                    true,
                    true);
                continue;
            }

            if (_preDepartureDrinksActive && index is 2 or 3)
            {
                crew.Update(
                    index == 2 ? 315d : 430d,
                    index == 2 ? 84d : 135d,
                    "Offering Champagne or orange juice before departure",
                    false,
                    false);
                continue;
            }

            if (_isArrivalPreparation && !secured)
            {
                crew.Update(
                    92d + (index * 82d),
                    index % 2 == 0 ? 84d : 135d,
                    "Preparing the cabin for arrival",
                    false,
                    false);
                continue;
            }

            if (entranceGreeting && index < 2)
            {
                var doorOpen = index == 0 ? L1DoorOpen : L2DoorOpen;
                crew.Update(index == 0 ? l1X : l2X, 168d, doorOpen ? "Greeting passengers" : "Standing by at entrance", false, false);
                continue;
            }

            if (secured)
            {
                var stationX = index switch
                {
                    0 => l1X,
                    1 => l2X,
                    _ => 120d + (((index - 2) % 4) * 285d)
                };
                crew.Update(Math.Clamp(stationX, 25d, 1008d), index % 2 == 0 ? 48d : 143d, "Secured at crew station", true, false);
                continue;
            }

            var activeX = 105d + ((index * 117d) % 825d);
            var activeY = index % 2 == 0 ? 70d : 129d;
            var activity = (index % 4) switch
            {
                0 => "Cabin service",
                1 => "Cabin walk-through",
                2 => "Passenger assistance",
                _ => "Galley preparation"
            };
            crew.Update(activeX, activeY, activity, false, false);
        }
    }

    private void UpdateCabinCrewRest()
    {
        var isCruise = LiveFlightPhase.Contains("Cruise", StringComparison.OrdinalIgnoreCase);
        var timeUntilLanding = GetTimeUntilLanding();
        _isArrivalPreparation = timeUntilLanding is { } arrivalRemaining && arrivalRemaining <= TimeSpan.FromHours(1d) ||
                                LiveFlightPhase.Contains("Approach", StringComparison.OrdinalIgnoreCase) ||
                                LiveFlightPhase.Contains("Descent", StringComparison.OrdinalIgnoreCase);
        if (!isCruise)
        {
            if (_crewRestAssignment.IsActive)
            {
                AddActivity("Cabin crew rest rotation ended · all crew returned to duty");
            }

            _crewRestCycleStartedAt = null;
            _crewRestAssignment = default;
            _lastAnnouncedCrewRestGroup = 0;
            _crewRestStatusOverride = _isArrivalPreparation
                ? "ALL CREW ON DUTY · preparing cabin for arrival"
                : "CREW REST · staged long-haul rotation";
            NotifyCrewRestChanged();
            return;
        }

        var currentTime = _operationsClock.Now;
        if (_crewRestCycleStartedAt is null || currentTime < _crewRestCycleStartedAt)
        {
            _crewRestCycleStartedAt = currentTime;
        }

        _crewRestAssignment = CabinCrewRestSchedule.Evaluate(
            _crewRestCycleStartedAt.Value,
            currentTime,
            ExpectedCabinCrewCount,
            timeUntilLanding);
        if (!_crewRestAssignment.IsActive)
        {
            var elapsed = currentTime - _crewRestCycleStartedAt.Value;
            _crewRestStatusOverride = timeUntilLanding is { } landing && landing <= CabinCrewRestSchedule.ArrivalRestCutoff
                ? "ALL CREW ON DUTY · landing within 3 hours"
                : elapsed < CabinCrewRestSchedule.FirstRestDuration + CabinCrewRestSchedule.SecondShiftExtraDuty
                    ? "ALL CREW ON DUTY · second shift remains on duty"
                    : "ALL CREW ON DUTY · scheduled rest complete";
        }
        if (_crewRestAssignment.IsActive && _lastAnnouncedCrewRestGroup != _crewRestAssignment.RestGroup)
        {
            _lastAnnouncedCrewRestGroup = _crewRestAssignment.RestGroup;
            var durationLabel = _crewRestAssignment.RestGroup == 1 ? "3h 30m" : "2h";
            AddActivity($"Cabin crew rest group {_crewRestAssignment.RestGroup} started · {RestingCrewCount} crew · {durationLabel} block");
        }

        NotifyCrewRestChanged();
    }

    private void UpdatePreDepartureWelcomeService()
    {
        var preDeparture = !_isAircraftMoving && !_isPushbackActive &&
                           (LiveFlightPhase.Contains("Preflight", StringComparison.OrdinalIgnoreCase) ||
                            LiveFlightPhase.Contains("Boarding", StringComparison.OrdinalIgnoreCase));
        if (_preDepartureDrinksActive && !preDeparture)
        {
            _preDepartureDrinksActive = false;
            AddActivity("Pre-departure welcome drinks completed");
        }

        if (_preDepartureDrinksStarted || !preDeparture || !_engine.StartPreDepartureDrinkSelection())
        {
            return;
        }

        _preDepartureDrinksStarted = true;
        _preDepartureDrinksActive = true;
        AddActivity("First and front Club World boarded · crew offering Champagne or orange juice");
    }

    private TimeSpan? GetTimeUntilLanding()
    {
        if (_importedScheduledArrivalLocal is not { } arrival)
        {
            return null;
        }

        return arrival > _operationsClock.Now ? arrival - _operationsClock.Now : TimeSpan.Zero;
    }

    private void ToggleSeatbeltFailSafe()
    {
        if (!CanManuallyToggleSeatbeltSign)
        {
            AddActivity("Seat-belt sign is controlled by the live simulator annunciator");
            return;
        }

        _seatbeltSignOn = !_seatbeltSignOn;
        OnPropertyChanged(nameof(SeatbeltSignOn));
        OnPropertyChanged(nameof(SeatbeltSignLabel));
        OnPropertyChanged(nameof(LiveCabinStatus));
        AddActivity($"Manual seat-belt fail-safe set to {(SeatbeltSignOn ? "ON" : "OFF")}");
        _engine.UpdateCabinActivities(TimeSpan.FromSeconds(1), _seatbeltSignOn, _liveFlightPhase);
        RefreshFromEngine();
    }

    private void ResetCabinServiceState()
    {
        _preDepartureDrinksStarted = false;
        _preDepartureDrinksActive = false;
    }

    private void NotifyCrewRestChanged()
    {
        OnPropertyChanged(nameof(RestingCrewCount));
        OnPropertyChanged(nameof(CrewRestStatus));
        OnPropertyChanged(nameof(CabinActivitySummary));
    }

    private async Task SaveSettingsQuietlyAsync()
    {
        if (_settingsStore is null)
        {
            return;
        }

        try
        {
            await _settingsStore.SaveAsync(_settings);
        }
        catch (Exception exception)
        {
            SimBriefStatus = $"The flight was imported, but settings could not be saved: {exception.Message}";
        }
    }

    private void ShowSimBriefError(Exception exception)
    {
        IsSimBriefSyncing = false;
        SimBriefStatus = exception.Message;
        AddActivity($"SimBrief sync failed — {exception.Message}");
    }

    private static string BuildFlightSummary(SimBriefFlightSummary summary)
    {
        var route = string.IsNullOrWhiteSpace(summary.Origin) && string.IsNullOrWhiteSpace(summary.Destination)
            ? string.Empty
            : $"{summary.Origin} → {summary.Destination}";
        var departure = summary.ScheduledDepartureUtc?.ToLocalTime().ToString("dd MMM · HH:mm", CultureInfo.InvariantCulture);
        return string.Join(
            " · ",
            new[] { summary.FlightNumber, route, summary.AircraftIcao, departure }.Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    private void AddActivity(string message)
    {
        ActivityLog.Insert(0, $"{_operationsClock.Now:HH:mm:ss}  {message}");
        while (ActivityLog.Count > 5)
        {
            ActivityLog.RemoveAt(ActivityLog.Count - 1);
        }
    }
}

public sealed record BoardingSpeedOption(string Label, double Multiplier)
{
    public override string ToString() => Label;
}

public sealed class CabinCrewMarkerViewModel : ObservableObject
{
    private double _x;
    private double _y;
    private string _activity = "Standing by";
    private bool _isSecured;
    private bool _isResting;

    public CabinCrewMarkerViewModel(int crewNumber)
    {
        CrewNumber = crewNumber;
        Role = crewNumber == 1 ? "Cabin Manager" : $"Cabin Crew {crewNumber}";
    }

    public int CrewNumber { get; }
    public string Role { get; }
    public double CanvasLeft => _x - 6d;
    public double CanvasTop => _y - 6d;
    public bool IsSecured { get => _isSecured; private set => SetProperty(ref _isSecured, value); }
    public bool IsResting { get => _isResting; private set => SetProperty(ref _isResting, value); }
    public string ToolTip => $"{Role} · {_activity}";

    public void Update(double x, double y, string activity, bool isSecured, bool isResting)
    {
        x = Math.Clamp(x, 24d, 1009d);
        y = Math.Clamp(y, 36d, 156d);
        if (Math.Abs(_x - x) > 0.01d)
        {
            _x = x;
            OnPropertyChanged(nameof(CanvasLeft));
        }
        if (Math.Abs(_y - y) > 0.01d)
        {
            _y = y;
            OnPropertyChanged(nameof(CanvasTop));
        }
        if (!string.Equals(_activity, activity, StringComparison.Ordinal))
        {
            _activity = activity;
            OnPropertyChanged(nameof(ToolTip));
        }
        IsSecured = isSecured;
        IsResting = isResting;
    }
}

public sealed class PassengerMarkerViewModel : ObservableObject
{
    private double _x;
    private double _y;
    private PassengerMovementState _movementState;
    private PassengerOperation _operation;
    private string _activityLabel = "Awaiting boarding";
    private string _markerColor = "#33B8E8";
    private string _markerBorderColor = "#D9F6FF";

    public PassengerMarkerViewModel(BoardingPassenger passenger)
    {
        PassengerId = passenger.Id;
        FullName = passenger.Profile.FullName;
        SeatNumber = passenger.Seat.Number;
        CabinClassName = FormatCabinClass(passenger.Seat.CabinClass);
        BoardingGroup = passenger.BoardingGroup;
        SeatX = passenger.Seat.X;
        SeatY = passenger.Seat.Y;
    }

    public int PassengerId { get; }
    public string FullName { get; }
    public string SeatNumber { get; }
    public string CabinClassName { get; }
    public int BoardingGroup { get; }
    public double SeatX { get; }
    public double SeatY { get; }
    public string DoorLabel { get; private set; } = string.Empty;

    public double X
    {
        get => _x;
        private set
        {
            if (SetProperty(ref _x, value))
            {
                OnPropertyChanged(nameof(CanvasLeft));
            }
        }
    }

    public double Y
    {
        get => _y;
        private set
        {
            if (SetProperty(ref _y, value))
            {
                OnPropertyChanged(nameof(CanvasTop));
            }
        }
    }

    public double CanvasLeft => X - (MarkerSize / 2d);
    public double CanvasTop => Y - (MarkerSize / 2d);

    public PassengerMovementState MovementState
    {
        get => _movementState;
        private set
        {
            if (!SetProperty(ref _movementState, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsWalking));
            OnPropertyChanged(nameof(IsOccupyingSeat));
            OnPropertyChanged(nameof(IsSecured));
            OnPropertyChanged(nameof(MarkerSize));
            OnPropertyChanged(nameof(CanvasLeft));
            OnPropertyChanged(nameof(CanvasTop));
            OnPropertyChanged(nameof(ToolTip));
        }
    }

    public bool IsWalking => MovementState == PassengerMovementState.Walking;
    public bool IsOccupyingSeat => MovementState == PassengerMovementState.OccupyingSeat;
    public bool IsSecured => MovementState == PassengerMovementState.Seated;
    public string MarkerColor { get => _markerColor; private set => SetProperty(ref _markerColor, value); }
    public string MarkerBorderColor { get => _markerBorderColor; private set => SetProperty(ref _markerBorderColor, value); }
    public double MarkerSize => IsWalking ? 12d : 10d;
    public string ToolTip => $"{FullName} • Seat {SeatNumber} • Group {BoardingGroup} • {CabinClassName} • {DoorLabel} • {_activityLabel}";

    public void Update(BoardingPassenger passenger, PassengerOperation operation)
    {
        _operation = operation;
        X = passenger.Position.X;
        Y = passenger.Position.Y;
        MovementState = passenger.MovementState;
        _activityLabel = PassengerManifestEntryViewModel.FormatActivity(passenger.CabinActivity);
        (MarkerColor, MarkerBorderColor) = GetActivityColors(passenger);
        var doorLabel = passenger.Door?.ToString() ?? string.Empty;
        if (!string.Equals(DoorLabel, doorLabel, StringComparison.Ordinal))
        {
            DoorLabel = doorLabel;
            OnPropertyChanged(nameof(DoorLabel));
        }

        OnPropertyChanged(nameof(ToolTip));
    }

    private static (string Fill, string Border) GetActivityColors(BoardingPassenger passenger) => passenger.CabinActivity switch
    {
        PassengerCabinActivity.SeatbeltFastened => ("#58E68A", "#D9FFE6"),
        PassengerCabinActivity.SettlingIn => ("#FF9D45", "#FFE0BE"),
        PassengerCabinActivity.SelectingWelcomeDrink => ("#F0C64E", "#FFF0AA"),
        PassengerCabinActivity.Sleeping => ("#9B86FF", "#E3DCFF"),
        PassengerCabinActivity.WatchingMovie => ("#2E90FF", "#D4E9FF"),
        PassengerCabinActivity.Gaming => ("#B76CFF", "#ECD8FF"),
        PassengerCabinActivity.UsingPhone => ("#4CB8FF", "#D9F2FF"),
        PassengerCabinActivity.Reading => ("#E8AE4A", "#FFF0C2"),
        PassengerCabinActivity.Working => ("#46C8C2", "#D5FFFC"),
        PassengerCabinActivity.Talking => ("#E06CCB", "#FFDDF8"),
        PassengerCabinActivity.WalkingToLavatory or PassengerCabinActivity.UsingLavatory or PassengerCabinActivity.ReturningToSeat => ("#20D7D1", "#D6FFFD"),
        PassengerCabinActivity.WalkingToSeat or PassengerCabinActivity.Deboarding => ("#33B8E8", "#D9F6FF"),
        _ => passenger.Seat.CabinClass switch
        {
            PassengerCabinClass.First => ("#C978ED", "#F1D7FF"),
            PassengerCabinClass.Business => ("#2E90FF", "#D4E9FF"),
            PassengerCabinClass.PremiumEconomy => ("#F0C64E", "#FFF0AA"),
            _ => ("#21CED8", "#D6FCFF")
        }
    };

    private static string FormatCabinClass(PassengerCabinClass cabinClass) => cabinClass switch
    {
        PassengerCabinClass.PremiumEconomy => "Premium Economy",
        _ => cabinClass.ToString()
    };
}

public sealed class PassengerManifestEntryViewModel : ObservableObject
{
    private string _doorLabel = "—";
    private string _statusLabel = "Awaiting boarding";
    private string _statusColor = "#8DA0B8";
    private string _currentActivity = "Awaiting boarding";
    private string _seatbeltStatus = "Not fastened";
    private string _seatbeltColor = "#8DA0B8";

    public PassengerManifestEntryViewModel(BoardingPassenger passenger)
    {
        PassengerId = passenger.Id;
        PassengerNumber = $"PAX {passenger.Id:000}";
        FullName = passenger.Profile.FullName;
        Age = passenger.Profile.Age;
        Nationality = passenger.Profile.Nationality;
        TravelPurpose = passenger.Profile.TravelPurpose;
        FrequentFlyerTier = passenger.Profile.FrequentFlyerTier;
        CheckedBags = passenger.Profile.CheckedBags;
        Assistance = passenger.Profile.Assistance;
        BookingReference = passenger.Profile.BookingReference;
        Email = passenger.Profile.Email;
        SeatNumber = passenger.Seat.Number;
        SeatX = passenger.Seat.X;
        SeatY = passenger.Seat.Y;
        CabinClassName = passenger.Seat.CabinClass == PassengerCabinClass.PremiumEconomy
            ? "Premium Economy"
            : passenger.Seat.CabinClass.ToString();
        BoardingGroup = passenger.BoardingGroup;
        Update(passenger, PassengerOperation.Boarding);
    }

    public int PassengerId { get; }
    public string PassengerNumber { get; }
    public string FullName { get; }
    public int Age { get; }
    public string Nationality { get; }
    public string TravelPurpose { get; }
    public string FrequentFlyerTier { get; }
    public int CheckedBags { get; }
    public string Assistance { get; }
    public string BookingReference { get; }
    public string Email { get; }
    public string SeatNumber { get; }
    public double SeatX { get; }
    public double SeatY { get; }
    public string CabinClassName { get; }
    public int BoardingGroup { get; }

    public string DoorLabel
    {
        get => _doorLabel;
        private set => SetProperty(ref _doorLabel, value);
    }

    public string StatusLabel
    {
        get => _statusLabel;
        private set => SetProperty(ref _statusLabel, value);
    }

    public string StatusColor
    {
        get => _statusColor;
        private set => SetProperty(ref _statusColor, value);
    }

    public string CurrentActivity
    {
        get => _currentActivity;
        private set => SetProperty(ref _currentActivity, value);
    }

    public string SeatbeltStatus
    {
        get => _seatbeltStatus;
        private set => SetProperty(ref _seatbeltStatus, value);
    }

    public string SeatbeltColor
    {
        get => _seatbeltColor;
        private set => SetProperty(ref _seatbeltColor, value);
    }

    public void Update(BoardingPassenger passenger, PassengerOperation operation)
    {
        DoorLabel = passenger.Door?.ToString() ?? "—";
        CurrentActivity = FormatActivity(passenger.CabinActivity);
        SeatbeltStatus = passenger.SeatbeltFastened ? "Fastened" : "Not fastened";
        SeatbeltColor = passenger.SeatbeltFastened ? "#58E68A" : "#FFB55F";
        (StatusLabel, StatusColor) = passenger.MovementState switch
        {
            PassengerMovementState.Waiting => ("Awaiting boarding", "#8DA0B8"),
            PassengerMovementState.Walking when operation == PassengerOperation.Deboarding => ("Walking to exit", "#52A8FF"),
            PassengerMovementState.Walking => ("Walking to seat", "#52A8FF"),
            PassengerMovementState.OccupyingSeat => ("Occupying seat", "#FF9D45"),
            PassengerMovementState.Seated => ("Seated · secured", "#58E68A"),
            PassengerMovementState.Deboarded => ("Deboarded", "#B88CFF"),
            _ => (passenger.MovementState.ToString(), "#8DA0B8")
        };
    }

    internal static string FormatActivity(PassengerCabinActivity activity) => activity switch
    {
        PassengerCabinActivity.AwaitingBoarding => "Awaiting boarding",
        PassengerCabinActivity.WalkingToSeat => "Walking to seat",
        PassengerCabinActivity.SettlingIn => "Stowing bags / settling in",
        PassengerCabinActivity.SelectingWelcomeDrink => "Choosing Champagne or orange juice",
        PassengerCabinActivity.SeatbeltFastened => "Seated with seat belt fastened",
        PassengerCabinActivity.WatchingMovie => "Watching a movie",
        PassengerCabinActivity.Gaming => "Gaming",
        PassengerCabinActivity.UsingPhone => "Using phone",
        PassengerCabinActivity.Sleeping => "Sleeping",
        PassengerCabinActivity.Reading => "Reading",
        PassengerCabinActivity.Working => "Working",
        PassengerCabinActivity.Talking => "Talking",
        PassengerCabinActivity.WalkingToLavatory => "Walking to the lavatory",
        PassengerCabinActivity.UsingLavatory => "Using the lavatory",
        PassengerCabinActivity.ReturningToSeat => "Returning to seat",
        PassengerCabinActivity.Deboarding => "Walking to exit",
        PassengerCabinActivity.OffAircraft => "Off aircraft",
        _ => activity.ToString()
    };
}

internal sealed class SeatNumberComparer : IComparer<string>
{
    public static SeatNumberComparer Instance { get; } = new();

    public int Compare(string? left, string? right)
    {
        var rowComparison = ParseRow(left).CompareTo(ParseRow(right));
        return rowComparison != 0 ? rowComparison : string.Compare(left, right, StringComparison.Ordinal);
    }

    private static int ParseRow(string? seatNumber)
    {
        var digits = new string((seatNumber ?? string.Empty).TakeWhile(char.IsDigit).ToArray());
        return int.TryParse(digits, out var row) ? row : int.MaxValue;
    }
}
