namespace FreeFlight.CabinControl.Core.Passengers;

public enum BoardingDoor
{
    L1,
    L2
}

public enum BoardingRunState
{
    Ready,
    Boarding,
    Paused,
    WaitingForDoor,
    Complete
}

public enum PassengerCabinClass
{
    First,
    Business,
    Economy
}

public enum PassengerMovementState
{
    Waiting,
    Walking,
    Seated
}

public readonly record struct CabinPoint(double X, double Y);

public sealed record CabinSeat(
    string Number,
    PassengerCabinClass CabinClass,
    double X,
    double Y,
    double AisleY);

public sealed class BoardingPassenger
{
    internal BoardingPassenger(int id, CabinSeat seat, int boardingGroup)
    {
        Id = id;
        Seat = seat;
        BoardingGroup = boardingGroup;
    }

    public int Id { get; }

    public CabinSeat Seat { get; }

    public int BoardingGroup { get; }

    public BoardingDoor? Door { get; internal set; }

    public PassengerMovementState MovementState { get; internal set; }

    public CabinPoint Position { get; internal set; }

    internal Queue<CabinPoint> Waypoints { get; set; } = new();
}
