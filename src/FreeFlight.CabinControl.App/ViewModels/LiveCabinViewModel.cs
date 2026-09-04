using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Threading;
using FreeFlight.CabinControl.App.Services;
using FreeFlight.CabinControl.Core.Cabin;
using FreeFlight.CabinControl.Core.Integration;
using FreeFlight.CabinControl.Core.Passengers;

namespace FreeFlight.CabinControl.App.ViewModels;

public sealed class LiveCabinViewModel : PageViewModel, IDisposable
{
    private readonly DispatcherTimer _crewAnimationTimer;
    private readonly Random _random = new(5051);
    private readonly Dictionary<int, CrewMotionTarget> _crewTargets = [];
    private readonly Dictionary<int, string> _lavatoryAssignments = [];
    private LavatoryQueueManager _lavatoryManager;
    private PassengerCabinLayout? _lastLayout;
    private DateTimeOffset _nextCrewTaskRefresh = DateTimeOffset.MinValue;
    private DateTimeOffset _nextLavatoryRefresh = DateTimeOffset.MinValue;

    public LiveCabinViewModel(PassengerFlowViewModel passengers)
        : base("Live Cabin", "Live passenger, crew, doors and cabin activity")
    {
        Passengers = passengers;
        _lavatoryManager = CreateLavatoryManager();
        Passengers.PropertyChanged += HandlePassengerPropertyChanged;
        Passengers.CabinCrewMarkers.CollectionChanged += HandleCrewCollectionChanged;
        _crewAnimationTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(80)
        };
        _crewAnimationTimer.Tick += HandleCrewAnimationTick;
        _crewAnimationTimer.Start();
        RebuildDoorRoster();
        RefreshLavatorySnapshots();
        EnsureCrewTargets(force: true);
    }

    public PassengerFlowViewModel Passengers { get; }

    public ObservableCollection<CabinDoorStatusViewModel> Doors { get; } = [];

    public ObservableCollection<LavatoryQueueStatusViewModel> Lavatories { get; } = [];

    public bool SeatMapOverrideActive =>
        Passengers.HasSimBriefFlight && SimBriefImportState.Latest?.SeatMapOverrideApplied == true;

    public string SeatMapOverrideLabel
    {
        get
        {
            var latest = SimBriefImportState.Latest;
            return SeatMapOverrideActive && latest is not null
                ? $"SEAT MAP OVERRIDE ACTIVE · SimBrief {latest.SimBriefRequestedPassengerCount} → mapped capacity {latest.PassengerCount}"
                : "Seat map capacity matched";
        }
    }

    public string DoorSummary => Doors.Count == 0
        ? "No door map available"
        : $"{Doors.Count(door => door.IsOpen)} open · {Doors.Count(door => door.IsEmergencyExit)} emergency exits · {Doors.Count} mapped exits";

    public string LavatorySummary
    {
        get
        {
            var occupied = Lavatories.Count(item => item.IsOccupied);
            var waiting = Lavatories.Sum(item => item.QueueLength);
            return $"{occupied}/{Lavatories.Count} occupied · {waiting} waiting";
        }
    }

    public void ApplyTelemetry(CabinTelemetrySnapshot snapshot)
    {
        EnsureDoorRosterMatchesLayout();
        var projected = CabinDoorTelemetry.Project(Passengers.SelectedCabinLayoutProfile.Layout, snapshot.Signals);
        foreach (var state in projected)
        {
            var row = Doors.FirstOrDefault(door => string.Equals(door.Id, state.Door.Id, StringComparison.OrdinalIgnoreCase));
            row?.Apply(state);
        }

        OnPropertyChanged(nameof(DoorSummary));
        EnsureCrewTargets(force: false);
        UpdateLavatorySimulation(force: false);
    }

    public void Dispose()
    {
        _crewAnimationTimer.Stop();
        _crewAnimationTimer.Tick -= HandleCrewAnimationTick;
        Passengers.PropertyChanged -= HandlePassengerPropertyChanged;
        Passengers.CabinCrewMarkers.CollectionChanged -= HandleCrewCollectionChanged;
        GC.SuppressFinalize(this);
    }

    private void HandlePassengerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PassengerFlowViewModel.SelectedCabinLayoutProfile))
        {
            RebuildDoorRoster();
            ResetLavatories();
            _crewTargets.Clear();
            EnsureCrewTargets(force: true);
        }

        if (e.PropertyName is nameof(PassengerFlowViewModel.HasSimBriefFlight) or
            nameof(PassengerFlowViewModel.SimBriefStatus) or
            nameof(PassengerFlowViewModel.ImportedAircraftIcao) or
            nameof(PassengerFlowViewModel.BookedPassengerCount))
        {
            OnPropertyChanged(nameof(SeatMapOverrideActive));
            OnPropertyChanged(nameof(SeatMapOverrideLabel));
        }
    }

    private void HandleCrewCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        EnsureCrewTargets(force: true);

    private void EnsureDoorRosterMatchesLayout()
    {
        var layout = Passengers.SelectedCabinLayoutProfile.Layout;
        if (_lastLayout != layout)
        {
            RebuildDoorRoster();
            ResetLavatories();
        }
    }

    private void RebuildDoorRoster()
    {
        _lastLayout = Passengers.SelectedCabinLayoutProfile.Layout;
        Doors.Clear();
        foreach (var door in CabinDoorCatalog.ForLayout(_lastLayout.Value))
        {
            Doors.Add(new CabinDoorStatusViewModel(door));
        }

        OnPropertyChanged(nameof(DoorSummary));
    }

    private void HandleCrewAnimationTick(object? sender, EventArgs e)
    {
        EnsureCrewTargets(force: false);
        UpdateLavatorySimulation(force: false);
        foreach (var crew in Passengers.CabinCrewMarkers)
        {
            if (!_crewTargets.TryGetValue(crew.CrewNumber, out var target) || crew.IsSecured || crew.IsResting)
            {
                continue;
            }

            var currentX = crew.CanvasLeft + 6d;
            var currentY = crew.CanvasTop + 6d;
            var nextX = currentX + ((target.X - currentX) * 0.10d);
            var nextY = currentY + ((target.Y - currentY) * 0.10d);
            if (Math.Abs(target.X - currentX) < 1.5d && Math.Abs(target.Y - currentY) < 1.5d)
            {
                target = CreateCrewTarget(crew.CrewNumber, target.Task);
                _crewTargets[crew.CrewNumber] = target;
            }

            crew.Update(nextX, nextY, target.Task.Detail, false, false);
        }
    }

    private void EnsureCrewTargets(bool force)
    {
        var now = DateTimeOffset.UtcNow;
        if (!force && now < _nextCrewTaskRefresh && Passengers.CabinCrewMarkers.All(crew => _crewTargets.ContainsKey(crew.CrewNumber)))
        {
            return;
        }

        _nextCrewTaskRefresh = now.AddSeconds(12);
        foreach (var crew in Passengers.CabinCrewMarkers)
        {
            if (crew.IsSecured || crew.IsResting)
            {
                continue;
            }

            var task = CreateTaskForCrew(crew.CrewNumber);
            _crewTargets[crew.CrewNumber] = CreateCrewTarget(crew.CrewNumber, task);
        }
    }

    private CrewMotionTarget CreateCrewTarget(int crewNumber, CabinCrewServiceTask task)
    {
        var narrowBody = IsNarrowBodyLayout(Passengers.SelectedCabinLayoutProfile.Layout);
        var minX = narrowBody ? 90d : 85d;
        var maxX = narrowBody ? 915d : 945d;
        var x = minX + (_random.NextDouble() * (maxX - minX));
        var y = narrowBody
            ? 96d + ((_random.NextDouble() - 0.5d) * 8d)
            : crewNumber % 2 == 0 ? 76d : 145d;
        return new CrewMotionTarget(x, y, task);
    }

    private CabinCrewServiceTask CreateTaskForCrew(int crewNumber)
    {
        var phase = Passengers.LiveFlightPhase;
        if (phase.Contains("Preflight", StringComparison.OrdinalIgnoreCase) ||
            phase.Contains("Boarding", StringComparison.OrdinalIgnoreCase))
        {
            return (crewNumber % 3) switch
            {
                0 => new CabinCrewServiceTask(CabinCrewServiceTaskType.PreflightChecks, "Preflight checks", "Checking cabin and emergency equipment"),
                1 => new CabinCrewServiceTask(CabinCrewServiceTaskType.PreparingGalley, "Galley preparation", "Loading drinks, service items and meal carts"),
                _ => new CabinCrewServiceTask(CabinCrewServiceTaskType.PassengerAssistance, "Passenger assistance", "Helping passengers settle and stow cabin baggage")
            };
        }

        if (phase.Contains("Cruise", StringComparison.OrdinalIgnoreCase))
        {
            return (crewNumber % 5) switch
            {
                0 => CabinCrewTaskFactory.HeatingMeal("Chicken main meal", "forward galley", 0.55d),
                1 => CabinCrewTaskFactory.DeliveringMeal("Chicken main meal", PickServiceSeat(), 0.35d),
                2 => CabinCrewTaskFactory.ServingDrink("tea / coffee", PickServiceSeat(), 0.45d),
                3 => CabinCrewTaskFactory.ServingDrink("water / soft drinks / wine", PickServiceSeat(), 0.65d),
                _ => CabinCrewTaskFactory.CollectingTrays("active cabin zone", 0.30d)
            };
        }

        if (phase.Contains("Descent", StringComparison.OrdinalIgnoreCase) ||
            phase.Contains("Approach", StringComparison.OrdinalIgnoreCase))
        {
            return new CabinCrewServiceTask(
                CabinCrewServiceTaskType.CabinCheck,
                "Cabin secure check",
                "Checking seat belts, seat backs, tables and cabin readiness for landing");
        }

        return new CabinCrewServiceTask(
            CabinCrewServiceTaskType.CabinCheck,
            "Cabin walk-through",
            "Monitoring passengers and cabin condition");
    }

    private string PickServiceSeat()
    {
        if (Passengers.PassengerManifest.Count == 0)
        {
            return "assigned cabin zone";
        }

        return Passengers.PassengerManifest[_random.Next(Passengers.PassengerManifest.Count)].SeatNumber;
    }

    private void UpdateLavatorySimulation(bool force)
    {
        var now = DateTimeOffset.UtcNow;
        if (!force && now < _nextLavatoryRefresh)
        {
            return;
        }
        _nextLavatoryRefresh = now.AddMilliseconds(500);

        var validIds = new HashSet<int>();
        foreach (var passenger in Passengers.PassengerManifest)
        {
            var activity = passenger.CurrentActivity;
            var involved = activity.Contains("lavatory", StringComparison.OrdinalIgnoreCase) ||
                           activity.Contains("Returning to seat", StringComparison.OrdinalIgnoreCase);
            if (!involved)
            {
                if (_lavatoryAssignments.Remove(passenger.PassengerId, out _))
                {
                    _lavatoryManager.CancelRequest(passenger.PassengerId);
                    _lavatoryManager.Release(passenger.PassengerId);
                }
                continue;
            }

            validIds.Add(passenger.PassengerId);
            if (!_lavatoryAssignments.ContainsKey(passenger.PassengerId) &&
                activity.Contains("lavatory", StringComparison.OrdinalIgnoreCase))
            {
                var target = _lavatoryManager.Snapshot()
                    .OrderBy(item => item.QueueLength + item.Occupants.Count)
                    .ThenBy(item => Math.Abs(item.Lavatory.LongitudinalStation - NormalizeSeatStation(passenger.SeatX)))
                    .First();
                _lavatoryManager.Request(passenger.PassengerId, target.Lavatory.Id);
                _lavatoryAssignments[passenger.PassengerId] = target.Lavatory.Id;
            }

            if (activity.Contains("Returning to seat", StringComparison.OrdinalIgnoreCase) &&
                _lavatoryAssignments.Remove(passenger.PassengerId, out _))
            {
                _lavatoryManager.CancelRequest(passenger.PassengerId);
                _lavatoryManager.Release(passenger.PassengerId);
            }
        }

        foreach (var stale in _lavatoryAssignments.Keys.Where(id => !validIds.Contains(id)).ToArray())
        {
            _lavatoryManager.CancelRequest(stale);
            _lavatoryManager.Release(stale);
            _lavatoryAssignments.Remove(stale);
        }

        RefreshLavatorySnapshots();
    }

    private void ResetLavatories()
    {
        _lavatoryAssignments.Clear();
        _lavatoryManager = CreateLavatoryManager();
        RefreshLavatorySnapshots();
    }

    private LavatoryQueueManager CreateLavatoryManager() => new(
        CabinLavatoryCatalog.ForAircraftFamily(IsNarrowBodyLayout(Passengers.SelectedCabinLayoutProfile.Layout)));

    private void RefreshLavatorySnapshots()
    {
        var snapshots = _lavatoryManager.Snapshot();
        Lavatories.Clear();
        foreach (var snapshot in snapshots)
        {
            Lavatories.Add(new LavatoryQueueStatusViewModel(snapshot));
        }
        OnPropertyChanged(nameof(LavatorySummary));
    }

    private static bool IsNarrowBodyLayout(PassengerCabinLayout layout) => layout is
        PassengerCabinLayout.BritishAirwaysA319100 or
        PassengerCabinLayout.BritishAirwaysA320200 or
        PassengerCabinLayout.BritishAirwaysA320Neo or
        PassengerCabinLayout.BritishAirwaysA321200 or
        PassengerCabinLayout.BritishAirwaysA321Neo or
        PassengerCabinLayout.BritishAirwaysEmbraer190;

    private static double NormalizeSeatStation(double x) => Math.Clamp(x / 1033d, 0d, 1d);

    private sealed record CrewMotionTarget(double X, double Y, CabinCrewServiceTask Task);
}

public sealed class CabinDoorStatusViewModel : ObservableObject
{
    private string _status = "Not detected";
    private string _statusColor = "#71869F";
    private bool _isOpen;
    private bool _isAvailable;

    public CabinDoorStatusViewModel(CabinDoorDefinition definition)
    {
        Definition = definition;
    }

    public CabinDoorDefinition Definition { get; }
    public string Id => Definition.Id;
    public string DisplayName => Definition.DisplayName;
    public bool IsEmergencyExit => Definition.IsEmergencyExit;
    public string KindLabel => Definition.Kind switch
    {
        CabinDoorKind.PassengerDoor => "Passenger door",
        CabinDoorKind.ServiceDoor => "Service door",
        CabinDoorKind.EmergencyExit => "Emergency exit",
        CabinDoorKind.CargoDoor => "Cargo door",
        _ => "Exit"
    };
    public bool IsOpen { get => _isOpen; private set => SetProperty(ref _isOpen, value); }
    public bool IsAvailable { get => _isAvailable; private set => SetProperty(ref _isAvailable, value); }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public string StatusColor { get => _statusColor; private set => SetProperty(ref _statusColor, value); }

    public void Apply(CabinDoorState state)
    {
        IsAvailable = state.IsAvailable;
        IsOpen = state.IsOpen;
        if (!state.IsAvailable)
        {
            Status = Definition.IsEmergencyExit ? "Emergency exit" : "Not detected";
            StatusColor = Definition.IsEmergencyExit ? "#FFB55F" : "#71869F";
        }
        else if (state.IsOpen)
        {
            Status = "OPEN";
            StatusColor = "#58E68A";
        }
        else
        {
            Status = "CLOSED";
            StatusColor = "#69B8FF";
        }
    }
}

public sealed class LavatoryQueueStatusViewModel
{
    public LavatoryQueueStatusViewModel(LavatoryQueueSnapshot snapshot)
    {
        Id = snapshot.Lavatory.Id;
        Zone = snapshot.Lavatory.Zone;
        IsOccupied = snapshot.IsOccupied;
        QueueLength = snapshot.QueueLength;
        Status = IsOccupied ? "OCCUPIED" : "AVAILABLE";
        Detail = QueueLength == 0 ? "No queue" : $"{QueueLength} passenger{(QueueLength == 1 ? string.Empty : "s")} waiting";
    }

    public string Id { get; }
    public string Zone { get; }
    public bool IsOccupied { get; }
    public int QueueLength { get; }
    public string Status { get; }
    public string Detail { get; }
}
