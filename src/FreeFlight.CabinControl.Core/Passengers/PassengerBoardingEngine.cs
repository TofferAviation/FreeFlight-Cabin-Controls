namespace FreeFlight.CabinControl.Core.Passengers;

public sealed class PassengerBoardingEngine
{
    private const double PassengerWalkingSpeed = 155d;
    private const double BaseSpawnIntervalSeconds = 0.42d;
    private readonly PassengerCabinLayoutDefinition _layoutDefinition;
    private readonly IReadOnlyList<CabinSeat> _cabinSeats;
    private readonly List<BoardingPassenger> _passengers = [];
    private readonly List<BoardingPassenger> _activePassengers = [];
    private readonly List<BoardingPassenger> _occupyingPassengers = [];
    private readonly List<BoardingPassenger> _lastSeatedPassengers = [];
    private readonly List<BoardingPassenger> _lastDeboardedPassengers = [];
    private readonly List<BoardingPassenger> _deboardingQueue = [];
    private readonly HashSet<BoardingDoor> _openDoors = [];
    private readonly HashSet<int> _boardingHoldPassengerIds = [];
    private readonly HashSet<int> _noShowPassengerIds = [];
    private int _nextPassengerIndex;
    private int _nextDeboardingPassengerIndex;
    private int _boardedCount;
    private int _deboardedCount;
    private int _currentBoardingGroup;
    private double _spawnAccumulator;

    public PassengerBoardingEngine(
        int targetPassengerCount = 228,
        PassengerCabinLayout layout = PassengerCabinLayout.FlightFactor777V2)
    {
        _layoutDefinition = PassengerCabinLayouts.Create(layout);
        _cabinSeats = _layoutDefinition.Seats;
        ConfigurePassengerCount(targetPassengerCount);
    }

    public PassengerCabinLayout Layout => _layoutDefinition.Layout;

    public int Capacity => _cabinSeats.Count;

    public int TargetPassengerCount { get; private set; }

    public int ExpectedBoardingCount => Math.Max(0, TargetPassengerCount - NoShowCount);

    public int BoardedCount => _boardedCount;

    public int NoShowCount => _noShowPassengerIds.Count;

    public int DeboardedCount => _deboardedCount;

    public int OnBoardCount => Math.Max(0, ExpectedBoardingCount - DeboardedCount);

    public int WalkingCount => _activePassengers.Count;

    public int OccupyingCount => _occupyingPassengers.Count;

    public int InCabinCount => WalkingCount + OccupyingCount;

    public int RemainingCount => Math.Max(0, ExpectedBoardingCount - BoardedCount);

    public int WaitingCount => Math.Max(0, ExpectedBoardingCount - BoardedCount - InCabinCount);

    public int OpenDoorCount => _openDoors.Count;

    public int CurrentBoardingGroup => _currentBoardingGroup;

    public int WaitingInCurrentBoardingGroup => _passengers.Count(passenger =>
        passenger.BoardingGroup == CurrentBoardingGroup &&
        passenger.MovementState == PassengerMovementState.Waiting &&
        !_boardingHoldPassengerIds.Contains(passenger.Id) &&
        !_noShowPassengerIds.Contains(passenger.Id));

    public IReadOnlyList<int> BoardingGroups => _passengers
        .Select(passenger => passenger.BoardingGroup)
        .Distinct()
        .Order()
        .ToArray();

    public double Progress => ExpectedBoardingCount == 0
        ? TargetPassengerCount == 0 ? 0d : 1d
        : BoardedCount / (double)ExpectedBoardingCount;

    public double DeboardingProgress => ExpectedBoardingCount == 0
        ? 0d
        : DeboardedCount / (double)ExpectedBoardingCount;

    public BoardingRunState State { get; private set; } = BoardingRunState.Ready;

    public PassengerOperation Operation { get; private set; } = PassengerOperation.Boarding;

    public IReadOnlyList<BoardingPassenger> Passengers => _passengers;

    public IReadOnlyList<BoardingPassenger> LastSeatedPassengers => _lastSeatedPassengers;

    public IReadOnlyList<BoardingPassenger> LastDeboardedPassengers => _lastDeboardedPassengers;

    public IReadOnlyCollection<BoardingDoor> OpenDoors => _openDoors;

    public bool IsDoorOpen(BoardingDoor door) => _openDoors.Contains(door);

    public void ConfigurePassengerCount(int passengerCount)
    {
        TargetPassengerCount = Math.Clamp(passengerCount, 0, Capacity);
        InitializeManifest();
    }

    public void SetDoorOpen(BoardingDoor door, bool isOpen)
    {
        if (isOpen)
        {
            _openDoors.Add(door);
        }
        else
        {
            _openDoors.Remove(door);
        }

        if (State == BoardingRunState.WaitingForDoor && _openDoors.Count > 0)
        {
            State = Operation == PassengerOperation.Boarding
                ? BoardingRunState.Boarding
                : BoardingRunState.Deboarding;
        }
        else if ((State is BoardingRunState.Boarding or BoardingRunState.Deboarding) && _openDoors.Count == 0)
        {
            State = BoardingRunState.WaitingForDoor;
        }
    }

    public void Start()
    {
        if (TargetPassengerCount == 0)
        {
            State = BoardingRunState.Ready;
            return;
        }

        if (ExpectedBoardingCount == 0)
        {
            State = BoardingRunState.Complete;
            return;
        }

        if (State is BoardingRunState.Complete or BoardingRunState.DeboardingComplete)
        {
            return;
        }

        State = _openDoors.Count > 0
            ? Operation == PassengerOperation.Boarding
                ? BoardingRunState.Boarding
                : BoardingRunState.Deboarding
            : BoardingRunState.WaitingForDoor;
    }

    public void StartDeboarding()
    {
        if (State != BoardingRunState.Complete)
        {
            return;
        }

        Operation = PassengerOperation.Deboarding;
        _activePassengers.Clear();
        _occupyingPassengers.Clear();
        _deboardingQueue.Clear();
        var random = new Random(778_300 + TargetPassengerCount + ((int)Layout * 997));
        _deboardingQueue.AddRange(_passengers
            .Where(passenger => !_noShowPassengerIds.Contains(passenger.Id) &&
                                passenger.MovementState == PassengerMovementState.Seated)
            .Select(passenger => new
            {
                Passenger = passenger,
                Score = Math.Abs(
                    passenger.Seat.X - GetDoorEntryPoint(
                        passenger.Seat.CabinClass == PassengerCabinClass.First
                            ? BoardingDoor.L1
                            : BoardingDoor.L2).X) + (random.NextDouble() * 220d)
            })
            .OrderBy(item => item.Score)
            .Select(item => item.Passenger));
        _nextDeboardingPassengerIndex = 0;
        _deboardedCount = 0;
        _spawnAccumulator = 0d;
        State = _openDoors.Count > 0
            ? BoardingRunState.Deboarding
            : BoardingRunState.WaitingForDoor;
    }

    public void Pause()
    {
        if (State is BoardingRunState.Boarding or BoardingRunState.Deboarding or BoardingRunState.WaitingForDoor)
        {
            State = BoardingRunState.Paused;
        }
    }

    public void Reset()
    {
        InitializeManifest();
    }

    public PassengerBoardingSession CaptureSession() => new(
        Layout,
        TargetPassengerCount,
        State,
        Operation,
        CurrentBoardingGroup,
        _openDoors.Order().ToArray(),
        _passengers.Select(passenger => new BoardingPassengerSession(
            passenger.Id,
            passenger.Door,
            passenger.MovementState,
            passenger.Position,
            passenger.CabinActivity,
            passenger.SeatbeltFastened,
            _boardingHoldPassengerIds.Contains(passenger.Id),
            _noShowPassengerIds.Contains(passenger.Id))).ToArray());

    public bool RestoreSession(PassengerBoardingSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.Layout != Layout || session.TargetPassengerCount != TargetPassengerCount)
        {
            return false;
        }

        InitializeManifest();
        Operation = session.Operation;
        _openDoors.Clear();
        foreach (var door in session.OpenDoors)
        {
            _openDoors.Add(door);
        }

        var states = session.Passengers.ToDictionary(item => item.PassengerId);
        foreach (var passenger in _passengers)
        {
            if (!states.TryGetValue(passenger.Id, out var saved))
            {
                continue;
            }

            if (saved.IsBoardingHeld) _boardingHoldPassengerIds.Add(passenger.Id);
            if (saved.IsNoShow) _noShowPassengerIds.Add(passenger.Id);
            passenger.Door = saved.Door;
            passenger.Waypoints.Clear();
            var restoredMovement = saved.MovementState;
            if (restoredMovement is PassengerMovementState.Walking or PassengerMovementState.OccupyingSeat)
            {
                restoredMovement = session.Operation == PassengerOperation.Deboarding
                    ? PassengerMovementState.Seated
                    : PassengerMovementState.Waiting;
            }

            passenger.MovementState = restoredMovement;
            passenger.Position = restoredMovement == PassengerMovementState.Waiting
                ? default
                : saved.Position;
            passenger.CabinActivity = restoredMovement == PassengerMovementState.Seated
                ? saved.CabinActivity
                : restoredMovement switch
                {
                    PassengerMovementState.Deboarded => PassengerCabinActivity.OffAircraft,
                    _ => PassengerCabinActivity.AwaitingBoarding
                };
            passenger.SeatbeltFastened = restoredMovement == PassengerMovementState.Seated && saved.SeatbeltFastened;
            passenger.SecondsUntilActivityChange = 30d + (passenger.Id % 90);
        }

        _boardedCount = _passengers.Count(passenger => passenger.MovementState == PassengerMovementState.Seated);
        _deboardedCount = _passengers.Count(passenger => passenger.MovementState == PassengerMovementState.Deboarded);
        _currentBoardingGroup = session.CurrentBoardingGroup;
        _nextPassengerIndex = 0;
        _nextDeboardingPassengerIndex = 0;
        _spawnAccumulator = 0d;
        State = session.State switch
        {
            BoardingRunState.Complete => BoardingRunState.Complete,
            BoardingRunState.DeboardingComplete => BoardingRunState.DeboardingComplete,
            BoardingRunState.Ready => BoardingRunState.Ready,
            _ => BoardingRunState.Paused
        };
        return true;
    }

    public bool TryBoardPassenger(int passengerId)
    {
        if (Operation != PassengerOperation.Boarding)
        {
            return false;
        }

        var passenger = _passengers.FirstOrDefault(candidate => candidate.Id == passengerId);
        if (passenger is null ||
            passenger.MovementState == PassengerMovementState.Seated ||
            _boardingHoldPassengerIds.Contains(passengerId) ||
            _noShowPassengerIds.Contains(passengerId))
        {
            return false;
        }

        _activePassengers.Remove(passenger);
        _occupyingPassengers.Remove(passenger);
        passenger.Door ??= SelectDoor(passenger);
        passenger.Position = new CabinPoint(passenger.Seat.X, passenger.Seat.Y);
        passenger.Waypoints.Clear();
        passenger.MovementState = PassengerMovementState.Seated;
        passenger.SecondsUntilSecured = 0d;
        passenger.CabinActivity = PassengerCabinActivity.SeatbeltFastened;
        passenger.SeatbeltFastened = true;
        passenger.SecondsUntilActivityChange = 15d;
        _boardedCount++;
        _lastSeatedPassengers.Add(passenger);
        if (_boardedCount >= ExpectedBoardingCount)
        {
            State = BoardingRunState.Complete;
        }

        return true;
    }

    public bool SetPassengerBoardingHold(int passengerId, bool isHeld)
    {
        var passenger = _passengers.FirstOrDefault(candidate => candidate.Id == passengerId);
        if (passenger is null || passenger.MovementState != PassengerMovementState.Waiting)
        {
            return false;
        }

        return isHeld
            ? _boardingHoldPassengerIds.Add(passengerId)
            : _boardingHoldPassengerIds.Remove(passengerId);
    }

    public bool MarkPassengerNoShow(int passengerId)
    {
        var passenger = _passengers.FirstOrDefault(candidate => candidate.Id == passengerId);
        if (passenger is null ||
            passenger.MovementState != PassengerMovementState.Waiting ||
            !_noShowPassengerIds.Add(passengerId))
        {
            return false;
        }

        _boardingHoldPassengerIds.Remove(passengerId);
        SkipAlreadyProcessedPassengers();
        if (_boardedCount >= ExpectedBoardingCount)
        {
            State = BoardingRunState.Complete;
        }

        return true;
    }

    public void Tick(TimeSpan elapsed, double speedMultiplier = 1d)
    {
        _lastSeatedPassengers.Clear();
        _lastDeboardedPassengers.Clear();
        if (State is BoardingRunState.Ready or BoardingRunState.Paused or
            BoardingRunState.Complete or BoardingRunState.DeboardingComplete)
        {
            return;
        }

        var scaledSeconds = Math.Clamp(elapsed.TotalSeconds, 0d, 1d) * Math.Clamp(speedMultiplier, 0.05d, 8d);
        if (Operation == PassengerOperation.Deboarding)
        {
            TickDeboarding(scaledSeconds);
            return;
        }

        SecureOccupiedPassengers(scaledSeconds);
        MoveActivePassengers(scaledSeconds);
        if (_boardedCount >= ExpectedBoardingCount)
        {
            State = BoardingRunState.Complete;
            return;
        }

        if (_openDoors.Count == 0)
        {
            State = BoardingRunState.WaitingForDoor;
            return;
        }

        State = BoardingRunState.Boarding;
        _spawnAccumulator += scaledSeconds;
        SkipAlreadyProcessedPassengers();
        var spawnInterval = _nextPassengerIndex < _passengers.Count
            ? GetSpawnInterval(_passengers[_nextPassengerIndex]) / _openDoors.Count
            : BaseSpawnIntervalSeconds;
        var activeLimit = _openDoors.Count * 16;
        if (_nextPassengerIndex < _passengers.Count &&
            _passengers[_nextPassengerIndex].BoardingGroup != _currentBoardingGroup &&
            _spawnAccumulator >= spawnInterval &&
            _activePassengers.Count < activeLimit)
        {
            _currentBoardingGroup = _passengers[_nextPassengerIndex].BoardingGroup;
        }

        while (_spawnAccumulator >= spawnInterval &&
               _nextPassengerIndex < _passengers.Count &&
               _passengers[_nextPassengerIndex].BoardingGroup == _currentBoardingGroup &&
               _activePassengers.Count < activeLimit)
        {
            SpawnPassenger(_passengers[_nextPassengerIndex++]);
            _spawnAccumulator -= spawnInterval;
            SkipAlreadyProcessedPassengers();
            spawnInterval = _nextPassengerIndex < _passengers.Count
                ? GetSpawnInterval(_passengers[_nextPassengerIndex]) / _openDoors.Count
                : BaseSpawnIntervalSeconds;
        }
    }

    public void UpdateCabinActivities(TimeSpan elapsed, bool seatbeltSignOn, string flightPhase)
    {
        var seconds = Math.Clamp(elapsed.TotalSeconds, 0d, 5d);
        if (seconds <= 0d)
        {
            return;
        }

        foreach (var passenger in _passengers)
        {
            if (passenger.MovementState != PassengerMovementState.Seated)
            {
                passenger.CabinActivity = passenger.MovementState switch
                {
                    PassengerMovementState.Waiting => PassengerCabinActivity.AwaitingBoarding,
                    PassengerMovementState.Walking when Operation == PassengerOperation.Deboarding => PassengerCabinActivity.Deboarding,
                    PassengerMovementState.Walking => PassengerCabinActivity.WalkingToSeat,
                    PassengerMovementState.OccupyingSeat => PassengerCabinActivity.SettlingIn,
                    PassengerMovementState.Deboarded => PassengerCabinActivity.OffAircraft,
                    _ => passenger.CabinActivity
                };
                passenger.SeatbeltFastened = false;
                continue;
            }

            if (seatbeltSignOn)
            {
                passenger.SeatbeltFastened = true;
                if (passenger.CabinActivity is PassengerCabinActivity.WalkingToLavatory or
                    PassengerCabinActivity.UsingLavatory or PassengerCabinActivity.ReturningToSeat)
                {
                    passenger.CabinActivity = PassengerCabinActivity.ReturningToSeat;
                    if (!MoveActivityPassenger(passenger, passenger.Seat.X, passenger.Seat.Y, seconds * 310d))
                    {
                        continue;
                    }
                }

                passenger.Position = new CabinPoint(passenger.Seat.X, passenger.Seat.Y);
                passenger.CabinActivity = PassengerCabinActivity.SeatbeltFastened;
                passenger.SecondsUntilActivityChange = 15d;
                continue;
            }

            passenger.SeatbeltFastened = false;
            switch (passenger.CabinActivity)
            {
                case PassengerCabinActivity.WalkingToLavatory:
                {
                    var lavatoryX = _cabinSeats.Max(seat => seat.X) + 18d;
                    if (MoveActivityPassenger(passenger, lavatoryX, passenger.Seat.AisleY, seconds * 95d))
                    {
                        passenger.CabinActivity = PassengerCabinActivity.UsingLavatory;
                        passenger.SecondsUntilActivityChange = 45d + ((passenger.Id * 13) % 75);
                    }
                    continue;
                }
                case PassengerCabinActivity.UsingLavatory:
                    passenger.SecondsUntilActivityChange -= seconds;
                    if (passenger.SecondsUntilActivityChange <= 0d)
                    {
                        passenger.CabinActivity = PassengerCabinActivity.ReturningToSeat;
                    }
                    continue;
                case PassengerCabinActivity.ReturningToSeat:
                    if (MoveActivityPassenger(passenger, passenger.Seat.X, passenger.Seat.Y, seconds * 105d))
                    {
                        SelectNextSeatedActivity(passenger, flightPhase);
                    }
                    continue;
            }

            passenger.Position = new CabinPoint(passenger.Seat.X, passenger.Seat.Y);
            passenger.SecondsUntilActivityChange -= seconds;
            if (passenger.SecondsUntilActivityChange <= 0d ||
                passenger.CabinActivity is PassengerCabinActivity.SeatbeltFastened or PassengerCabinActivity.SettlingIn)
            {
                SelectNextSeatedActivity(passenger, flightPhase);
            }
        }
    }

    private static bool MoveActivityPassenger(BoardingPassenger passenger, double targetX, double targetY, double distance)
    {
        var deltaX = targetX - passenger.Position.X;
        var deltaY = targetY - passenger.Position.Y;
        var remaining = Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
        if (remaining <= distance || remaining < 0.01d)
        {
            passenger.Position = new CabinPoint(targetX, targetY);
            return true;
        }

        var ratio = distance / remaining;
        passenger.Position = new CabinPoint(
            passenger.Position.X + (deltaX * ratio),
            passenger.Position.Y + (deltaY * ratio));
        return false;
    }

    private static void SelectNextSeatedActivity(BoardingPassenger passenger, string flightPhase)
    {
        passenger.ActivitySequence++;
        var phaseSeed = string.IsNullOrWhiteSpace(flightPhase)
            ? 0
            : StringComparer.OrdinalIgnoreCase.GetHashCode(flightPhase);
        var selector = Math.Abs((passenger.Id * 31) + (passenger.ActivitySequence * 17) + phaseSeed);
        passenger.CabinActivity = (selector % 17) switch
        {
            0 when !string.Equals(flightPhase, "TAXI", StringComparison.OrdinalIgnoreCase) => PassengerCabinActivity.WalkingToLavatory,
            1 or 2 => PassengerCabinActivity.Sleeping,
            3 or 4 or 5 => PassengerCabinActivity.WatchingMovie,
            6 or 7 => PassengerCabinActivity.UsingPhone,
            8 or 9 => PassengerCabinActivity.Reading,
            10 => PassengerCabinActivity.Gaming,
            11 => PassengerCabinActivity.Working,
            _ => PassengerCabinActivity.Talking
        };
        passenger.SecondsUntilActivityChange = passenger.CabinActivity == PassengerCabinActivity.WalkingToLavatory
            ? 0d
            : 75d + (selector % 240);
    }

    private void SkipAlreadyProcessedPassengers()
    {
        while (_nextPassengerIndex < _passengers.Count &&
               (_passengers[_nextPassengerIndex].MovementState != PassengerMovementState.Waiting ||
                _boardingHoldPassengerIds.Contains(_passengers[_nextPassengerIndex].Id) ||
                _noShowPassengerIds.Contains(_passengers[_nextPassengerIndex].Id)))
        {
            _nextPassengerIndex++;
        }
    }

    private void InitializeManifest()
    {
        _passengers.Clear();
        _activePassengers.Clear();
        _occupyingPassengers.Clear();
        _lastSeatedPassengers.Clear();
        _lastDeboardedPassengers.Clear();
        _deboardingQueue.Clear();
        _boardingHoldPassengerIds.Clear();
        _noShowPassengerIds.Clear();
        _nextPassengerIndex = 0;
        _nextDeboardingPassengerIndex = 0;
        _boardedCount = 0;
        _deboardedCount = 0;
        _currentBoardingGroup = 0;
        _spawnAccumulator = 0d;
        Operation = PassengerOperation.Boarding;

        var random = new Random(777_000 + TargetPassengerCount);
        var selectedSeats = _cabinSeats
            .OrderBy(_ => random.Next())
            .Take(TargetPassengerCount)
            .Select(seat => new
            {
                Seat = seat,
                Group = GetBoardingGroup(seat),
                TieBreaker = random.Next()
            })
            .OrderBy(item => item.Group)
            .ThenBy(item => item.TieBreaker)
            .ToList();

        for (var index = 0; index < selectedSeats.Count; index++)
        {
            var item = selectedSeats[index];
            var passengerId = index + 1;
            _passengers.Add(new BoardingPassenger(
                passengerId,
                item.Seat,
                item.Group,
                CreatePassengerProfile(passengerId, item.Seat)));
        }

        _currentBoardingGroup = _passengers.Count == 0 ? 0 : _passengers[0].BoardingGroup;

        State = BoardingRunState.Ready;
    }

    private void SpawnPassenger(BoardingPassenger passenger)
    {
        var door = SelectDoor(passenger);
        var entry = GetDoorEntryPoint(door);
        passenger.Door = door;
        passenger.MovementState = PassengerMovementState.Walking;
        passenger.CabinActivity = PassengerCabinActivity.WalkingToSeat;
        passenger.SeatbeltFastened = false;
        passenger.Position = entry;
        var cabinCrossingY = _cabinSeats
            .Select(seat => seat.AisleY)
            .Distinct()
            .Average();
        passenger.Waypoints = new Queue<CabinPoint>(
        [
            new CabinPoint(entry.X, 174d),
            new CabinPoint(entry.X, cabinCrossingY),
            new CabinPoint(entry.X, passenger.Seat.AisleY),
            new CabinPoint(passenger.Seat.X, passenger.Seat.AisleY),
            new CabinPoint(passenger.Seat.X, passenger.Seat.Y)
        ]);
        _activePassengers.Add(passenger);
    }

    private void TickDeboarding(double scaledSeconds)
    {
        MoveActivePassengers(scaledSeconds);
        if (_deboardedCount >= ExpectedBoardingCount)
        {
            State = BoardingRunState.DeboardingComplete;
            return;
        }

        if (_openDoors.Count == 0)
        {
            State = BoardingRunState.WaitingForDoor;
            return;
        }

        State = BoardingRunState.Deboarding;
        _spawnAccumulator += scaledSeconds;
        var spawnInterval = _nextDeboardingPassengerIndex < _deboardingQueue.Count
            ? GetSpawnInterval(_deboardingQueue[_nextDeboardingPassengerIndex]) / _openDoors.Count
            : BaseSpawnIntervalSeconds;
        var activeLimit = _openDoors.Count * 16;
        while (_spawnAccumulator >= spawnInterval &&
               _nextDeboardingPassengerIndex < _deboardingQueue.Count &&
               _activePassengers.Count < activeLimit)
        {
            SpawnDeboardingPassenger(_deboardingQueue[_nextDeboardingPassengerIndex++]);
            _spawnAccumulator -= spawnInterval;
            spawnInterval = _nextDeboardingPassengerIndex < _deboardingQueue.Count
                ? GetSpawnInterval(_deboardingQueue[_nextDeboardingPassengerIndex]) / _openDoors.Count
                : BaseSpawnIntervalSeconds;
        }
    }

    private void SpawnDeboardingPassenger(BoardingPassenger passenger)
    {
        var door = SelectDoor(passenger);
        var exit = GetDoorEntryPoint(door);
        passenger.Door = door;
        passenger.MovementState = PassengerMovementState.Walking;
        passenger.CabinActivity = PassengerCabinActivity.Deboarding;
        passenger.SeatbeltFastened = false;
        passenger.Position = new CabinPoint(passenger.Seat.X, passenger.Seat.Y);
        passenger.Waypoints = new Queue<CabinPoint>(
        [
            new CabinPoint(passenger.Seat.X, passenger.Seat.AisleY),
            new CabinPoint(exit.X, passenger.Seat.AisleY),
            new CabinPoint(exit.X, 166d),
            exit
        ]);
        _boardedCount = Math.Max(0, _boardedCount - 1);
        _activePassengers.Add(passenger);
    }

    private void MoveActivePassengers(double elapsedSeconds)
    {
        for (var index = _activePassengers.Count - 1; index >= 0; index--)
        {
            var passenger = _activePassengers[index];
            var congestionFactor = IsAisleCongested(passenger) ? 0.42d : 1d;
            var remainingDistance = PassengerWalkingSpeed * passenger.WalkingSpeedFactor * congestionFactor * elapsedSeconds;
            while (remainingDistance > 0d && passenger.Waypoints.Count > 0)
            {
                var target = passenger.Waypoints.Peek();
                var deltaX = target.X - passenger.Position.X;
                var deltaY = target.Y - passenger.Position.Y;
                var distance = Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
                if (distance <= remainingDistance || distance < 0.001d)
                {
                    passenger.Position = target;
                    passenger.Waypoints.Dequeue();
                    remainingDistance -= distance;
                    continue;
                }

                var ratio = remainingDistance / distance;
                passenger.Position = new CabinPoint(
                    passenger.Position.X + (deltaX * ratio),
                    passenger.Position.Y + (deltaY * ratio));
                remainingDistance = 0d;
            }

            if (passenger.Waypoints.Count > 0)
            {
                continue;
            }

            if (Operation == PassengerOperation.Deboarding)
            {
                passenger.MovementState = PassengerMovementState.Deboarded;
                passenger.CabinActivity = PassengerCabinActivity.OffAircraft;
                passenger.SeatbeltFastened = false;
                _activePassengers.RemoveAt(index);
                _deboardedCount++;
                _lastDeboardedPassengers.Add(passenger);
                continue;
            }

            passenger.MovementState = PassengerMovementState.OccupyingSeat;
            passenger.CabinActivity = PassengerCabinActivity.SettlingIn;
            passenger.SecondsUntilSecured = 6d + ((passenger.Id % 5) * 1.5d);
            _activePassengers.RemoveAt(index);
            _occupyingPassengers.Add(passenger);
        }
    }

    private void SecureOccupiedPassengers(double elapsedSeconds)
    {
        for (var index = _occupyingPassengers.Count - 1; index >= 0; index--)
        {
            var passenger = _occupyingPassengers[index];
            passenger.SecondsUntilSecured -= elapsedSeconds;
            if (passenger.SecondsUntilSecured > 0d)
            {
                continue;
            }

            passenger.MovementState = PassengerMovementState.Seated;
            passenger.CabinActivity = PassengerCabinActivity.SeatbeltFastened;
            passenger.SeatbeltFastened = true;
            passenger.SecondsUntilActivityChange = 15d + (passenger.Id % 20);
            _occupyingPassengers.RemoveAt(index);
            _boardedCount++;
            _lastSeatedPassengers.Add(passenger);
        }
    }

    private BoardingDoor SelectDoor(BoardingPassenger passenger)
    {
        if (_openDoors.Count == 1)
        {
            return _openDoors.Single();
        }

        return passenger.Seat.CabinClass == PassengerCabinClass.First
            ? BoardingDoor.L1
            : BoardingDoor.L2;
    }

    private CabinPoint GetDoorEntryPoint(BoardingDoor door) => door switch
    {
        BoardingDoor.L1 => new CabinPoint(_layoutDefinition.L1DoorX, 208d),
        _ => new CabinPoint(_layoutDefinition.L2DoorX, 208d)
    };

    private int GetBoardingGroup(CabinSeat seat)
    {
        if (Layout == PassengerCabinLayout.BritishAirways777200Er)
        {
            return seat.CabinClass switch
            {
                PassengerCabinClass.Business when seat.X < 300d => 1,
                PassengerCabinClass.Business => 2,
                PassengerCabinClass.PremiumEconomy => 3,
                PassengerCabinClass.Economy => AddZoneVariation(GetEconomyZone(seat.X, 4), seat, 4, 8),
                _ => 1
            };
        }

        if (Layout == PassengerCabinLayout.BritishAirways777300)
        {
            return seat.CabinClass switch
            {
                PassengerCabinClass.First => 1,
                PassengerCabinClass.Business when seat.X < 540d => 2,
                PassengerCabinClass.Business => 3,
                PassengerCabinClass.PremiumEconomy => 4,
                PassengerCabinClass.Economy => AddZoneVariation(GetEconomyZone(seat.X, 5), seat, 5, 8),
                _ => 8
            };
        }

        return seat.CabinClass switch
        {
            PassengerCabinClass.First => 1,
            PassengerCabinClass.Business when seat.X < 450d => 2,
            PassengerCabinClass.Business => 3,
            PassengerCabinClass.Economy => AddZoneVariation(GetEconomyZone(seat.X, 4), seat, 4, 8),
            _ => 8
        };
    }

    private double GetSpawnInterval(BoardingPassenger passenger)
    {
        var passengerVariation = ((passenger.Id * 37) + ((int)Layout * 11)) % 57;
        return BaseSpawnIntervalSeconds * (0.72d + (passengerVariation / 100d));
    }

    private bool IsAisleCongested(BoardingPassenger passenger)
    {
        if (passenger.Waypoints.Count == 0)
        {
            return false;
        }

        return _activePassengers.Any(other =>
            !ReferenceEquals(other, passenger) &&
            Math.Abs(other.Position.X - passenger.Position.X) < 22d &&
            Math.Abs(other.Position.Y - passenger.Position.Y) < 7d);
    }

    private int GetEconomyZone(double seatX, int firstEconomyGroup) => Layout switch
    {
        PassengerCabinLayout.BritishAirways777200Er => seatX switch
        {
            >= 950d => firstEconomyGroup,
            >= 900d => firstEconomyGroup + 1,
            >= 840d => firstEconomyGroup + 2,
            >= 780d => firstEconomyGroup + 3,
            _ => firstEconomyGroup + 4
        },
        PassengerCabinLayout.BritishAirways777300 => seatX switch
        {
            >= 970d => firstEconomyGroup,
            >= 930d => firstEconomyGroup + 1,
            >= 890d => firstEconomyGroup + 2,
            _ => firstEconomyGroup + 3
        },
        _ => seatX switch
        {
            >= 850d => firstEconomyGroup,
            >= 800d => firstEconomyGroup + 1,
            >= 750d => firstEconomyGroup + 2,
            >= 700d => firstEconomyGroup + 3,
            _ => firstEconomyGroup + 4
        }
    };

    private static int AddZoneVariation(int baseGroup, CabinSeat seat, int minimumGroup, int maximumGroup)
    {
        var stableSeed = seat.Number.Sum(character => character * 17);
        var variation = (stableSeed % 5) switch
        {
            0 => -1,
            1 => 1,
            _ => 0
        };
        return Math.Clamp(baseGroup + variation, minimumGroup, maximumGroup);
    }

    private static PassengerProfile CreatePassengerProfile(int passengerId, CabinSeat seat)
    {
        var identityIndex = passengerId - 1;
        var firstName = FirstNames[identityIndex % FirstNames.Length];
        var lastName = LastNames[((identityIndex / FirstNames.Length) + (identityIndex * 5)) % LastNames.Length];
        var seed = 777_000 + (passengerId * 97) + seat.Number.Sum(character => character * 13);
        var random = new Random(seed);
        var tier = seat.CabinClass switch
        {
            PassengerCabinClass.First => FrequentFlyerTiers[random.Next(2, FrequentFlyerTiers.Length)],
            PassengerCabinClass.Business => FrequentFlyerTiers[random.Next(1, FrequentFlyerTiers.Length)],
            PassengerCabinClass.PremiumEconomy => FrequentFlyerTiers[random.Next(1, FrequentFlyerTiers.Length)],
            _ => FrequentFlyerTiers[random.Next(FrequentFlyerTiers.Length)]
        };
        var assistance = random.Next(18) switch
        {
            0 => "Wheelchair assistance",
            1 => "Priority boarding",
            2 => "Dietary note on booking",
            _ => "None"
        };
        var bookingNumber = Math.Abs((passengerId * 173) + seed) % 10_000;
        var emailName = $"{firstName}.{lastName}".ToLowerInvariant();
        return new PassengerProfile(
            $"{firstName} {lastName}",
            random.Next(18, 83),
            Nationalities[random.Next(Nationalities.Length)],
            TravelPurposes[random.Next(TravelPurposes.Length)],
            tier,
            random.Next(0, 3),
            assistance,
            $"FF{bookingNumber:0000}",
            $"{emailName}.p{passengerId:000}@passengers.freeflight.test");
    }

    private static readonly string[] FirstNames =
    [
        "Alex", "Amelia", "Arthur", "Ava", "Benjamin", "Charlotte", "Daniel", "Ella",
        "Ethan", "Freya", "George", "Grace", "Hannah", "Henry", "Isla", "Jack",
        "James", "Leo", "Lily", "Maya", "Noah", "Oliver", "Ruby", "Sophie"
    ];

    private static readonly string[] LastNames =
    [
        "Andersen", "Bennett", "Campbell", "Davies", "Evans", "Fischer", "Garcia", "Hansen",
        "Ivanov", "Johansson", "Khan", "Lewis", "Martin", "Nielsen", "Olsen", "Patel",
        "Roberts", "Schmidt", "Taylor", "Walker", "Wilson", "Young", "Zhang", "Moreau"
    ];

    private static readonly string[] Nationalities =
    [
        "British", "Norwegian", "Swedish", "Danish", "German", "French", "Spanish", "Italian",
        "Dutch", "Irish", "Canadian", "American", "Australian", "Japanese", "Indian", "Brazilian"
    ];

    private static readonly string[] TravelPurposes =
    [
        "Business", "Holiday", "Visiting family", "Weekend break", "Study", "Connecting journey"
    ];

    private static readonly string[] FrequentFlyerTiers =
    [
        "None", "Blue", "Bronze", "Silver", "Gold"
    ];

}
