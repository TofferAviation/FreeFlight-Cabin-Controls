using FreeFlight.CabinControl.Core.Passengers;

namespace FreeFlight.CabinControl.Core.Cabin;

public enum CabinDoorSide
{
    Left,
    Right
}

public enum CabinDoorKind
{
    PassengerDoor,
    ServiceDoor,
    EmergencyExit,
    CargoDoor,
    Unknown
}

public sealed record CabinDoorDefinition(
    string Id,
    string DisplayName,
    CabinDoorSide Side,
    int? DoorNumber,
    CabinDoorKind Kind,
    double LongitudinalStation,
    bool IsControllable,
    bool CanBoardPassengers)
{
    public bool IsEmergencyExit => Kind == CabinDoorKind.EmergencyExit;
}

public sealed record CabinDoorState(
    CabinDoorDefinition Door,
    bool IsAvailable,
    double OpenRatio,
    string Source)
{
    public bool IsOpen => IsAvailable && OpenRatio >= 0.5d;
}

public static class CabinDoorCatalog
{
    public static IReadOnlyList<CabinDoorDefinition> ForLayout(PassengerCabinLayout layout) => layout switch
    {
        PassengerCabinLayout.BritishAirways777200Er => BuildWideBody(4),
        PassengerCabinLayout.BritishAirways777300 => BuildWideBody(5),
        PassengerCabinLayout.FlightFactor777V2 => BuildWideBody(5),
        PassengerCabinLayout.BritishAirwaysA3501000 => BuildWideBody(4),
        PassengerCabinLayout.BritishAirwaysA380800 => BuildWideBody(5),
        PassengerCabinLayout.BritishAirways7878 or
        PassengerCabinLayout.BritishAirways7879 or
        PassengerCabinLayout.BritishAirways78710 => BuildWideBody(4),
        PassengerCabinLayout.BritishAirwaysA319100 => BuildNarrowBody(overwingPairsPerSide: 1),
        PassengerCabinLayout.BritishAirwaysA320200 or
        PassengerCabinLayout.BritishAirwaysA320Neo => BuildNarrowBody(overwingPairsPerSide: 2),
        PassengerCabinLayout.BritishAirwaysA321200 => BuildA321Classic(),
        PassengerCabinLayout.BritishAirwaysA321Neo => BuildA321Neo(),
        PassengerCabinLayout.BritishAirwaysEmbraer190 => BuildRegionalJet(),
        _ => []
    };

    public static CabinDoorDefinition? Find(PassengerCabinLayout layout, string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        return ForLayout(layout).FirstOrDefault(door =>
            string.Equals(door.Id, id.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<CabinDoorDefinition> BuildWideBody(int doorPairs)
    {
        var doors = new List<CabinDoorDefinition>(doorPairs * 2);
        for (var number = 1; number <= doorPairs; number++)
        {
            var station = doorPairs == 1 ? 0.5d : 0.07d + ((number - 1) * (0.86d / (doorPairs - 1)));
            doors.Add(new CabinDoorDefinition(
                $"L{number}", $"L{number}", CabinDoorSide.Left, number,
                CabinDoorKind.PassengerDoor, station, true, true));
            doors.Add(new CabinDoorDefinition(
                $"R{number}", $"R{number}", CabinDoorSide.Right, number,
                CabinDoorKind.ServiceDoor, station, true, false));
        }

        return doors;
    }

    private static IReadOnlyList<CabinDoorDefinition> BuildNarrowBody(int overwingPairsPerSide)
    {
        var doors = new List<CabinDoorDefinition>
        {
            new("L1", "L1", CabinDoorSide.Left, 1, CabinDoorKind.PassengerDoor, 0.08d, true, true),
            new("R1", "R1", CabinDoorSide.Right, 1, CabinDoorKind.ServiceDoor, 0.08d, true, false)
        };
        for (var index = 1; index <= overwingPairsPerSide; index++)
        {
            var station = overwingPairsPerSide == 1 ? 0.50d : index == 1 ? 0.46d : 0.54d;
            doors.Add(new CabinDoorDefinition($"OWL{index}", $"Overwing L{index}", CabinDoorSide.Left, null,
                CabinDoorKind.EmergencyExit, station, false, false));
            doors.Add(new CabinDoorDefinition($"OWR{index}", $"Overwing R{index}", CabinDoorSide.Right, null,
                CabinDoorKind.EmergencyExit, station, false, false));
        }
        doors.Add(new CabinDoorDefinition("L2", "L2", CabinDoorSide.Left, 2, CabinDoorKind.PassengerDoor, 0.92d, true, true));
        doors.Add(new CabinDoorDefinition("R2", "R2", CabinDoorSide.Right, 2, CabinDoorKind.ServiceDoor, 0.92d, true, false));
        return doors;
    }

    private static IReadOnlyList<CabinDoorDefinition> BuildA321Classic() =>
    [
        new("L1", "L1", CabinDoorSide.Left, 1, CabinDoorKind.PassengerDoor, 0.06d, true, true),
        new("R1", "R1", CabinDoorSide.Right, 1, CabinDoorKind.ServiceDoor, 0.06d, true, false),
        new("L2", "L2", CabinDoorSide.Left, 2, CabinDoorKind.PassengerDoor, 0.34d, true, true),
        new("R2", "R2", CabinDoorSide.Right, 2, CabinDoorKind.ServiceDoor, 0.34d, true, false),
        new("L3", "L3", CabinDoorSide.Left, 3, CabinDoorKind.PassengerDoor, 0.66d, true, true),
        new("R3", "R3", CabinDoorSide.Right, 3, CabinDoorKind.ServiceDoor, 0.66d, true, false),
        new("L4", "L4", CabinDoorSide.Left, 4, CabinDoorKind.PassengerDoor, 0.94d, true, true),
        new("R4", "R4", CabinDoorSide.Right, 4, CabinDoorKind.ServiceDoor, 0.94d, true, false)
    ];

    private static IReadOnlyList<CabinDoorDefinition> BuildA321Neo() =>
    [
        new("L1", "L1", CabinDoorSide.Left, 1, CabinDoorKind.PassengerDoor, 0.06d, true, true),
        new("R1", "R1", CabinDoorSide.Right, 1, CabinDoorKind.ServiceDoor, 0.06d, true, false),
        new("OWL1", "Overwing L1", CabinDoorSide.Left, null, CabinDoorKind.EmergencyExit, 0.48d, false, false),
        new("OWR1", "Overwing R1", CabinDoorSide.Right, null, CabinDoorKind.EmergencyExit, 0.48d, false, false),
        new("L3", "L3", CabinDoorSide.Left, 3, CabinDoorKind.PassengerDoor, 0.69d, true, true),
        new("R3", "R3", CabinDoorSide.Right, 3, CabinDoorKind.ServiceDoor, 0.69d, true, false),
        new("L4", "L4", CabinDoorSide.Left, 4, CabinDoorKind.PassengerDoor, 0.94d, true, true),
        new("R4", "R4", CabinDoorSide.Right, 4, CabinDoorKind.ServiceDoor, 0.94d, true, false)
    ];

    private static IReadOnlyList<CabinDoorDefinition> BuildRegionalJet() =>
    [
        new("L1", "L1", CabinDoorSide.Left, 1, CabinDoorKind.PassengerDoor, 0.08d, true, true),
        new("R1", "R1", CabinDoorSide.Right, 1, CabinDoorKind.ServiceDoor, 0.08d, true, false),
        new("OWL1", "Overwing L1", CabinDoorSide.Left, null, CabinDoorKind.EmergencyExit, 0.50d, false, false),
        new("OWR1", "Overwing R1", CabinDoorSide.Right, null, CabinDoorKind.EmergencyExit, 0.50d, false, false),
        new("L2", "L2", CabinDoorSide.Left, 2, CabinDoorKind.PassengerDoor, 0.92d, true, true),
        new("R2", "R2", CabinDoorSide.Right, 2, CabinDoorKind.ServiceDoor, 0.92d, true, false)
    ];
}

public static class CabinDoorTelemetry
{
    public static IReadOnlyList<CabinDoorState> Project(
        PassengerCabinLayout layout,
        IReadOnlyDictionary<string, double> signals)
    {
        ArgumentNullException.ThrowIfNull(signals);
        var states = new List<CabinDoorState>();
        foreach (var door in CabinDoorCatalog.ForLayout(layout))
        {
            var canonicalKey = $"door_{door.Id.ToLowerInvariant()}_ratio";
            if (signals.TryGetValue(canonicalKey, out var value) && double.IsFinite(value))
            {
                states.Add(new CabinDoorState(door, true, Normalize(value), canonicalKey));
                continue;
            }

            states.Add(new CabinDoorState(door, false, 0d, string.Empty));
        }

        return states;
    }

    private static double Normalize(double value) =>
        Math.Clamp(value > 1.5d ? value / 100d : value, 0d, 1d);
}
