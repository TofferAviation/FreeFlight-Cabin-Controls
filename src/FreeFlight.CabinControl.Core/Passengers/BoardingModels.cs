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
    BritishAirways777300,
    BritishAirwaysA320200,
    BritishAirwaysA320Neo
}

public enum PassengerMovementState
{
    Waiting,
    Walking,
    OccupyingSeat,
    Seated,
    Deboarded
}

public enum PassengerCabinActivity
{
    AwaitingBoarding,
    WalkingToSeat,
    SettlingIn,
    SelectingWelcomeDrink,
    RespondingToSeatbeltSign,
    SeatbeltFastened,
    WatchingMovie,
    Gaming,
    UsingPhone,
    Sleeping,
    Reading,
    Working,
    Talking,
    WalkingToLavatory,
    UsingLavatory,
    ReturningToSeat,
    Deboarding,
    OffAircraft
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
    string BookingReference,
    string Email);

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

    internal Queue<CabinPoint> ActivityWaypoints { get; set; } = new();

    internal double SecondsUntilSecured { get; set; }

    public PassengerCabinActivity CabinActivity { get; internal set; } = PassengerCabinActivity.AwaitingBoarding;

    public bool SeatbeltFastened { get; internal set; }

    internal double SecondsUntilActivityChange { get; set; }

    internal double SecondsUntilSeatbeltResponse { get; set; }

    internal int ActivitySequence { get; set; }
}

public sealed record BoardingPassengerSession(
    int PassengerId,
    BoardingDoor? Door,
    PassengerMovementState MovementState,
    CabinPoint Position,
    PassengerCabinActivity CabinActivity,
    bool SeatbeltFastened,
    bool IsBoardingHeld,
    bool IsNoShow);

public sealed record PassengerBoardingSession(
    PassengerCabinLayout Layout,
    int TargetPassengerCount,
    BoardingRunState State,
    PassengerOperation Operation,
    int CurrentBoardingGroup,
    IReadOnlyList<BoardingDoor> OpenDoors,
    IReadOnlyList<BoardingPassengerSession> Passengers);
