using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Threading;
using FreeFlight.CabinControl.Core.Cabin;
using FreeFlight.CabinControl.Core.Integration;
using FreeFlight.CabinControl.Core.Passengers;

namespace FreeFlight.CabinControl.App.ViewModels;

public sealed class LiveCabinViewModel : PageViewModel, IDisposable
{
    private readonly DispatcherTimer _crewAnimationTimer;
    private readonly Random _random = new(5051);
    private readonly Dictionary<int, CrewMotionTarget> _crewTargets = [];
    private PassengerCabinLayout? _lastLayout;
    private DateTimeOffset _nextCrewTaskRefresh = DateTimeOffset.MinValue;

    public LiveCabinViewModel(PassengerFlowViewModel passengers)
        : base("Live Cabin", "Live passenger, crew, doors and cabin activity")
    {
        Passengers = passengers;
        Passengers.PropertyChanged += HandlePassengerPropertyChanged;
        Passengers.CabinCrewMarkers.CollectionChanged += HandleCrewCollectionChanged;
        _crewAnimationTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(80)
        };
        _crewAnimationTimer.Tick += HandleCrewAnimationTick;
        _crewAnimationTimer.Start();
        RebuildDoorRoster();
        EnsureCrewTargets(force: true);
    }

    public PassengerFlowViewModel Passengers { get; }

    public ObservableCollection<CabinDoorStatusViewModel> Doors { get; } = [];

    public string DoorSummary => Doors.Count == 0
        ? "No door map available"
        : $"{Doors.Count(door => door.IsOpen)} open · {Doors.Count(door => door.IsEmergencyExit)} emergency exits · {Doors.Count} mapped exits";

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
            _crewTargets.Clear();
            EnsureCrewTargets(force: true);
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
        var narrowBody = Passengers.IsNarrowBodyCabinLayout;
        var minX = narrowBody ? 90d : 85d;
        var maxX = narrowBody ? 915d : 945d;
        var x = minX + (_random.NextDouble() * (maxX - minX));
        var y = narrowBody
            ? 89d + ((_random.NextDouble() - 0.5d) * 8d)
            : crewNumber % 2 == 0 ? 72d : 127d;
        return new CrewMotionTarget(x, y, task);
    }

    private CabinCrewServiceTask CreateTaskForCrew(int crewNumber)
    {
        var phase = Passengers.LiveFlightPhase;
        if (phase.Contains("Preflight", StringComparison.OrdinalIgnoreCase) ||
            phase.Contains("Boarding", StringComparison.OrdinalIgnoreCase))
        {
            return crewNumber % 3 switch
            {
                0 => new CabinCrewServiceTask(CabinCrewServiceTaskType.PreflightChecks, "Preflight checks", "Checking cabin and emergency equipment"),
                1 => new CabinCrewServiceTask(CabinCrewServiceTaskType.PreparingGalley, "Galley preparation", "Loading drinks, service items and meal carts"),
                _ => new CabinCrewServiceTask(CabinCrewServiceTaskType.PassengerAssistance, "Passenger assistance", "Helping passengers settle and stow cabin baggage")
            };
        }

        if (phase.Contains("Cruise", StringComparison.OrdinalIgnoreCase))
        {
            return crewNumber % 5 switch
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
