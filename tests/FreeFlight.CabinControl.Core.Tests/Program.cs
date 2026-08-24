using FreeFlight.CabinControl.Core.Configuration;
using FreeFlight.CabinControl.Core.Content;
using FreeFlight.CabinControl.Core.Persistence;
using FreeFlight.CabinControl.Core.Passengers;

var tests = new (string Name, Func<Task> Run)[]
{
    ("Settings round-trip", SettingsRoundTripAsync),
    ("Valid airline pack", ValidAirlinePackAsync),
    ("Traversal asset rejected", TraversalAssetRejectedAsync),
    ("Executable asset rejected", ExecutableAssetRejectedAsync),
    ("L2-only passenger routing", L2OnlyPassengerRoutingAsync),
    ("Boarding tickets select L1 and L2", BoardingTicketsSelectDoorsAsync),
    ("Two-door boarding increases passenger flow", TwoDoorBoardingIncreasesFlowAsync),
    ("Passenger seats select two aisle lanes", PassengerSeatsSelectTwoAislesAsync),
    ("Boarding waits for an open door", BoardingWaitsForDoorAsync),
    ("Passenger seat centres match the cabin layout", PassengerSeatCentresMatchLayoutAsync),
    ("Passenger seat occupation becomes secured", PassengerSeatOccupationBecomesSecuredAsync),
    ("Passenger boarding completes", PassengerBoardingCompletesAsync),
    ("Passenger profiles are complete and unique", PassengerProfilesAreCompleteAndUniqueAsync),
    ("Boarding groups run in numeric order", BoardingGroupsRunInNumericOrderAsync),
    ("Passenger deboarding completes", PassengerDeboardingCompletesAsync)
};

var failures = new List<string>();
foreach (var test in tests)
{
    try
    {
        await test.Run();
        Console.WriteLine($"PASS  {test.Name}");
    }
    catch (Exception exception)
    {
        failures.Add($"{test.Name}: {exception.Message}");
        Console.WriteLine($"FAIL  {test.Name}: {exception.Message}");
    }
}

Console.WriteLine();
Console.WriteLine($"{tests.Length - failures.Count}/{tests.Length} checks passed.");
return failures.Count == 0 ? 0 : 1;

static async Task SettingsRoundTripAsync()
{
    var directory = Path.Combine(Path.GetTempPath(), $"freeflight-tests-{Guid.NewGuid():N}");
    var path = Path.Combine(directory, "settings.json");
    var store = new JsonSettingsStore(path);

    try
    {
        var expected = new AppSettings
        {
            UserDisplayName = "Test User",
            MasterVolume = 63,
            SeatbackDisplaysEnabled = false,
            CabinLightingMode = "Night",
            CabinTargetTemperatureC = 21.5,
            AudioOutputDeviceId = "test-endpoint",
            AudioOutputDeviceName = "Test speakers",
            ActiveAirlinePackId = "test.airline",
            ActiveAirlineId = "custom.tst",
            PassengerPreviewBookedCount = 196,
            PassengerPreviewSpeed = 4d,
            SimBriefPilotId = "123456",
            SimBriefAutoSync = true,
            CustomAirlineProfiles =
            [
                new CustomAirlineProfileSettings
                {
                    Id = "custom.tst",
                    Name = "Test Virtual",
                    Icao = "TST",
                    SoundPackName = "Test pack"
                }
            ]
        };

        await store.SaveAsync(expected);
        var actual = await store.LoadAsync();

        AssertEqual("Test User", actual.UserDisplayName, "Display name was not persisted.");
        AssertEqual(63, actual.MasterVolume, "Master volume was not persisted.");
        AssertEqual(false, actual.SeatbackDisplaysEnabled, "Display state was not persisted.");
        AssertEqual("Night", actual.CabinLightingMode, "Cabin lighting mode was not persisted.");
        AssertEqual(21.5, actual.CabinTargetTemperatureC, "Cabin temperature was not persisted.");
        AssertEqual("test-endpoint", actual.AudioOutputDeviceId, "Audio endpoint id was not persisted.");
        AssertEqual("test.airline", actual.ActiveAirlinePackId, "Airline pack id was not persisted.");
        AssertEqual("custom.tst", actual.ActiveAirlineId, "Active airline id was not persisted.");
        AssertEqual(196, actual.PassengerPreviewBookedCount, "Preview passenger count was not persisted.");
        AssertEqual(4d, actual.PassengerPreviewSpeed, "Preview boarding speed was not persisted.");
        AssertEqual("123456", actual.SimBriefPilotId, "SimBrief Pilot ID was not persisted.");
        AssertEqual(true, actual.SimBriefAutoSync, "SimBrief auto-sync preference was not persisted.");
        AssertEqual("Test Virtual", actual.CustomAirlineProfiles.Single().Name, "Custom airline was not persisted.");
    }
    finally
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }
    }
}

static Task ValidAirlinePackAsync()
{
    var manifest = CreateManifest([
        new AirlinePackAsset { Type = "announcement", Path = "audio/boarding.ogg", Licence = "Created by pack author" }
    ]);
    var result = new AirlinePackValidator().Validate(manifest, Path.Combine(Path.GetTempPath(), "pack"));
    Assert(result.IsValid, string.Join(Environment.NewLine, result.Errors));
    return Task.CompletedTask;
}

static Task TraversalAssetRejectedAsync()
{
    var manifest = CreateManifest([
        new AirlinePackAsset { Type = "video", Path = "../outside.mp4", Licence = "Test" }
    ]);
    var result = new AirlinePackValidator().Validate(manifest, Path.Combine(Path.GetTempPath(), "pack"));
    Assert(!result.IsValid, "A path outside the pack directory was accepted.");
    return Task.CompletedTask;
}

static Task ExecutableAssetRejectedAsync()
{
    var manifest = CreateManifest([
        new AirlinePackAsset { Type = "unknown", Path = "payload.exe", Licence = "Test" }
    ]);
    var result = new AirlinePackValidator().Validate(manifest, Path.Combine(Path.GetTempPath(), "pack"));
    Assert(!result.IsValid, "An executable asset was accepted.");
    return Task.CompletedTask;
}

static Task L2OnlyPassengerRoutingAsync()
{
    var engine = new PassengerBoardingEngine(40);
    engine.SetDoorOpen(BoardingDoor.L2, true);
    engine.Start();
    Advance(engine, seconds: 8d, speed: 4d);
    var visiblePassengers = engine.Passengers
        .Where(passenger => passenger.MovementState != PassengerMovementState.Waiting)
        .ToArray();
    Assert(visiblePassengers.Length > 0, "No passengers entered through the open L2 door.");
    Assert(visiblePassengers.All(passenger => passenger.Door == BoardingDoor.L2),
        "A passenger used a door other than L2 while only L2 was open.");
    return Task.CompletedTask;
}

static Task BoardingWaitsForDoorAsync()
{
    var engine = new PassengerBoardingEngine(20);
    engine.Start();
    Advance(engine, seconds: 2d, speed: 4d);
    AssertEqual(BoardingRunState.WaitingForDoor, engine.State, "Boarding did not wait with every door closed.");
    AssertEqual(0, engine.WalkingCount, "A passenger entered while every door was closed.");

    engine.SetDoorOpen(BoardingDoor.L1, true);
    Advance(engine, seconds: 3d, speed: 4d);
    Assert(engine.Passengers.Any(passenger => passenger.Door == BoardingDoor.L1),
        "Boarding did not resume through L1 when that door opened.");
    return Task.CompletedTask;
}

static Task BoardingTicketsSelectDoorsAsync()
{
    var engine = new PassengerBoardingEngine(int.MaxValue);
    engine.SetDoorOpen(BoardingDoor.L1, true);
    engine.SetDoorOpen(BoardingDoor.L2, true);
    engine.Start();
    Advance(engine, seconds: 30d, speed: 8d);

    AssertEqual(BoardingRunState.Complete, engine.State, "Two-door boarding did not complete.");
    Assert(engine.Passengers
            .Where(passenger => passenger.Seat.CabinClass == PassengerCabinClass.First)
            .All(passenger => passenger.Door == BoardingDoor.L1),
        "A First passenger did not use L1 while both doors were open.");
    Assert(engine.Passengers
            .Where(passenger => passenger.Seat.CabinClass is PassengerCabinClass.Business or PassengerCabinClass.Economy)
            .All(passenger => passenger.Door == BoardingDoor.L2),
        "A Business or Economy passenger did not use L2 while both doors were open.");
    return Task.CompletedTask;
}

static Task PassengerSeatsSelectTwoAislesAsync()
{
    var engine = new PassengerBoardingEngine(int.MaxValue);
    AssertAisles(engine, PassengerCabinClass.First, 56d, 91d);
    AssertAisles(engine, PassengerCabinClass.Business, 56d, 91d);
    AssertAisles(engine, PassengerCabinClass.Economy, 63d, 91d);
    return Task.CompletedTask;
}

static Task TwoDoorBoardingIncreasesFlowAsync()
{
    var singleDoorEngine = new PassengerBoardingEngine(120);
    singleDoorEngine.SetDoorOpen(BoardingDoor.L2, true);
    singleDoorEngine.Start();

    var twoDoorEngine = new PassengerBoardingEngine(120);
    twoDoorEngine.SetDoorOpen(BoardingDoor.L1, true);
    twoDoorEngine.SetDoorOpen(BoardingDoor.L2, true);
    twoDoorEngine.Start();

    Advance(singleDoorEngine, seconds: 5d, speed: 2d);
    Advance(twoDoorEngine, seconds: 5d, speed: 2d);
    var singleDoorEntered = singleDoorEngine.BoardedCount + singleDoorEngine.InCabinCount;
    var twoDoorEntered = twoDoorEngine.BoardedCount + twoDoorEngine.InCabinCount;
    Assert(twoDoorEntered > singleDoorEntered,
        $"Two open doors did not increase passenger flow. Single: {singleDoorEntered}; two doors: {twoDoorEntered}.");
    return Task.CompletedTask;
}

static void AssertAisles(
    PassengerBoardingEngine engine,
    PassengerCabinClass cabinClass,
    double upperAisleY,
    double lowerAisleY)
{
    var aisleCoordinates = engine.Passengers
        .Where(passenger => passenger.Seat.CabinClass == cabinClass)
        .Select(passenger => passenger.Seat.AisleY)
        .ToHashSet();
    Assert(aisleCoordinates.SetEquals([upperAisleY, lowerAisleY]),
        $"{cabinClass} seats did not resolve to the expected two aisle lanes.");
}

static Task PassengerBoardingCompletesAsync()
{
    var engine = new PassengerBoardingEngine(12);
    engine.SetDoorOpen(BoardingDoor.L2, true);
    engine.Start();
    Advance(engine, seconds: 30d, speed: 4d);
    AssertEqual(BoardingRunState.Complete, engine.State, "The preview boarding run did not complete.");
    AssertEqual(12, engine.BoardedCount, "The boarded count did not match the manifest.");
    AssertEqual(0, engine.RemainingCount, "Completed boarding still reported remaining passengers.");
    return Task.CompletedTask;
}

static Task PassengerSeatCentresMatchLayoutAsync()
{
    var engine = new PassengerBoardingEngine(int.MaxValue);
    AssertEqual(219, engine.Capacity, "The visual cabin capacity did not match its seat centres.");
    AssertSeatCentre(engine, "1A", 304d, 38d);
    AssertSeatCentre(engine, "4K", 403d, 108d);
    AssertSeatCentre(engine, "5A", 447d, 38d);
    AssertSeatCentre(engine, "9K", 565d, 108d);
    AssertSeatCentre(engine, "10A", 630d, 30d);
    AssertSeatCentre(engine, "33J", 890d, 123d);
    return Task.CompletedTask;
}

static Task PassengerSeatOccupationBecomesSecuredAsync()
{
    var engine = new PassengerBoardingEngine(1);
    engine.SetDoorOpen(BoardingDoor.L2, true);
    engine.Start();
    for (var index = 0; index < 30 && engine.OccupyingCount == 0; index++)
    {
        engine.Tick(TimeSpan.FromSeconds(0.1d), 4d);
    }

    var passenger = engine.Passengers.Single();
    AssertEqual(PassengerMovementState.OccupyingSeat, passenger.MovementState,
        "The passenger did not enter the orange seat-occupation state.");
    AssertNear(passenger.Seat.X, passenger.Position.X, "The passenger was not centred horizontally in the seat.");
    AssertNear(passenger.Seat.Y, passenger.Position.Y, "The passenger was not centred vertically in the seat.");
    AssertEqual(0, engine.BoardedCount, "The passenger was counted as secured before fastening the seat belt.");

    Advance(engine, seconds: 4d, speed: 4d);
    AssertEqual(PassengerMovementState.Seated, passenger.MovementState,
        "The passenger did not change from orange occupied to green secured.");
    AssertEqual(1, engine.BoardedCount, "The secured passenger was not included in the seated count.");
    return Task.CompletedTask;
}

static Task PassengerProfilesAreCompleteAndUniqueAsync()
{
    var firstEngine = new PassengerBoardingEngine(80);
    var secondEngine = new PassengerBoardingEngine(80);
    Assert(firstEngine.Passengers.All(passenger =>
            !string.IsNullOrWhiteSpace(passenger.Profile.FullName) &&
            passenger.Profile.Age is >= 18 and <= 82 &&
            !string.IsNullOrWhiteSpace(passenger.Profile.Nationality) &&
            !string.IsNullOrWhiteSpace(passenger.Profile.BookingReference)),
        "One or more passenger profiles were incomplete.");
    AssertEqual(80, firstEngine.Passengers.Select(passenger => passenger.Profile.BookingReference).Distinct().Count(),
        "Booking references were not unique within the manifest.");
    Assert(firstEngine.Passengers.Zip(secondEngine.Passengers).All(pair =>
            pair.First.Profile == pair.Second.Profile && pair.First.Seat == pair.Second.Seat),
        "The same preview load did not generate a stable deterministic manifest.");
    return Task.CompletedTask;
}

static Task PassengerDeboardingCompletesAsync()
{
    var engine = new PassengerBoardingEngine(36);
    engine.SetDoorOpen(BoardingDoor.L1, true);
    engine.SetDoorOpen(BoardingDoor.L2, true);
    engine.Start();
    Advance(engine, seconds: 30d, speed: 8d);
    AssertEqual(BoardingRunState.Complete, engine.State, "Boarding did not complete before the deboarding test.");

    engine.StartDeboarding();
    AssertEqual(PassengerOperation.Deboarding, engine.Operation, "The passenger operation did not switch to deboarding.");
    Advance(engine, seconds: 30d, speed: 8d);

    AssertEqual(BoardingRunState.DeboardingComplete, engine.State, "The deboarding run did not complete.");
    AssertEqual(36, engine.DeboardedCount, "The deboarded count did not match the manifest.");
    AssertEqual(0, engine.BoardedCount, "Passengers remained counted as seated after deboarding.");
    AssertEqual(0, engine.OnBoardCount, "Passengers remained onboard after deboarding.");
    Assert(engine.Passengers.All(passenger => passenger.MovementState == PassengerMovementState.Deboarded),
        "One or more passengers did not reach the deboarded state.");
    Assert(engine.Passengers
            .Where(passenger => passenger.Seat.CabinClass == PassengerCabinClass.First)
            .All(passenger => passenger.Door == BoardingDoor.L1),
        "A First passenger did not deboard through L1 with both doors open.");
    Assert(engine.Passengers
            .Where(passenger => passenger.Seat.CabinClass is PassengerCabinClass.Business or PassengerCabinClass.Economy)
            .All(passenger => passenger.Door == BoardingDoor.L2),
        "A Business or Economy passenger did not deboard through L2 with both doors open.");
    return Task.CompletedTask;
}

static Task BoardingGroupsRunInNumericOrderAsync()
{
    var engine = new PassengerBoardingEngine(int.MaxValue);
    Assert(engine.BoardingGroups.SequenceEqual(Enumerable.Range(1, 8)),
        $"Expected boarding groups 1–8, got {string.Join(", ", engine.BoardingGroups)}.");
    AssertEqual(1, engine.CurrentBoardingGroup, "The first boarding call was not Group 1.");

    engine.SetDoorOpen(BoardingDoor.L1, true);
    engine.SetDoorOpen(BoardingDoor.L2, true);
    engine.Start();
    var previousGroup = engine.CurrentBoardingGroup;
    for (var index = 0; index < 400 && engine.State != BoardingRunState.Complete; index++)
    {
        engine.Tick(TimeSpan.FromSeconds(0.1d), 8d);
        Assert(engine.CurrentBoardingGroup >= previousGroup,
            "The active boarding group moved backwards.");
        previousGroup = engine.CurrentBoardingGroup;

        var firstWaitingGroup = engine.Passengers
            .Where(passenger => passenger.MovementState == PassengerMovementState.Waiting)
            .Select(passenger => passenger.BoardingGroup)
            .DefaultIfEmpty(int.MaxValue)
            .Min();
        var latestStartedGroup = engine.Passengers
            .Where(passenger => passenger.MovementState != PassengerMovementState.Waiting)
            .Select(passenger => passenger.BoardingGroup)
            .DefaultIfEmpty(0)
            .Max();
        Assert(latestStartedGroup <= firstWaitingGroup,
            $"Group {latestStartedGroup} started while Group {firstWaitingGroup} was still waiting.");
    }

    AssertEqual(BoardingRunState.Complete, engine.State, "Ordered group boarding did not complete.");
    AssertEqual(8, engine.CurrentBoardingGroup, "The final boarding call was not Group 8.");
    return Task.CompletedTask;
}

static void AssertSeatCentre(PassengerBoardingEngine engine, string seatNumber, double x, double y)
{
    var seat = engine.Passengers.Single(passenger => passenger.Seat.Number == seatNumber).Seat;
    AssertNear(x, seat.X, $"Seat {seatNumber} has the wrong horizontal centre.");
    AssertNear(y, seat.Y, $"Seat {seatNumber} has the wrong vertical centre.");
}

static void Advance(PassengerBoardingEngine engine, double seconds, double speed)
{
    var tickCount = (int)Math.Ceiling(seconds / 0.1d);
    for (var index = 0; index < tickCount; index++)
    {
        engine.Tick(TimeSpan.FromSeconds(0.1d), speed);
    }
}

static AirlinePackManifest CreateManifest(IReadOnlyList<AirlinePackAsset> assets) => new()
{
    SchemaVersion = AirlinePackManifest.CurrentSchemaVersion,
    Id = "test.airline",
    Version = "1.0.0",
    DisplayName = "Test Airline",
    Author = "Test Author",
    Licence = "Test metadata",
    AircraftAdapters = ["generic.preview"],
    Assets = assets
};

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static void AssertEqual<T>(T expected, T actual, string message)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"{message} Expected '{expected}', got '{actual}'.");
    }
}

static void AssertNear(double expected, double actual, string message)
{
    if (Math.Abs(expected - actual) > 0.001d)
    {
        throw new InvalidOperationException($"{message} Expected '{expected}', got '{actual}'.");
    }
}
