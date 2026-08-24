using FreeFlight.CabinControl.Core.Configuration;
using FreeFlight.CabinControl.Core.Content;
using FreeFlight.CabinControl.Core.Persistence;
using FreeFlight.CabinControl.Core.Passengers;
using FreeFlight.CabinControl.Core.Operations;

var tests = new (string Name, Func<Task> Run)[]
{
    ("Settings round-trip", SettingsRoundTripAsync),
    ("Turnaround schedule calculates from departure", TurnaroundScheduleCalculatesFromDepartureAsync),
    ("Turnaround schedule follows the operations clock", TurnaroundScheduleFollowsClockAsync),
    ("Heavy aircraft receive deterministic T5 B or C gates", HeavyAircraftReceiveDeterministicGateAsync),
    ("Narrow-body aircraft receive T5 A gates", NarrowBodyAircraftReceiveAGateAsync),
    ("Unsupported airports retain the manual gate", UnsupportedAirportRetainsManualGateAsync),
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
    ("Passenger deboarding completes", PassengerDeboardingCompletesAsync),
    ("British Airways cabin layouts map official seats", BritishAirwaysCabinLayoutsMapSeatsAsync),
    ("British Airways layouts board and deboard", BritishAirwaysLayoutsOperateAsync),
    ("Partial loads distribute tickets across the cabin", PartialLoadsDistributeTicketsAsync),
    ("Gate desk boarding updates the cabin engine", GateDeskBoardingUpdatesCabinAsync)
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
            PassengerCabinLayoutId = "british-airways.777-300",
            SimBriefPilotId = "123456",
            SimBriefAutoSync = true,
            GateFlightNumber = "BA281",
            GateOriginIata = "LHR",
            GateDestinationIata = "LAX",
            GateNumber = "C65",
            AutomaticGateAssignment = false,
            ScheduledDepartureLocal = "14:25",
            TurnaroundMinutes = 75,
            AutomaticGateTiming = false,
            BoardingStartMinutesBeforeDeparture = 60,
            FinalBoardingMinutesBeforeDeparture = 10,
            GateCloseMinutesBeforeDeparture = 3,
            ManualGateOverride = true,
            PassengerNameRegionMix = "Europe",
            PassengerGenerationSeed = 456789,
            BoardingGroupOrder = "Outside In",
            SpecialAssistanceBoardsFirst = false,
            PreventBoardingAfterGateClose = false,
            BoardingPassPrinter = "Test boarding printer",
            BagTagPrinter = "Test bag printer",
            SoundAlerts = false,
            BoardingCallChime = "Silent",
            AutoArchiveCompletedFlights = false,
            ArchiveCompletedFlightsAfterDays = 90,
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
        AssertEqual("british-airways.777-300", actual.PassengerCabinLayoutId, "Cabin layout selection was not persisted.");
        AssertEqual("123456", actual.SimBriefPilotId, "SimBrief Pilot ID was not persisted.");
        AssertEqual(true, actual.SimBriefAutoSync, "SimBrief auto-sync preference was not persisted.");
        AssertEqual("BA281", actual.GateFlightNumber, "Gate flight number was not persisted.");
        AssertEqual("LAX", actual.GateDestinationIata, "Gate route was not persisted.");
        AssertEqual("C65", actual.GateNumber, "Gate number was not persisted.");
        AssertEqual(false, actual.AutomaticGateAssignment, "Automatic gate assignment was not persisted.");
        AssertEqual("14:25", actual.ScheduledDepartureLocal, "Departure time was not persisted.");
        AssertEqual(75, actual.TurnaroundMinutes, "Turnaround duration was not persisted.");
        AssertEqual(false, actual.AutomaticGateTiming, "Automatic gate timing was not persisted.");
        AssertEqual(60, actual.BoardingStartMinutesBeforeDeparture, "Boarding timing was not persisted.");
        AssertEqual(3, actual.GateCloseMinutesBeforeDeparture, "Gate close timing was not persisted.");
        AssertEqual(true, actual.ManualGateOverride, "Manual gate override was not persisted.");
        AssertEqual("Europe", actual.PassengerNameRegionMix, "Passenger region mix was not persisted.");
        AssertEqual(456789, actual.PassengerGenerationSeed, "Passenger seed was not persisted.");
        AssertEqual("Outside In", actual.BoardingGroupOrder, "Boarding group order was not persisted.");
        AssertEqual("Test boarding printer", actual.BoardingPassPrinter, "Boarding-pass printer was not persisted.");
        AssertEqual("Silent", actual.BoardingCallChime, "Boarding chime was not persisted.");
        AssertEqual(90, actual.ArchiveCompletedFlightsAfterDays, "Archive retention was not persisted.");
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

static Task HeavyAircraftReceiveDeterministicGateAsync()
{
    var departure = new DateTimeOffset(2026, 8, 25, 18, 30, 0, TimeSpan.FromHours(2));
    var first = AircraftGateAssignmentService.Assign("LHR", "B77W", "BA117", departure, "A4", true);
    var second = AircraftGateAssignmentService.Assign("EGLL", "B77W", "BA117", departure, "A4", true);

    Assert(first.GateNumber.StartsWith('B') || first.GateNumber.StartsWith('C'), "A Boeing 777 was not assigned to a T5 B/C gate.");
    AssertEqual(first.GateNumber, second.GateNumber, "The same flight did not retain its deterministic gate.");
    AssertEqual(AircraftGateCategory.WideBody, first.AircraftCategory, "A Boeing 777 was not classified as wide-body/heavy.");
    return Task.CompletedTask;
}

static Task NarrowBodyAircraftReceiveAGateAsync()
{
    var assignment = AircraftGateAssignmentService.Assign(
        "LHR",
        "A320",
        "BA281",
        new DateTimeOffset(2026, 8, 25, 14, 25, 0, TimeSpan.FromHours(2)),
        "B42",
        true);

    Assert(assignment.GateNumber.StartsWith('A'), "An Airbus A320 was not assigned to a T5 A gate.");
    AssertEqual(AircraftGateCategory.NarrowBody, assignment.AircraftCategory, "An Airbus A320 was not classified as narrow-body.");
    return Task.CompletedTask;
}

static Task UnsupportedAirportRetainsManualGateAsync()
{
    var assignment = AircraftGateAssignmentService.Assign(
        "OSL",
        "B77W",
        "BA761",
        DateTimeOffset.Now,
        "D12",
        true);

    AssertEqual("D12", assignment.GateNumber, "An airport without a gate profile did not retain the manual fallback.");
    AssertEqual(false, assignment.IsAutomatic, "An unsupported airport was incorrectly marked as automatically assigned.");
    return Task.CompletedTask;
}

static Task TurnaroundScheduleCalculatesFromDepartureAsync()
{
    var departure = new DateTimeOffset(2026, 8, 25, 0, 30, 0, TimeSpan.FromHours(2));
    var schedule = FlightTurnaroundSchedule.Create(departure, 60, 45, 5, 2);

    AssertEqual(departure.AddMinutes(-60), schedule.TurnaroundStart, "Turnaround did not begin one hour before departure.");
    AssertEqual(departure.AddMinutes(-55), schedule.GateOpen, "Gate opening was not calculated from the boarding window.");
    AssertEqual(departure.AddMinutes(-45), schedule.BoardingStart, "Boarding start was not calculated from departure.");
    AssertEqual(departure.AddMinutes(-5), schedule.FinalBoarding, "Final boarding was not calculated from departure.");
    AssertEqual(departure.AddMinutes(-2), schedule.GateClose, "Gate close was not calculated from departure.");
    AssertEqual(new DateTimeOffset(2026, 8, 24, 23, 30, 0, TimeSpan.FromHours(2)), schedule.TurnaroundStart, "Midnight rollover was not preserved.");
    return Task.CompletedTask;
}

static Task TurnaroundScheduleFollowsClockAsync()
{
    var departure = new DateTimeOffset(2026, 8, 25, 18, 30, 0, TimeSpan.FromHours(2));
    var schedule = FlightTurnaroundSchedule.Create(departure, 60, 45, 5, 2);

    AssertEqual(TurnaroundStage.AwaitingTurnaround, schedule.GetStage(departure.AddMinutes(-61)), "Early clock time selected the wrong stage.");
    AssertEqual(TurnaroundStage.Turnaround, schedule.GetStage(departure.AddMinutes(-58)), "Turnaround stage did not activate.");
    AssertEqual(TurnaroundStage.GateOpen, schedule.GetStage(departure.AddMinutes(-50)), "Gate-open stage did not activate.");
    AssertEqual(TurnaroundStage.Boarding, schedule.GetStage(departure.AddMinutes(-30)), "Boarding stage did not activate.");
    AssertEqual(TurnaroundStage.GateClosing, schedule.GetStage(departure.AddMinutes(-1)), "Gate-closing stage did not activate.");
    AssertEqual(TurnaroundStage.Departure, schedule.GetStage(departure), "Departure stage did not activate.");
    return Task.CompletedTask;
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
    AssertAisles(engine, PassengerCabinClass.First, 56d, 94d);
    AssertAisles(engine, PassengerCabinClass.Business, 53d, 95d);
    AssertAisles(engine, PassengerCabinClass.Economy, 57d, 95d);
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
    AssertEqual(311, engine.Capacity, "The visual cabin capacity did not match its individual seat centres.");
    AssertSeatCentre(engine, "1A", 304d, 30d);
    AssertSeatCentre(engine, "4K", 403d, 119d);
    AssertSeatCentre(engine, "5A", 447d, 31d);
    AssertSeatCentre(engine, "9K", 565d, 116d);
    AssertSeatCentre(engine, "10A", 630d, 30d);
    AssertSeatCentre(engine, "33K", 890d, 120d);
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

static Task BritishAirwaysCabinLayoutsMapSeatsAsync()
{
    var twoHundred = new PassengerBoardingEngine(int.MaxValue, PassengerCabinLayout.BritishAirways777200Er);
    AssertEqual(280, twoHundred.Capacity, "The British Airways 777-200ER mapped capacity is incorrect.");
    AssertSeatCentre(twoHundred, "1A", 72d, 136d);
    AssertSeatCentre(twoHundred, "40K", 985d, 66d);
    Assert(twoHundred.BoardingGroups.SequenceEqual(Enumerable.Range(1, 8)),
        "The British Airways 777-200ER did not create boarding groups 1–8.");

    var threeHundred = new PassengerBoardingEngine(int.MaxValue, PassengerCabinLayout.BritishAirways777300);
    AssertEqual(266, threeHundred.Capacity, "The British Airways 777-300 mapped capacity is incorrect.");
    AssertSeatCentre(threeHundred, "1A", 76d, 136d);
    AssertSeatCentre(threeHundred, "44K", 992d, 66d);
    Assert(threeHundred.BoardingGroups.SequenceEqual(Enumerable.Range(1, 8)),
        "The British Airways 777-300 did not create boarding groups 1–8.");
    return Task.CompletedTask;
}

static Task BritishAirwaysLayoutsOperateAsync()
{
    foreach (var layout in new[]
             {
                 PassengerCabinLayout.BritishAirways777200Er,
                 PassengerCabinLayout.BritishAirways777300
             })
    {
        var engine = new PassengerBoardingEngine(int.MaxValue, layout);
        engine.SetDoorOpen(BoardingDoor.L1, true);
        engine.SetDoorOpen(BoardingDoor.L2, true);
        engine.Start();
        Advance(engine, seconds: 120d, speed: 8d);
        AssertEqual(BoardingRunState.Complete, engine.State, $"{layout} boarding did not complete.");
        AssertEqual(engine.Capacity, engine.BoardedCount, $"{layout} did not seat every ticketed passenger.");
        Assert(engine.Passengers.All(passenger =>
                Math.Abs(passenger.Position.X - passenger.Seat.X) < 0.001d &&
                Math.Abs(passenger.Position.Y - passenger.Seat.Y) < 0.001d),
            $"{layout} did not finish passengers in their boarding-pass seats.");

        engine.StartDeboarding();
        Advance(engine, seconds: 120d, speed: 8d);
        AssertEqual(BoardingRunState.DeboardingComplete, engine.State, $"{layout} deboarding did not complete.");
        AssertEqual(engine.Capacity, engine.DeboardedCount, $"{layout} did not deboard every passenger.");
    }

    return Task.CompletedTask;
}

static Task PartialLoadsDistributeTicketsAsync()
{
    var engine = new PassengerBoardingEngine(180, PassengerCabinLayout.BritishAirways777200Er);
    AssertEqual(180, engine.Passengers.Select(passenger => passenger.Seat.Number).Distinct().Count(),
        "Two passengers were assigned the same seat.");
    Assert(engine.Passengers.Any(passenger => passenger.Seat.X < 300d) &&
           engine.Passengers.Any(passenger => passenger.Seat.X > 900d),
        "The partial load filled a single end of the aircraft instead of distributing tickets.");

    foreach (var group in engine.Passengers.GroupBy(passenger => passenger.BoardingGroup).Where(group => group.Count() >= 5))
    {
        var xCoordinates = group.Select(passenger => passenger.Seat.X).ToArray();
        var adjacentPairs = xCoordinates.Zip(xCoordinates.Skip(1)).ToArray();
        Assert(adjacentPairs.Any(pair => pair.First < pair.Second) &&
               adjacentPairs.Any(pair => pair.First > pair.Second),
            $"Boarding Group {group.Key} was assigned in a rigid front-to-back or back-to-front sequence.");
    }

    return Task.CompletedTask;
}

static Task GateDeskBoardingUpdatesCabinAsync()
{
    var engine = new PassengerBoardingEngine(24, PassengerCabinLayout.BritishAirways777200Er);
    var selected = engine.Passengers[17];
    Assert(engine.TryBoardPassenger(selected.Id), "The gate desk could not board an eligible passenger.");
    AssertEqual(PassengerMovementState.Seated, selected.MovementState,
        "Gate desk boarding did not place the passenger in the assigned seat.");
    AssertNear(selected.Seat.X, selected.Position.X, "Gate desk boarding used the wrong horizontal seat coordinate.");
    AssertNear(selected.Seat.Y, selected.Position.Y, "Gate desk boarding used the wrong vertical seat coordinate.");
    AssertEqual(1, engine.BoardedCount, "Gate desk boarding did not update the cabin count.");
    Assert(!engine.TryBoardPassenger(selected.Id), "The same passenger was boarded twice.");

    engine.SetDoorOpen(BoardingDoor.L2, true);
    engine.Start();
    Advance(engine, seconds: 60d, speed: 8d);
    AssertEqual(BoardingRunState.Complete, engine.State,
        "Normal boarding did not complete after a passenger was boarded from the gate desk.");
    AssertEqual(24, engine.BoardedCount, "The final count was incorrect after gate desk boarding.");
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
