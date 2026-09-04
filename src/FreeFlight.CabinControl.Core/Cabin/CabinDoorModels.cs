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
        PassengerCabinLayout.BritishAirways777200Er => Build777(4),
        PassengerCabinLayout.BritishAirways777300 => Build777(5),
        PassengerCabinLayout.BritishAirwaysA320200 or PassengerCabinLayout.BritishAirwaysA320Neo => BuildA320(),
        PassengerCabinLayout.FlightFactor777V2 => Build777(5),
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

    private static IReadOnlyList<CabinDoorDefinition> Build777(int doorPairs)
    {
        var doors = new List<CabinDoorDefinition>(doorPairs * 2);
        for (var number = 1; number <= doorPairs; number++)
        {
            var station = doorPairs == 1 ? 0.5d : 0.07d + ((number - 1) * (0.86d / (doorPairs - 1)));
            doors.Add(new CabinDoorDefinition(
                $"L{number}",
                $"L{number}",
                CabinDoorSide.Left,
                number,
                CabinDoorKind.PassengerDoor,
                station,
                true,
                true));
            doors.Add(new CabinDoorDefinition(
                $"R{number}",
                $"R{number}",
                CabinDoorSide.Right,
                number,
                CabinDoorKind.ServiceDoor,
                station,
                true,
                false));
        }

        return doors;
    }

    private static IReadOnlyList<CabinDoorDefinition> BuildA320() =>
    [
        new("L1", "L1", CabinDoorSide.Left, 1, CabinDoorKind.PassengerDoor, 0.08d, true, true),
        new("R1", "R1", CabinDoorSide.Right, 1, CabinDoorKind.ServiceDoor, 0.08d, true, false),
        new("OWL1", "Overwing L1", CabinDoorSide.Left, null, CabinDoorKind.EmergencyExit, 0.46d, false, false),
        new("OWL2", "Overwing L2", CabinDoorSide.Left, null, CabinDoorKind.EmergencyExit, 0.54d, false, false),
        new("OWR1", "Overwing R1", CabinDoorSide.Right, null, CabinDoorKind.EmergencyExit, 0.46d, false, false),
        new("OWR2", "Overwing R2", CabinDoorSide.Right, null, CabinDoorKind.EmergencyExit, 0.54d, false, false),
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
