namespace FreeFlight.CabinControl.Core.Integration;

public enum AircraftCabinSemantic
{
    PassengerDoorL1,
    PassengerDoorL2,
    SeatbeltSign,
    SafetyVideo,
    BoardingMusic,
    CabinLighting,
    CabinTemperature,
    PassengerAddress,
    CabinDisplayPage
}

public sealed record AircraftIdentity(
    string Icao,
    string Description,
    string AcfRelativePath);

public sealed record AircraftCabinBinding(
    AircraftCabinSemantic Semantic,
    IReadOnlyList<string> ReadDatarefs,
    IReadOnlyList<string> WriteDatarefs,
    IReadOnlyList<string> Commands,
    int? ArrayIndex = null);

public interface IAircraftCabinAdapter
{
    string Id { get; }

    string DisplayName { get; }

    bool Matches(AircraftIdentity aircraft);

    IReadOnlyList<AircraftCabinBinding> Bindings { get; }
}

public sealed class FlightFactor777V2CabinAdapter : IAircraftCabinAdapter
{
    public string Id => "flightfactor.777v2";

    public string DisplayName => "FlightFactor 777 v2";

    public IReadOnlyList<AircraftCabinBinding> Bindings { get; } =
    [
        new(
            AircraftCabinSemantic.PassengerDoorL1,
            ["sim/flightmodel2/misc/door_open_ratio"],
            ["sim/flightmodel2/misc/door_open_ratio"],
            [],
            0),
        new(
            AircraftCabinSemantic.PassengerDoorL2,
            ["sim/flightmodel2/misc/door_open_ratio"],
            ["sim/flightmodel2/misc/door_open_ratio"],
            [],
            1),
        new(
            AircraftCabinSemantic.SeatbeltSign,
            [
                "sim/cockpit2/annunciators/fasten_seatbelt",
                "sim/cockpit2/switches/fasten_seat_belts",
                "sim/cockpit/switches/fasten_seat_belts"
            ],
            [
                "sim/cockpit2/switches/fasten_seat_belts",
                "sim/cockpit/switches/fasten_seat_belts"
            ],
            [])
    ];

    public bool Matches(AircraftIdentity aircraft)
    {
        var identity = $"{aircraft.Icao} {aircraft.Description} {aircraft.AcfRelativePath}";
        return identity.Contains("777", StringComparison.OrdinalIgnoreCase) &&
               (identity.Contains("FlightFactor", StringComparison.OrdinalIgnoreCase) ||
                identity.Contains("Flight Factor", StringComparison.OrdinalIgnoreCase) ||
                identity.Contains("1-sim", StringComparison.OrdinalIgnoreCase));
    }
}
