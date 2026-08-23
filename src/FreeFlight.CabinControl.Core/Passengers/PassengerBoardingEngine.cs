namespace FreeFlight.CabinControl.Core.Passengers;

public sealed class PassengerBoardingEngine
{
    private const double PassengerWalkingSpeed = 205d;
    private const double BaseSpawnIntervalSeconds = 0.55d;
    private readonly IReadOnlyList<CabinSeat> _cabinSeats = CreateFf777PreviewSeats();
    private readonly List<BoardingPassenger> _passengers = [];
    private readonly List<BoardingPassenger> _activePassengers = [];
    private readonly List<BoardingPassenger> _lastSeatedPassengers = [];
    private readonly HashSet<BoardingDoor> _openDoors = [];
    private int _nextPassengerIndex;
    private int _boardedCount;
    private double _spawnAccumulator;

    public PassengerBoardingEngine(int targetPassengerCount = 228)
    {
        ConfigurePassengerCount(targetPassengerCount);
    }

    public int Capacity => _cabinSeats.Count;

    public int TargetPassengerCount { get; private set; }

    public int BoardedCount => _boardedCount;

    public int WalkingCount => _activePassengers.Count;

    public int RemainingCount => Math.Max(0, TargetPassengerCount - BoardedCount);

    public int WaitingCount => Math.Max(0, TargetPassengerCount - BoardedCount - WalkingCount);

    public int OpenDoorCount => _openDoors.Count;

    public double Progress => TargetPassengerCount == 0
        ? 0d
        : BoardedCount / (double)TargetPassengerCount;

    public BoardingRunState State { get; private set; } = BoardingRunState.Ready;

    public IReadOnlyList<BoardingPassenger> Passengers => _passengers;

    public IReadOnlyList<BoardingPassenger> LastSeatedPassengers => _lastSeatedPassengers;

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
            State = BoardingRunState.Boarding;
        }
        else if (State == BoardingRunState.Boarding && _openDoors.Count == 0)
        {
            State = BoardingRunState.WaitingForDoor;
        }
    }

    public void Start()
    {
        if (State == BoardingRunState.Complete)
        {
            return;
        }

        State = _openDoors.Count > 0
            ? BoardingRunState.Boarding
            : BoardingRunState.WaitingForDoor;
    }

    public void Pause()
    {
        if (State is BoardingRunState.Boarding or BoardingRunState.WaitingForDoor)
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
        if (State is BoardingRunState.Ready or BoardingRunState.Paused or BoardingRunState.Complete)
        {
            return;
        }

        var scaledSeconds = Math.Clamp(elapsed.TotalSeconds, 0d, 1d) * Math.Clamp(speedMultiplier, 0.25d, 8d);
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
        while (_spawnAccumulator >= spawnInterval &&
               _nextPassengerIndex < _passengers.Count &&
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
        _lastSeatedPassengers.Clear();
        _nextPassengerIndex = 0;
        _boardedCount = 0;
        _spawnAccumulator = 0d;

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
            _passengers.Add(new BoardingPassenger(index + 1, item.Seat, item.Group));
        }

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

            passenger.MovementState = PassengerMovementState.Seated;
            _activePassengers.RemoveAt(index);
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

        return passenger.Seat.CabinClass == PassengerCabinClass.First || passenger.Seat.X < 545d
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
        PassengerCabinClass.Economy when seat.X > 800d => 4,
        PassengerCabinClass.Economy when seat.X > 710d => 5,
        _ => 6
    };

    private static IReadOnlyList<CabinSeat> CreateFf777PreviewSeats()
    {
        var seats = new List<CabinSeat>(256);
        AddSeatBlock(
            seats,
            PassengerCabinClass.First,
            firstRow: 1,
            rowCount: 4,
            startX: 205d,
            endX: 392d,
            letters: ["A", "K"],
            yPositions: [77d, 143d]);
        AddSeatBlock(
            seats,
            PassengerCabinClass.Business,
            firstRow: 5,
            rowCount: 14,
            startX: 445d,
            endX: 595d,
            letters: ["A", "D", "G", "K"],
            yPositions: [69d, 92d, 130d, 153d]);
        AddSeatBlock(
            seats,
            PassengerCabinClass.Economy,
            firstRow: 19,
            rowCount: 24,
            startX: 630d,
            endX: 890d,
            letters: ["A", "B", "C", "D", "F", "G", "H", "J"],
            yPositions: [63d, 76d, 89d, 102d, 126d, 139d, 152d, 165d]);
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
                var aisleY = y < 114d ? 108d : 118d;
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
