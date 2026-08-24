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
    Deboarding,
    Paused,
    WaitingForDoor,
    Complete,
    DeboardingComplete
}

public enum PassengerOperation
{
    Boarding,
    Deboarding
}

public enum PassengerCabinClass
{
    First,
    Business,
    PremiumEconomy,
    Economy
}

public enum PassengerCabinLayout
{
    FlightFactor777V2,
    BritishAirways777200Er,
    BritishAirways777300
}

public enum PassengerMovementState
{
    Waiting,
    Walking,
    OccupyingSeat,
    Seated,
    Deboarded
}

public readonly record struct CabinPoint(double X, double Y);

public sealed record CabinSeat(
    string Number,
    PassengerCabinClass CabinClass,
    double X,
    double Y,
    double AisleY);

public sealed record PassengerProfile(
    string FullName,
    int Age,
    string Nationality,
    string TravelPurpose,
    string FrequentFlyerTier,
    int CheckedBags,
    string Assistance,
    string BookingReference);

public sealed class BoardingPassenger
{
    internal BoardingPassenger(int id, CabinSeat seat, int boardingGroup, PassengerProfile profile)
    {
        Id = id;
        Seat = seat;
        BoardingGroup = boardingGroup;
        Profile = profile;
        WalkingSpeedFactor = 0.78d + (((id * 17) % 39) / 100d);
    }

    public int Id { get; }

    public CabinSeat Seat { get; }

    public int BoardingGroup { get; }

    public PassengerProfile Profile { get; }

    internal double WalkingSpeedFactor { get; }

    public BoardingDoor? Door { get; internal set; }

    public PassengerMovementState MovementState { get; internal set; }

    public CabinPoint Position { get; internal set; }

    internal Queue<CabinPoint> Waypoints { get; set; } = new();

    internal double SecondsUntilSecured { get; set; }
}
