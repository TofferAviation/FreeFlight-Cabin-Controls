using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Threading;
using FreeFlight.CabinControl.App.Infrastructure;
using FreeFlight.CabinControl.Core.Configuration;
using FreeFlight.CabinControl.Core.Passengers;

namespace FreeFlight.CabinControl.App.ViewModels;

public sealed class PassengerFlowViewModel : PageViewModel, IDisposable
{
    private readonly AppSettings _settings;
    private readonly PassengerBoardingEngine _engine;
    private readonly DispatcherTimer _animationTimer;
    private readonly Dictionary<int, PassengerMarkerViewModel> _markersByPassengerId = [];
    private readonly HashSet<int> _loggedPassengerIds = [];
    private int _bookedPassengerCount;
    private BoardingSpeedOption _selectedSpeedOption;
    private DateTime _lastAnimationTick;

    public PassengerFlowViewModel(AppSettings settings, SharedStatusViewModel status)
        : base("Passenger Flow", "Simulator-free FF777 boarding preview and cabin manifest")
    {
        _settings = settings;
        Status = status;
        _engine = new PassengerBoardingEngine(settings.PassengerPreviewBookedCount);
        _bookedPassengerCount = _engine.TargetPassengerCount;
        SpeedOptions =
        [
            new BoardingSpeedOption("1× Real-time", 1d),
            new BoardingSpeedOption("2× Preview", 2d),
            new BoardingSpeedOption("4× Fast Preview", 4d)
        ];
        _selectedSpeedOption = SpeedOptions.MinBy(option =>
            Math.Abs(option.Multiplier - settings.PassengerPreviewSpeed)) ?? SpeedOptions[1];

        _engine.SetDoorOpen(BoardingDoor.L2, true);
        ActivityLog.Add("Preview manifest prepared — L2 is open for boarding");
        StartPauseCommand = new RelayCommand(_ => StartPauseBoarding());
        ResetCommand = new RelayCommand(_ => ResetBoarding());
        SetLoadPresetCommand = new RelayCommand(SetLoadPreset);

        _animationTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(50)
        };
        _animationTimer.Tick += HandleAnimationTick;
        RefreshFromEngine();
    }

    public SharedStatusViewModel Status { get; }

    public ICommand StartPauseCommand { get; }

    public ICommand ResetCommand { get; }

    public ICommand SetLoadPresetCommand { get; }

    public ObservableCollection<PassengerMarkerViewModel> PassengerMarkers { get; } = [];

    public ObservableCollection<string> ActivityLog { get; } = [];

    public IReadOnlyList<BoardingSpeedOption> SpeedOptions { get; }

    public int CabinCapacity => _engine.Capacity;

    public int BookedPassengerCount
    {
        get => _bookedPassengerCount;
        set
        {
            if (!CanEditPassengerLoad)
            {
                return;
            }

            var clamped = Math.Clamp(value, 1, CabinCapacity);
            if (!SetProperty(ref _bookedPassengerCount, clamped))
            {
                return;
            }

            _settings.PassengerPreviewBookedCount = clamped;
            _engine.ConfigurePassengerCount(clamped);
            ClearPassengerVisuals();
            AddActivity($"Manifest changed to {clamped} booked passengers");
            RefreshFromEngine();
        }
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
            OnPropertyChanged(nameof(BoardingEta));
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

    public int BoardedPassengerCount => _engine.BoardedCount;

    public int WalkingPassengerCount => _engine.WalkingCount;

    public int RemainingPassengerCount => _engine.RemainingCount;

    public int WaitingPassengerCount => _engine.WaitingCount;

    public double BoardingProgress => _engine.Progress * 100d;

    public BoardingRunState BoardingState => _engine.State;

    public string BoardingStateLabel => _engine.State switch
    {
        BoardingRunState.Boarding => "BOARDING IN PROGRESS",
        BoardingRunState.Paused => "BOARDING PAUSED",
        BoardingRunState.WaitingForDoor => "WAITING FOR AN OPEN DOOR",
        BoardingRunState.Complete => "BOARDING COMPLETE",
        _ => "READY TO BOARD"
    };

    public string BoardingStateColor => _engine.State switch
    {
        BoardingRunState.Boarding => "#58E68A",
        BoardingRunState.Complete => "#58E68A",
        BoardingRunState.Paused => "#F0C64E",
        BoardingRunState.WaitingForDoor => "#FF9D45",
        _ => "#52A8FF"
    };

    public string PrimaryActionLabel => _engine.State switch
    {
        BoardingRunState.Boarding or BoardingRunState.WaitingForDoor => "Pause Boarding",
        BoardingRunState.Paused => "Resume Boarding",
        BoardingRunState.Complete => "Board Again",
        _ => "Start Boarding"
    };

    public string PrimaryActionGlyph => _engine.State is BoardingRunState.Boarding or BoardingRunState.WaitingForDoor
        ? "\uE769"
        : "\uE768";

    public bool CanEditPassengerLoad => _engine.State is BoardingRunState.Ready or BoardingRunState.Complete;

    public string ActiveDoorSummary => _engine.OpenDoorCount switch
    {
        2 => "L1 + L2 OPEN",
        1 when L1DoorOpen => "L1 OPEN",
        1 => "L2 OPEN",
        _ => "ALL DOORS CLOSED"
    };

    public string DoorRoutingSummary => _engine.OpenDoorCount switch
    {
        2 => "L1 routes First and forward Business • L2 routes the remaining cabin",
        1 when L1DoorOpen => "All remaining passengers are routing through L1",
        1 => "All remaining passengers are routing through L2",
        _ => "Boarding is held until L1 or L2 is opened"
    };

    public string BoardingEta
    {
        get
        {
            if (_engine.State == BoardingRunState.Complete)
            {
                return "Complete";
            }

            if (_engine.OpenDoorCount == 0)
            {
                return "--:--";
            }

            var passengersPerMinute = 70d * _engine.OpenDoorCount * SelectedSpeedOption.Multiplier;
            var remainingMinutes = _engine.RemainingCount / passengersPerMinute;
            var duration = TimeSpan.FromMinutes(remainingMinutes);
            return duration.TotalHours >= 1d
                ? $"{(int)duration.TotalHours}:{duration.Minutes:00}:{duration.Seconds:00}"
                : $"{duration.Minutes:00}:{duration.Seconds:00}";
        }
    }

    public int FirstClassCount => _engine.Passengers.Count(passenger =>
        passenger.Seat.CabinClass == PassengerCabinClass.First);

    public int BusinessClassCount => _engine.Passengers.Count(passenger =>
        passenger.Seat.CabinClass == PassengerCabinClass.Business);

    public int EconomyClassCount => _engine.Passengers.Count(passenger =>
        passenger.Seat.CabinClass == PassengerCabinClass.Economy);

    public int L1PassengerCount => _engine.Passengers.Count(passenger =>
        passenger.Door == BoardingDoor.L1);

    public int L2PassengerCount => _engine.Passengers.Count(passenger =>
        passenger.Door == BoardingDoor.L2);

    public void AdvancePreview(TimeSpan elapsed)
    {
        _engine.Tick(elapsed, SelectedSpeedOption.Multiplier);
        RefreshFromEngine();
    }

    public void Dispose()
    {
        _animationTimer.Stop();
        _animationTimer.Tick -= HandleAnimationTick;
        GC.SuppressFinalize(this);
    }

    private void StartPauseBoarding()
    {
        if (_engine.State is BoardingRunState.Boarding or BoardingRunState.WaitingForDoor)
        {
            _engine.Pause();
            _animationTimer.Stop();
            AddActivity("Boarding paused by the user");
            RefreshFromEngine();
            return;
        }

        if (_engine.State == BoardingRunState.Complete)
        {
            _engine.Reset();
            ClearPassengerVisuals();
            AddActivity("New boarding run prepared with the current manifest");
        }

        _engine.Start();
        _lastAnimationTick = DateTime.UtcNow;
        _animationTimer.Start();
        AddActivity(_engine.OpenDoorCount == 0
            ? "Boarding requested — waiting for an open passenger door"
            : $"Boarding started through {ActiveDoorSummary.Replace(" OPEN", string.Empty, StringComparison.Ordinal)}");
        RefreshFromEngine();
    }

    private void ResetBoarding()
    {
        _animationTimer.Stop();
        _engine.Reset();
        ClearPassengerVisuals();
        ActivityLog.Clear();
        ActivityLog.Add("Boarding preview reset — manifest ready");
        RefreshFromEngine();
    }

    private void SetLoadPreset(object? parameter)
    {
        if (!CanEditPassengerLoad || parameter is not string text || !int.TryParse(text, out var percentage))
        {
            return;
        }

        BookedPassengerCount = Math.Max(1, (int)Math.Round(CabinCapacity * (percentage / 100d)));
    }

    private void HandleAnimationTick(object? sender, EventArgs e)
    {
        var now = DateTime.UtcNow;
        var elapsed = _lastAnimationTick == default
            ? _animationTimer.Interval
            : now - _lastAnimationTick;
        _lastAnimationTick = now;
        AdvancePreview(elapsed);
        if (_engine.State == BoardingRunState.Complete)
        {
            _animationTimer.Stop();
            AddActivity($"Boarding complete — {_engine.BoardedCount} passengers seated");
        }
    }

    private void ResumeTimerIfNeeded()
    {
        if (_engine.State is not (BoardingRunState.Boarding or BoardingRunState.WaitingForDoor))
        {
            return;
        }

        _lastAnimationTick = DateTime.UtcNow;
        _animationTimer.Start();
    }

    private void RefreshFromEngine()
    {
        var visiblePassengers = _engine.Passengers
            .Where(passenger => passenger.MovementState != PassengerMovementState.Waiting)
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

            marker.Update(passenger);
        }

        foreach (var staleMarker in PassengerMarkers.Where(marker => !visibleIds.Contains(marker.PassengerId)).ToArray())
        {
            PassengerMarkers.Remove(staleMarker);
            _markersByPassengerId.Remove(staleMarker.PassengerId);
        }

        foreach (var passenger in _engine.LastSeatedPassengers.Where(passenger =>
                     _loggedPassengerIds.Add(passenger.Id)))
        {
            AddActivity($"Passenger {passenger.Id:000} seated at {passenger.Seat.Number} via {passenger.Door}");
        }

        OnPropertyChanged(nameof(BoardedPassengerCount));
        OnPropertyChanged(nameof(WalkingPassengerCount));
        OnPropertyChanged(nameof(RemainingPassengerCount));
        OnPropertyChanged(nameof(WaitingPassengerCount));
        OnPropertyChanged(nameof(BoardingProgress));
        OnPropertyChanged(nameof(BoardingState));
        OnPropertyChanged(nameof(BoardingStateLabel));
        OnPropertyChanged(nameof(BoardingStateColor));
        OnPropertyChanged(nameof(PrimaryActionLabel));
        OnPropertyChanged(nameof(PrimaryActionGlyph));
        OnPropertyChanged(nameof(CanEditPassengerLoad));
        OnPropertyChanged(nameof(ActiveDoorSummary));
        OnPropertyChanged(nameof(DoorRoutingSummary));
        OnPropertyChanged(nameof(BoardingEta));
        OnPropertyChanged(nameof(FirstClassCount));
        OnPropertyChanged(nameof(BusinessClassCount));
        OnPropertyChanged(nameof(EconomyClassCount));
        OnPropertyChanged(nameof(L1PassengerCount));
        OnPropertyChanged(nameof(L2PassengerCount));
    }

    private void ClearPassengerVisuals()
    {
        PassengerMarkers.Clear();
        _markersByPassengerId.Clear();
        _loggedPassengerIds.Clear();
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

    public PassengerMarkerViewModel(BoardingPassenger passenger)
    {
        PassengerId = passenger.Id;
        SeatNumber = passenger.Seat.Number;
        CabinClassName = passenger.Seat.CabinClass.ToString();
        Update(passenger);
    }

    public int PassengerId { get; }

    public string SeatNumber { get; }

    public string CabinClassName { get; }

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
            OnPropertyChanged(nameof(MarkerSize));
            OnPropertyChanged(nameof(CanvasLeft));
            OnPropertyChanged(nameof(CanvasTop));
            OnPropertyChanged(nameof(ToolTip));
        }
    }

    public bool IsWalking => MovementState == PassengerMovementState.Walking;

    public double MarkerSize => IsWalking ? 10d : 6d;

    public string ToolTip => $"Passenger {PassengerId:000} • Seat {SeatNumber} • {CabinClassName} • {DoorLabel} • {MovementState}";

    public void Update(BoardingPassenger passenger)
    {
        X = passenger.Position.X;
        Y = passenger.Position.Y;
        MovementState = passenger.MovementState;
        var doorLabel = passenger.Door?.ToString() ?? string.Empty;
        if (!string.Equals(DoorLabel, doorLabel, StringComparison.Ordinal))
        {
            DoorLabel = doorLabel;
            OnPropertyChanged(nameof(DoorLabel));
            OnPropertyChanged(nameof(ToolTip));
        }
    }
}
