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
    ("Boarding waits for an open door", BoardingWaitsForDoorAsync),
    ("Passenger seat centres match the cabin layout", PassengerSeatCentresMatchLayoutAsync),
    ("Passenger seat occupation becomes secured", PassengerSeatOccupationBecomesSecuredAsync),
    ("Passenger boarding completes", PassengerBoardingCompletesAsync)
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
