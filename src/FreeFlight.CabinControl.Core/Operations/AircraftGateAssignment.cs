using System.Security.Cryptography;
using System.Text;

namespace FreeFlight.CabinControl.Core.Operations;

public enum AircraftGateCategory
{
    Unknown,
    Regional,
    NarrowBody,
    WideBody
}

public sealed record AircraftGateAssignment(
    string GateNumber,
    string Concourse,
    AircraftGateCategory AircraftCategory,
    bool IsAutomatic,
    string Reason)
{
    public string Summary => IsAutomatic
        ? $"{Concourse} · {Reason}"
        : Reason;
}

public static class AircraftGateAssignmentService
{
    private static readonly string[] HeathrowTerminal5AGates =
    [
        "A4", "A5", "A6", "A7", "A8", "A9", "A10", "A11", "A12", "A13",
        "A14", "A15", "A16", "A17", "A18", "A19", "A20", "A21", "A22", "A23"
    ];

    private static readonly string[] HeathrowTerminal5WideBodyGates =
    [
        "B32", "B33", "B34", "B35", "B36", "B37", "B38", "B39", "B42", "B43",
        "B44", "B45", "B46", "B47", "B48", "C52", "C53", "C54", "C55", "C56",
        "C57", "C58", "C60", "C61", "C62", "C63", "C64", "C65", "C66"
    ];

    private static readonly string[] RegionalPrefixes = ["AT4", "AT7", "DH8", "E17", "E19", "CRJ"];
    private static readonly string[] NarrowBodyPrefixes = ["A20", "A21", "A31", "A32", "B73", "B75", "B38", "B39", "B3M", "BCS"];
    private static readonly string[] WideBodyPrefixes = ["A30", "A33", "A34", "A35", "A38", "B74", "B76", "B77", "B78"];

    public static AircraftGateAssignment Assign(
        string airportCode,
        string aircraftIcao,
        string flightNumber,
        DateTimeOffset departure,
        string manualGate,
        bool automaticAssignment)
    {
        var fallbackGate = NormalizeGate(manualGate);
        if (!automaticAssignment)
        {
            return new AircraftGateAssignment(
                fallbackGate,
                "Manual",
                ClassifyAircraft(aircraftIcao),
                false,
                "Manual gate assignment");
        }

        var airport = NormalizeAirport(airportCode);
        if (airport is not ("LHR" or "EGLL"))
        {
            return new AircraftGateAssignment(
                fallbackGate,
                "Airport profile pending",
                ClassifyAircraft(aircraftIcao),
                false,
                $"No automatic profile for {airport}");
        }

        var category = ClassifyAircraft(aircraftIcao);
        var gates = category == AircraftGateCategory.WideBody
            ? HeathrowTerminal5WideBodyGates
            : HeathrowTerminal5AGates;
        var gate = gates[StableIndex(
            $"{airport}|{aircraftIcao.Trim().ToUpperInvariant()}|{flightNumber.Trim().ToUpperInvariant()}|{departure:yyyyMMdd}",
            gates.Length)];
        var concourse = gate[0] switch
        {
            'B' => "Heathrow T5B",
            'C' => "Heathrow T5C",
            _ => "Heathrow T5A"
        };
        var reason = category == AircraftGateCategory.WideBody
            ? "wide-body / heavy stand profile"
            : category == AircraftGateCategory.Regional
                ? "regional aircraft stand profile"
                : category == AircraftGateCategory.NarrowBody
                    ? "narrow-body stand profile"
                    : "standard stand profile";

        return new AircraftGateAssignment(gate, concourse, category, true, reason);
    }

    public static AircraftGateCategory ClassifyAircraft(string aircraftIcao)
    {
        var normalized = aircraftIcao.Trim().ToUpperInvariant();
        if (normalized.Length == 0)
        {
            return AircraftGateCategory.Unknown;
        }

        if (WideBodyPrefixes.Any(normalized.StartsWith))
        {
            return AircraftGateCategory.WideBody;
        }

        if (NarrowBodyPrefixes.Any(normalized.StartsWith))
        {
            return AircraftGateCategory.NarrowBody;
        }

        return RegionalPrefixes.Any(normalized.StartsWith)
            ? AircraftGateCategory.Regional
            : AircraftGateCategory.Unknown;
    }

    public static string DescribeAircraft(string aircraftIcao)
    {
        var normalized = aircraftIcao.Trim().ToUpperInvariant();
        return normalized switch
        {
            "A319" => "Airbus A319",
            "A320" => "Airbus A320",
            "A20N" => "Airbus A320neo",
            "A321" => "Airbus A321",
            "A21N" => "Airbus A321neo",
            "B772" => "Boeing 777-200ER",
            "B77L" => "Boeing 777-200LR",
            "B773" => "Boeing 777-300",
            "B77W" => "Boeing 777-300ER",
            "B788" => "Boeing 787-8",
            "B789" => "Boeing 787-9",
            "B78X" => "Boeing 787-10",
            "A359" => "Airbus A350-900",
            "A35K" => "Airbus A350-1000",
            "A388" => "Airbus A380-800",
            _ when normalized.Length > 0 => normalized,
            _ => "Aircraft not detected"
        };
    }

    private static int StableIndex(string value, int itemCount)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        var seed = BitConverter.ToUInt32(hash, 0);
        return (int)(seed % itemCount);
    }

    private static string NormalizeAirport(string value) => value.Trim().ToUpperInvariant() switch
    {
        "EGLL" => "LHR",
        var airport when airport.Length > 0 => airport,
        _ => "UNKNOWN"
    };

    private static string NormalizeGate(string value) => string.IsNullOrWhiteSpace(value)
        ? "TBD"
        : value.Trim().ToUpperInvariant();
}
