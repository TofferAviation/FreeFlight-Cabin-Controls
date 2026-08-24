namespace FreeFlight.CabinControl.Core.Passengers;

public sealed class PassengerBoardingEngine
{
    private const double PassengerWalkingSpeed = 205d;
    private const double BaseSpawnIntervalSeconds = 0.55d;
    private readonly IReadOnlyList<CabinSeat> _cabinSeats = CreateFf777PreviewSeats();
    private readonly List<BoardingPassenger> _passengers = [];
    private readonly List<BoardingPassenger> _activePassengers = [];
    private readonly List<BoardingPassenger> _occupyingPassengers = [];
    private readonly List<BoardingPassenger> _lastSeatedPassengers = [];
    private readonly List<BoardingPassenger> _lastDeboardedPassengers = [];
    private readonly List<BoardingPassenger> _deboardingQueue = [];
    private readonly HashSet<BoardingDoor> _openDoors = [];
    private int _nextPassengerIndex;
    private int _nextDeboardingPassengerIndex;
    private int _boardedCount;
    private int _deboardedCount;
    private int _currentBoardingGroup;
    private double _spawnAccumulator;

    public PassengerBoardingEngine(int targetPassengerCount = 228)
    {
        ConfigurePassengerCount(targetPassengerCount);
    }

    public int Capacity => _cabinSeats.Count;

    public int TargetPassengerCount { get; private set; }

    public int BoardedCount => _boardedCount;

    public int DeboardedCount => _deboardedCount;

    public int OnBoardCount => Math.Max(0, TargetPassengerCount - DeboardedCount);

    public int WalkingCount => _activePassengers.Count;

    public int OccupyingCount => _occupyingPassengers.Count;

    public int InCabinCount => WalkingCount + OccupyingCount;

    public int RemainingCount => Math.Max(0, TargetPassengerCount - BoardedCount);

    public int WaitingCount => Math.Max(0, TargetPassengerCount - BoardedCount - InCabinCount);

    public int OpenDoorCount => _openDoors.Count;

    public int CurrentBoardingGroup => _currentBoardingGroup;

    public int WaitingInCurrentBoardingGroup => _passengers.Count(passenger =>
        passenger.BoardingGroup == CurrentBoardingGroup &&
        passenger.MovementState == PassengerMovementState.Waiting);

    public IReadOnlyList<int> BoardingGroups => _passengers
        .Select(passenger => passenger.BoardingGroup)
        .Distinct()
        .Order()
        .ToArray();

    public double Progress => TargetPassengerCount == 0
        ? 0d
        : BoardedCount / (double)TargetPassengerCount;

    public double DeboardingProgress => TargetPassengerCount == 0
        ? 0d
        : DeboardedCount / (double)TargetPassengerCount;

    public BoardingRunState State { get; private set; } = BoardingRunState.Ready;

    public PassengerOperation Operation { get; private set; } = PassengerOperation.Boarding;

    public IReadOnlyList<BoardingPassenger> Passengers => _passengers;

    public IReadOnlyList<BoardingPassenger> LastSeatedPassengers => _lastSeatedPassengers;

    public IReadOnlyList<BoardingPassenger> LastDeboardedPassengers => _lastDeboardedPassengers;

    public IReadOnlyCollection<BoardingDoor> OpenDoors => _openDoors;

    public bool IsDoorOpen(BoardingDoor door) => _openDoors.Contains(door);

    public void ConfigurePassengerCount(int passengerCount)
    {
        TargetPassengerCount = Math.Clamp(passengerCount, 1, Capacity);
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
        _deboardingQueue.AddRange(_passengers
            .OrderBy(passenger => passenger.Seat.CabinClass == PassengerCabinClass.First ? 0 : 1)
            .ThenBy(passenger => Math.Abs(
                passenger.Seat.X - GetDoorEntryPoint(
                    passenger.Seat.CabinClass == PassengerCabinClass.First
                        ? BoardingDoor.L1
                        : BoardingDoor.L2).X))
            .ThenBy(passenger => passenger.BoardingGroup));
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
        if (_boardedCount >= TargetPassengerCount)
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
        var spawnInterval = BaseSpawnIntervalSeconds / _openDoors.Count;
        var activeLimit = _openDoors.Count * 8;
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
        passenger.Position = entry;
        passenger.Waypoints = new Queue<CabinPoint>(
        [
            new CabinPoint(entry.X, 166d),
            new CabinPoint(entry.X, passenger.Seat.AisleY),
            new CabinPoint(passenger.Seat.X, passenger.Seat.AisleY),
            new CabinPoint(passenger.Seat.X, passenger.Seat.Y)
        ]);
        _activePassengers.Add(passenger);
    }

    private void TickDeboarding(double scaledSeconds)
    {
        MoveActivePassengers(scaledSeconds);
        if (_deboardedCount >= TargetPassengerCount)
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
        var spawnInterval = BaseSpawnIntervalSeconds / _openDoors.Count;
        var activeLimit = _openDoors.Count * 8;
        while (_spawnAccumulator >= spawnInterval &&
               _nextDeboardingPassengerIndex < _deboardingQueue.Count &&
               _activePassengers.Count < activeLimit)
        {
            SpawnDeboardingPassenger(_deboardingQueue[_nextDeboardingPassengerIndex++]);
            _spawnAccumulator -= spawnInterval;
        }
    }

    private void SpawnDeboardingPassenger(BoardingPassenger passenger)
    {
        var door = SelectDoor(passenger);
        var exit = GetDoorEntryPoint(door);
        passenger.Door = door;
        passenger.MovementState = PassengerMovementState.Walking;
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
            var remainingDistance = PassengerWalkingSpeed * elapsedSeconds;
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
                _activePassengers.RemoveAt(index);
                _deboardedCount++;
                _lastDeboardedPassengers.Add(passenger);
                continue;
            }

            passenger.MovementState = PassengerMovementState.OccupyingSeat;
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

    private static CabinPoint GetDoorEntryPoint(BoardingDoor door) => door switch
    {
        BoardingDoor.L1 => new CabinPoint(183d, 208d),
        _ => new CabinPoint(426d, 208d)
    };

    private static int GetBoardingGroup(CabinSeat seat) => seat.CabinClass switch
    {
        PassengerCabinClass.First => 1,
        PassengerCabinClass.Business when seat.X < 520d => 2,
        PassengerCabinClass.Business => 3,
        PassengerCabinClass.Economy when seat.X >= 850d => 4,
        PassengerCabinClass.Economy when seat.X >= 800d => 5,
        PassengerCabinClass.Economy when seat.X >= 750d => 6,
        PassengerCabinClass.Economy when seat.X >= 700d => 7,
        _ => 8
    };

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
        return new PassengerProfile(
            $"{firstName} {lastName}",
            random.Next(18, 83),
            Nationalities[random.Next(Nationalities.Length)],
            TravelPurposes[random.Next(TravelPurposes.Length)],
            tier,
            random.Next(0, 3),
            assistance,
            $"FF{bookingNumber:0000}");
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

    private static IReadOnlyList<CabinSeat> CreateFf777PreviewSeats()
    {
        var seats = new List<CabinSeat>(256);
        AddSeatBlock(
            seats,
            PassengerCabinClass.First,
            firstRow: 1,
            rowCount: 4,
            startX: 304d,
            endX: 403d,
            letters: ["A", "D", "K"],
            yPositions: [38d, 73d, 108d]);
        AddSeatBlock(
            seats,
            PassengerCabinClass.Business,
            firstRow: 5,
            rowCount: 5,
            startX: 447d,
            endX: 565d,
            letters: ["A", "D", "K"],
            yPositions: [38d, 73d, 108d]);
        AddSeatBlock(
            seats,
            PassengerCabinClass.Economy,
            firstRow: 10,
            rowCount: 24,
            startX: 630d,
            endX: 890d,
            letters: ["A", "B", "C", "D", "F", "G", "H", "J"],
            yPositions: [30d, 43d, 56d, 69d, 84d, 97d, 110d, 123d]);
        return seats;
    }

    private static void AddSeatBlock(
        ICollection<CabinSeat> seats,
        PassengerCabinClass cabinClass,
        int firstRow,
        int rowCount,
        double startX,
        double endX,
        IReadOnlyList<string> letters,
        IReadOnlyList<double> yPositions)
    {
        var rowSpacing = rowCount == 1 ? 0d : (endX - startX) / (rowCount - 1);
        for (var rowIndex = 0; rowIndex < rowCount; rowIndex++)
        {
            var rowNumber = firstRow + rowIndex;
            var x = startX + (rowSpacing * rowIndex);
            for (var seatIndex = 0; seatIndex < letters.Count; seatIndex++)
            {
                var y = yPositions[seatIndex];
                var aisleY = cabinClass switch
                {
                    PassengerCabinClass.Economy when seatIndex <= 3 => 63d,
                    PassengerCabinClass.Economy => 91d,
                    _ when seatIndex <= 1 => 56d,
                    _ => 91d
                };
                seats.Add(new CabinSeat(
                    $"{rowNumber}{letters[seatIndex]}",
                    cabinClass,
                    x,
                    y,
                    aisleY));
            }
        }
    }
}
