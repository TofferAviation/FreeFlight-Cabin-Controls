using System.Globalization;
using System.Text.RegularExpressions;

namespace FreeFlight.CabinControl.Core.Operations;

/// <summary>
/// Converts only clear, equipment-related SayIntentions cabin dialogue into a
/// fleet-defect proposal. Passenger service and medical scenarios deliberately
/// do not become aircraft technical records.
/// </summary>
public static class SayIntentionsCabinEventClassifier
{
    private static readonly string[] FaultSignals =
    [
        "inoperative", "not working", "malfunction", "malfunctioning", "failed",
        "failure", "fault", "faulty", "broken", "out of service", "leak",
        "leaking", "smoke", "smoky", "fumes", "burning", "overheating", "overheated"
    ];

    private static readonly string[] ClearSignals =
    [
        "no fault", "no issue", "working normally", "fully operational", "has been fixed",
        "is fixed", "has been repaired", "is repaired"
    ];

    private static readonly CabinRule[] Rules =
    [
        new("Lavatory", ["lavatory", "toilet", "washroom"], "Lavatory"),
        new("Galley", ["galley", "oven", "coffee maker", "coffee machine", "chiller"], "Galley"),
        new("Passenger seat", ["seat", "armrest", "tray table", "seat belt"], "Passenger seat"),
        new("IFE", ["ife", "entertainment screen", "seat screen", "video screen"], "IFE"),
        new("USB / power", ["usb", "power outlet", "charging port", "in-seat power"], "USB / power"),
        new("PSU / lighting", ["reading light", "cabin light", "psu", "air vent", "call bell"], "PSU / lighting"),
        new("PA / interphone", ["interphone", "public address", "pa system", "cabin handset"], "PA / interphone"),
        new("Cabin door", ["cabin door", "passenger door", "door slide", "door seal"], "Cabin door"),
        new("Other", ["cabin temperature", "air conditioning", "pressurisation", "pressurization"], "Cabin systems")
    ];

    public static bool TryCreateProposal(
        string eventId,
        DateTimeOffset occurredAt,
        string transcript,
        out SayIntentionsFleetDefectProposal? proposal)
    {
        proposal = null;
        var normalized = Normalize(transcript);
        if (normalized.Length < 3 || ContainsAny(normalized, ClearSignals) || !ContainsAny(normalized, FaultSignals))
        {
            return false;
        }

        var rule = Rules.FirstOrDefault(candidate => ContainsAny(normalized, candidate.Keywords));
        if (rule is null)
        {
            return false;
        }

        var severe = ContainsAny(normalized, ["smoke", "smoky", "fumes", "burning", "leak", "leaking", "overheating", "overheated", "cabin door", "door slide"]);
        proposal = new SayIntentionsFleetDefectProposal(
            eventId,
            rule.Category,
            $"SayIntentions cabin event at {occurredAt.UtcDateTime.ToString("HH:mm 'UTC'", CultureInfo.InvariantCulture)}: {Truncate(normalized, 420)}",
            severe ? "high" : "normal",
            severe ? "restriction" : "none",
            rule.Location);
        return true;
    }

    private static bool ContainsAny(string value, IEnumerable<string> candidates) =>
        candidates.Any(candidate => value.Contains(candidate, StringComparison.OrdinalIgnoreCase));

    private static string Normalize(string value) =>
        Regex.Replace(value.Trim(), @"\s+", " ");

    private static string Truncate(string value, int maximumLength) =>
        value.Length <= maximumLength ? value : $"{value[..(maximumLength - 1)].TrimEnd()}…";

    private sealed record CabinRule(string Category, string[] Keywords, string Location);
}

public sealed record SayIntentionsFleetDefectProposal(
    string EventId,
    string Category,
    string Description,
    string Severity,
    string DispatchImpact,
    string? Location);
