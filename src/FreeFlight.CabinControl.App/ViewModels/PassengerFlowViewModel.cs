using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Threading;
using FreeFlight.CabinControl.App.Infrastructure;
using FreeFlight.CabinControl.App.Services;
using FreeFlight.CabinControl.Core.Configuration;
using FreeFlight.CabinControl.Core.Passengers;
using FreeFlight.CabinControl.Core.Persistence;

namespace FreeFlight.CabinControl.App.ViewModels;

public sealed class PassengerFlowViewModel : PageViewModel, IDisposable
{
    private readonly AppSettings _settings;
    private readonly ISettingsStore? _settingsStore;
    private readonly ISimBriefClient _simBriefClient;
    private readonly PassengerBoardingEngine _engine;
    private readonly DispatcherTimer _animationTimer;
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
    private CabinLayoutProfileOption _selectedCabinLayoutProfile;

    public PassengerFlowViewModel(
        AppSettings settings,
        SharedStatusViewModel status,
        ISettingsStore? settingsStore = null,
        ISimBriefClient? simBriefClient = null)
        : base("Passenger Flow", "Simulator-free 777 boarding, deboarding and passenger manifest")
    {
        _settings = settings;
        _settingsStore = settingsStore;
        _simBriefClient = simBriefClient ?? new SimBriefClient();
        _selectedCabinLayoutProfile = CabinLayoutProfileCatalog.Resolve(settings.PassengerCabinLayoutId);
        _settings.PassengerCabinLayoutId = _selectedCabinLayoutProfile.Id;
        _simBriefPilotId = settings.SimBriefPilotId;
        _simBriefAutoSync = settings.SimBriefAutoSync;
        Status = status;
        _engine = new PassengerBoardingEngine(settings.PassengerPreviewBookedCount);
        _bookedPassengerCount = Math.Max(1, settings.PassengerPreviewBookedCount);
        SpeedOptions =
        [
            new BoardingSpeedOption("Real Ops · 30–45 min", 0.06d),
            new BoardingSpeedOption("1× Preview", 1d),
            new BoardingSpeedOption("2× Preview", 2d),
            new BoardingSpeedOption("4× Fast Preview", 4d)
        ];
        _selectedSpeedOption = SpeedOptions.MinBy(option =>
            Math.Abs(option.Multiplier - settings.PassengerPreviewSpeed)) ?? SpeedOptions[1];

        _engine.SetDoorOpen(BoardingDoor.L2, true);
        ActivityLog.Add("Preview manifest prepared — L2 is open for boarding");
        StartPauseCommand = new RelayCommand(_ => StartPauseOperation());
        ResetCommand = new RelayCommand(_ => ResetPreview());
        SetLoadPresetCommand = new RelayCommand(SetLoadPreset);
        OpenManifestCommand = new RelayCommand(_ => IsManifestOpen = true);
        CloseManifestCommand = new RelayCommand(_ => IsManifestOpen = false);
        SelectPassengerCommand = new RelayCommand(SelectPassenger);
        ClosePassengerDetailsCommand = new RelayCommand(_ => IsPassengerDetailsOpen = false);
        SyncSimBriefCommand = new AsyncRelayCommand(SyncSimBriefAsync, ShowSimBriefError);

        _animationTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(50)
        };
        _animationTimer.Tick += HandleAnimationTick;
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
    public ObservableCollection<PassengerMarkerViewModel> PassengerMarkers { get; } = [];
    public ObservableCollection<PassengerManifestEntryViewModel> PassengerManifest { get; } = [];
    public ObservableCollection<string> ActivityLog { get; } = [];
    public IReadOnlyList<BoardingSpeedOption> SpeedOptions { get; }
    public IReadOnlyList<CabinLayoutProfileOption> CabinLayoutProfiles => CabinLayoutProfileCatalog.All;
    public bool IsOperationalCabinLayout => SelectedCabinLayoutProfile.IsOperational;
    public bool IsReferenceCabinLayout => !IsOperationalCabinLayout;

    public CabinLayoutProfileOption SelectedCabinLayoutProfile
    {
        get => _selectedCabinLayoutProfile;
        set => SetCabinLayoutProfile(value, persist: true);
    }

    public int CabinCapacity => _engine.Capacity;
    public int MappedPassengerCount => _engine.TargetPassengerCount;
    public int UnmappedPassengerCount => Math.Max(0, BookedPassengerCount - MappedPassengerCount);
    public bool HasCapacityOverflow => UnmappedPassengerCount > 0;
    public int PassengerInputMaximum => Math.Max(CabinCapacity, BookedPassengerCount);
    public string CapacitySummary => HasCapacityOverflow
        ? $"{MappedPassengerCount} mapped · {UnmappedPassengerCount} unmapped"
        : $"of {CabinCapacity} seats";
    public string ManifestSummary => HasCapacityOverflow
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
        private set => SetProperty(ref _selectedPassenger, value);
    }

    public int BoardedPassengerCount => _engine.BoardedCount;
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

    public string BoardingGroupStatus => _engine.State switch
    {
        BoardingRunState.Ready => $"NEXT · GROUP {_engine.CurrentBoardingGroup}",
        BoardingRunState.Boarding => $"GROUP {_engine.CurrentBoardingGroup} BOARDING",
        BoardingRunState.Paused when _engine.Operation == PassengerOperation.Boarding => $"GROUP {_engine.CurrentBoardingGroup} PAUSED",
        BoardingRunState.WaitingForDoor when _engine.Operation == PassengerOperation.Boarding => $"GROUP {_engine.CurrentBoardingGroup} HELD",
        BoardingRunState.Complete => "ALL MAPPED GROUPS BOARDED",
        BoardingRunState.Deboarding => "DEBOARDING",
        BoardingRunState.Paused => "DEBOARDING PAUSED",
        BoardingRunState.WaitingForDoor => "DEBOARDING HELD",
        BoardingRunState.DeboardingComplete => "CABIN EMPTY",
        _ => "BOARDING GROUPS READY"
    };

    public string BoardingGroupDetail => _engine.State switch
    {
        BoardingRunState.Complete => $"{MappedPassengerCount} mapped passengers boarded",
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
    public string PassengerLoadSourceLabel => HasSimBriefFlight ? "SIMBRIEF PRIORITY" : "MANUAL LOAD";

    public string ActiveDoorSummary => _engine.OpenDoorCount switch
    {
        2 => "L1 + L2 OPEN",
        1 when L1DoorOpen => "L1 OPEN",
        1 => "L2 OPEN",
        _ => "ALL DOORS CLOSED"
    };

    public string DoorRoutingSummary => _engine.OpenDoorCount switch
    {
        2 when _engine.Operation == PassengerOperation.Deboarding => "Ticket routing active • First exits through L1 • Business and Economy use L2",
        2 => "Ticket routing active • First uses L1 • Business and Economy use L2",
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
    public int EconomyClassCount => _engine.Passengers.Count(passenger => passenger.Seat.CabinClass == PassengerCabinClass.Economy);
    public int L1PassengerCount => _engine.Passengers.Count(passenger => passenger.Door == BoardingDoor.L1);
    public int L2PassengerCount => _engine.Passengers.Count(passenger => passenger.Door == BoardingDoor.L2);

    public void AdvancePreview(TimeSpan elapsed)
    {
        _engine.Tick(elapsed, SelectedSpeedOption.Multiplier);
        RefreshFromEngine();
    }

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
            var passengerCount = Math.Max(1, summary.PassengerCount);
            HasSimBriefFlight = true;
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
        GC.SuppressFinalize(this);
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

        if (!profile.IsOperational &&
            _engine.State is BoardingRunState.Boarding or BoardingRunState.Deboarding or BoardingRunState.WaitingForDoor)
        {
            _engine.Pause();
            _animationTimer.Stop();
            AddActivity("Cabin operation paused while viewing an airline seat-map reference");
            RefreshFromEngine();
        }

        _settings.PassengerCabinLayoutId = profile.Id;
        OnPropertyChanged(nameof(IsOperationalCabinLayout));
        OnPropertyChanged(nameof(IsReferenceCabinLayout));
        AddActivity($"Live cabin layout changed to {profile.Name}");
        if (persist)
        {
            _ = SaveSettingsQuietlyAsync();
        }
    }

    private void SetLoadPreset(object? parameter)
    {
        if (CanAdjustPassengerLoad && parameter is string text && int.TryParse(text, out var percentage))
        {
            BookedPassengerCount = Math.Max(1, (int)Math.Round(CabinCapacity * (percentage / 100d)));
        }
    }

    private void ApplyBookedPassengerCount(int value, bool simBriefPriority)
    {
        var bookedCount = simBriefPriority
            ? Math.Max(1, value)
            : Math.Clamp(value, 1, CabinCapacity);
        if (!SetProperty(ref _bookedPassengerCount, bookedCount, nameof(BookedPassengerCount)))
        {
            return;
        }

        _settings.PassengerPreviewBookedCount = bookedCount;
        _engine.ConfigurePassengerCount(Math.Min(bookedCount, CabinCapacity));
        ClearPassengerVisuals();
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
        RefreshFromEngine();
    }

    private void SelectPassenger(object? parameter)
    {
        var passengerId = parameter switch
        {
            int id => id,
            PassengerManifestEntryViewModel entry => entry.PassengerId,
            PassengerMarkerViewModel marker => marker.PassengerId,
            _ => 0
        };
        if (_manifestByPassengerId.TryGetValue(passengerId, out var selected))
        {
            SelectedPassenger = selected;
            IsPassengerDetailsOpen = true;
        }
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
        PassengerManifest.Clear();
        _manifestByPassengerId.Clear();
        foreach (var passenger in _engine.Passengers
                     .OrderBy(passenger => passenger.BoardingGroup)
                     .ThenBy(passenger => passenger.Id))
        {
            var entry = new PassengerManifestEntryViewModel(passenger);
            PassengerManifest.Add(entry);
            _manifestByPassengerId.Add(passenger.Id, entry);
        }

        SelectedPassenger = null;
        IsPassengerDetailsOpen = false;
        OnPropertyChanged(nameof(FirstClassCount));
        OnPropertyChanged(nameof(BusinessClassCount));
        OnPropertyChanged(nameof(EconomyClassCount));
    }

    private void RefreshFromEngine()
    {
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
        return string.Join(" · ", new[] { summary.FlightNumber, route }.Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    private void AddActivity(string message)
    {
        ActivityLog.Insert(0, $"{DateTime.Now:HH:mm:ss}  {message}");
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

public sealed class PassengerMarkerViewModel : ObservableObject
{
    private double _x;
    private double _y;
    private PassengerMovementState _movementState;
    private PassengerOperation _operation;

    public PassengerMarkerViewModel(BoardingPassenger passenger)
    {
        PassengerId = passenger.Id;
        FullName = passenger.Profile.FullName;
        SeatNumber = passenger.Seat.Number;
        CabinClassName = passenger.Seat.CabinClass.ToString();
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
    public double MarkerSize => IsWalking ? 9d : 8d;
    public string ToolTip => $"{FullName} • Seat {SeatNumber} • Group {BoardingGroup} • {CabinClassName} • {DoorLabel} • " +
                             (_operation == PassengerOperation.Deboarding && IsWalking ? "Walking to exit" : MovementState);

    public void Update(BoardingPassenger passenger, PassengerOperation operation)
    {
        _operation = operation;
        X = passenger.Position.X;
        Y = passenger.Position.Y;
        MovementState = passenger.MovementState;
        var doorLabel = passenger.Door?.ToString() ?? string.Empty;
        if (!string.Equals(DoorLabel, doorLabel, StringComparison.Ordinal))
        {
            DoorLabel = doorLabel;
            OnPropertyChanged(nameof(DoorLabel));
        }

        OnPropertyChanged(nameof(ToolTip));
    }
}

public sealed class PassengerManifestEntryViewModel : ObservableObject
{
    private string _doorLabel = "—";
    private string _statusLabel = "Awaiting boarding";
    private string _statusColor = "#8DA0B8";

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
        SeatNumber = passenger.Seat.Number;
        CabinClassName = passenger.Seat.CabinClass.ToString();
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
    public string SeatNumber { get; }
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

    public void Update(BoardingPassenger passenger, PassengerOperation operation)
    {
        DoorLabel = passenger.Door?.ToString() ?? "—";
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
