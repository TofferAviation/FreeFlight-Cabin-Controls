namespace FreeFlight.CabinControl.Core.Integration;

/// <summary>
/// The state of a known aircraft profile. A profile is never treated as safe
/// for a shared operation simply because its name looks familiar: its local
/// X-Plane datarefs must also pass the checks bundled with Ember.
/// </summary>
public enum SharedFlightProfileState
{
    SimulatorNotConnected,
    UnrecognisedAircraft,
    NeedsGuidedValidation,
    ReadyForGuidedValidation
}

public sealed record SharedFlightProfileCheck(
    string Name,
    bool Passed,
    bool Required,
    string Detail);

public sealed record SharedFlightProfileReport(
    string Simulator,
    string SimulatorVersion,
    string AircraftIcao,
    string AircraftDescription,
    string AircraftRelativePath,
    string ProfileId,
    string ProfileName,
    SharedFlightProfileState State,
    string Summary,
    IReadOnlyList<SharedFlightProfileCheck> Checks,
    DateTimeOffset GeneratedAt)
{
    public static SharedFlightProfileReport Offline { get; } = new(
        "X-Plane 12",
        "",
        "",
        "No aircraft detected",
        "",
        "",
        "No X-Plane profile detected",
        SharedFlightProfileState.SimulatorNotConnected,
        "Start X-Plane and load one of the supported test aircraft to run the local profile check.",
        [],
        DateTimeOffset.UtcNow);
}

/// <summary>
/// Immutable recognition and validation rules for Ember's first X-Plane
/// shared-flight cohort. The catalogue avoids guessed, aircraft-specific
/// control mappings: each detected add-on must expose the baseline signals
/// before a tester can be asked to validate it.
/// </summary>
public static class SharedFlightProfileCatalog
{
    private sealed record ProfileDefinition(
        string Id,
        string Name,
        Func<string, bool> Matches);

    private static readonly IReadOnlyList<ProfileDefinition> Profiles =
    [
        new("toliss-a330neo", "ToLiss A330neo", text => Has(text, "toliss") && Has(text, "a330")),
        new("toliss-a320-family", "ToLiss A320 family · NEO / CFM / IAE", text =>
            Has(text, "toliss") && (Has(text, "a319") || Has(text, "a320") || Has(text, "a321"))),
        new("flightfactor-777v2", "FlightFactor 777 v2", text =>
            (Has(text, "flightfactor") || Has(text, "flight factor") || Has(text, "ff")) && Has(text, "777")),
        new("xcrafts-ejet-family", "X-Crafts E-Jet family", text =>
            Has(text, "x-crafts") || Has(text, "xcrafts") || Has(text, "e-jets") || Has(text, "ejet")),
        new("flightfactor-a350", "FlightFactor A350", text =>
            (Has(text, "flightfactor") || Has(text, "flight factor") || Has(text, "ff")) && Has(text, "a350")),
        new("zibo-levelup-737", "Zibo / LevelUp 737", text =>
            Has(text, "zibo") || Has(text, "zibomod") || Has(text, "b737-800x") ||
            Has(text, "levelup") || Has(text, "level up") || Has(text, "lvlup")),
        new("flightfactor-a320", "FlightFactor A320", text =>
            (Has(text, "flightfactor") || Has(text, "flight factor") || Has(text, "ff")) && Has(text, "a320"))
    ];

    public static SharedFlightProfileReport EvaluateXPlane(
        bool connected,
        string simulatorVersion,
        string aircraftIcao,
        string aircraftDescription,
        string aircraftRelativePath,
        IReadOnlyCollection<string> availableDatarefs,
        bool nativeJetwayCommandAvailable,
        DateTimeOffset? generatedAt = null)
    {
        var now = generatedAt ?? DateTimeOffset.UtcNow;
        if (!connected)
        {
            return SharedFlightProfileReport.Offline with { GeneratedAt = now };
        }

        var source = string.Join(" ", aircraftIcao, aircraftDescription, aircraftRelativePath).ToLowerInvariant();
        var profile = Profiles.FirstOrDefault(candidate => candidate.Matches(source));
        if (profile is null)
        {
            return new SharedFlightProfileReport(
                "X-Plane 12",
                simulatorVersion,
                aircraftIcao,
                aircraftDescription,
                aircraftRelativePath,
                "",
                "No X-Plane profile detected",
                SharedFlightProfileState.UnrecognisedAircraft,
                "This aircraft is not in the first validation group. Ember will not attempt to synchronise it.",
                [],
                now);
        }

        var datarefs = new HashSet<string>(availableDatarefs, StringComparer.Ordinal);
        var hasTelemetry = datarefs.Contains("sim/flightmodel/position/groundspeed") &&
                           (datarefs.Contains("sim/flightmodel/failures/onground_any") ||
                            datarefs.Contains("sim/flightmodel2/gear/on_ground"));
        var hasSeatbelts = datarefs.Contains("sim/cockpit2/switches/fasten_seat_belts") ||
                          datarefs.Contains("sim/cockpit/switches/fasten_seat_belts") ||
                          datarefs.KeysAnyContains("seatbelt", "seat_belt", "passsign");
        var hasDoors = datarefs.Contains("sim/flightmodel2/misc/door_open_ratio") ||
                       datarefs.KeysAnyContains("door", "exit");
        var hasAircraftIdentity = !string.IsNullOrWhiteSpace(aircraftDescription) ||
                                  !string.IsNullOrWhiteSpace(aircraftRelativePath);
        var checks = new[]
        {
            new SharedFlightProfileCheck("Aircraft identity", hasAircraftIdentity, true,
                hasAircraftIdentity ? "The loaded aircraft was identified locally." : "X-Plane did not expose an aircraft description or path."),
            new SharedFlightProfileCheck("Baseline flight telemetry", hasTelemetry, true,
                hasTelemetry ? "Ground speed and on-ground status are available." : "Required standard flight datarefs are unavailable."),
            new SharedFlightProfileCheck("Cabin sign signal", hasSeatbelts, true,
                hasSeatbelts ? "A seat-belt or passenger-sign signal was found." : "No safe cabin-sign dataref was found."),
            new SharedFlightProfileCheck("Door signal", hasDoors, true,
                hasDoors ? "At least one aircraft door signal was found." : "No safe aircraft door dataref was found."),
            new SharedFlightProfileCheck("Native jetway", nativeJetwayCommandAvailable, false,
                nativeJetwayCommandAvailable ? "X-Plane's native jetway command is available." : "Jetway support was not exposed; this does not block profile testing.")
        };
        var requiredPassed = checks.Where(check => check.Required).All(check => check.Passed);
        var state = requiredPassed
            ? SharedFlightProfileState.ReadyForGuidedValidation
            : SharedFlightProfileState.NeedsGuidedValidation;
        var summary = requiredPassed
            ? "This aircraft is ready for the guided CrewLink validation. Its result does not enable generic cockpit or FMS control."
            : "One or more required safe signals are missing. Ember will keep shared aircraft control disabled for this aircraft.";

        return new SharedFlightProfileReport(
            "X-Plane 12",
            simulatorVersion,
            aircraftIcao,
            aircraftDescription,
            aircraftRelativePath,
            profile.Id,
            profile.Name,
            state,
            summary,
            checks,
            now);
    }

    private static bool Has(string text, string value) => text.Contains(value, StringComparison.OrdinalIgnoreCase);

    private static bool KeysAnyContains(this IEnumerable<string> names, params string[] fragments) =>
        names.Any(name => fragments.Any(fragment => name.Contains(fragment, StringComparison.OrdinalIgnoreCase)));
}
