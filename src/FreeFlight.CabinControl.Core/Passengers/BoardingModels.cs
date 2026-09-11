namespace FreeFlight.CabinControl.Core.Passengers;

public enum BoardingDoor
{
    L1,
    L2,
    L3,
    L4,
    L5,
    R1,
    R2,
    R3,
    R4,
    R5,
    OverwingLeft,
    OverwingRight
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
    BritishAirwaysA320Neo,
    BritishAirwaysA319,
    BritishAirwaysA321,
    BritishAirwaysA321Neo220M,
    BritishAirwaysA350,
    BritishAirways777200Lgw,
    BritishAirways777200First,
    BritishAirways7878ClubSuite,
    BritishAirways7879,
    BritishAirways7879Alternate,
    BritishAirways78710,
    BritishAirwaysEmbraer190
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
    QueuedForLavatory,
    UsingLavatory,
    ReturningToSeat,
    WaitingForCabinService,
    ReceivingMeal,
    EatingMeal,
    ReceivingDrink,
    Drinking,
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

public sealed record CabinDoorDefinition(
    BoardingDoor Door,
    string Label,
    double X,
    bool IsBoardingDoor,
    bool IsEmergencyExit = false)
{
    public string TypeLabel => IsEmergencyExit ? "Emergency exit" : IsBoardingDoor ? "Passenger door" : "Service door";
}

public sealed record CabinMenuItem(
    string Id,
    string Name,
    string Category,
    decimal PriceGbp,
    string Description);

public sealed record CabinPurchase(
    int PassengerId,
    string SeatNumber,
    string ItemId,
    string ItemName,
    decimal PriceGbp,
    DateTimeOffset Timestamp);

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

    public decimal OnboardSpendGbp { get; internal set; }

    public string LastPurchase { get; internal set; } = "No purchases";

    internal int PurchaseSequence { get; set; }

    internal int LavatoryIndex { get; set; } = -1;
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
