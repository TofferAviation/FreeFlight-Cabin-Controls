using System.Globalization;
using System.Net.Http;
using System.Text.Json;

namespace FreeFlight.CabinControl.App.Services;

public sealed record SimBriefFlightSummary(
    int PassengerCount,
    string FlightNumber,
    string Origin,
    string Destination,
    string AircraftIcao,
    DateTimeOffset? ScheduledDepartureUtc,
    DateTimeOffset? GeneratedAtUtc,
    DateTimeOffset? EstimatedArrivalUtc = null);

public interface ISimBriefClient
{
    Task<SimBriefFlightSummary> FetchLatestOfpAsync(
        string pilotId,
        CancellationToken cancellationToken = default);
}

public sealed class SimBriefClient : ISimBriefClient
{
    private static readonly HttpClient HttpClient = new()
    {
        BaseAddress = new Uri("https://www.simbrief.com/"),
        Timeout = TimeSpan.FromSeconds(15)
    };

    public async Task<SimBriefFlightSummary> FetchLatestOfpAsync(
        string pilotId,
        CancellationToken cancellationToken = default)
    {
        var normalizedPilotId = pilotId.Trim();
        if (normalizedPilotId.Length == 0 || !normalizedPilotId.All(char.IsDigit))
        {
            throw new ArgumentException("Enter the numeric Pilot ID shown in SimBrief Account Settings.", nameof(pilotId));
        }

        var endpoint = $"api/xml.fetcher.php?userid={Uri.EscapeDataString(normalizedPilotId)}&json=1";
        using var response = await HttpClient.GetAsync(endpoint, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                response.StatusCode == System.Net.HttpStatusCode.BadRequest
                    ? "SimBrief could not find a generated OFP for that Pilot ID."
                    : $"SimBrief returned HTTP {(int)response.StatusCode}.");
        }

        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(responseStream, cancellationToken: cancellationToken);
        var root = document.RootElement;
        var passengerCount = ReadRequiredInt(root, "weights", "pax_count");
        var airline = ReadString(root, "general", "icao_airline");
        var flightNumber = ReadString(root, "general", "flight_number");
        var flightLabel = string.Concat(airline, flightNumber);
        if (string.IsNullOrWhiteSpace(flightLabel))
        {
            flightLabel = "Latest OFP";
        }

        return new SimBriefFlightSummary(
            passengerCount,
            flightLabel,
            ReadString(root, "origin", "icao_code"),
            ReadString(root, "destination", "icao_code"),
            ReadAircraftIcao(root),
            ReadUnixTimestamp(root, "times", "est_out") ??
            ReadUnixTimestamp(root, "times", "sched_out"),
            ReadUnixTimestamp(root, "params", "time_generated"),
            ReadUnixTimestamp(root, "times", "est_in") ??
            ReadUnixTimestamp(root, "times", "sched_in"));
    }

    private static string ReadAircraftIcao(JsonElement root)
    {
        foreach (var candidate in new[]
                 {
                     ReadString(root, "aircraft", "icaocode"),
                     ReadString(root, "aircraft", "icao_code"),
                     ReadString(root, "general", "icao_aircraft")
                 })
        {
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                return candidate.Trim().ToUpperInvariant();
            }
        }

        return string.Empty;
    }

    private static int ReadRequiredInt(JsonElement root, string sectionName, string propertyName)
    {
        var text = ReadString(root, sectionName, propertyName);
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) || value < 0)
        {
            throw new InvalidOperationException("The latest SimBrief OFP did not contain a valid passenger count.");
        }

        return value;
    }

    private static string ReadString(JsonElement root, string sectionName, string propertyName)
    {
        if (!root.TryGetProperty(sectionName, out var section) ||
            !section.TryGetProperty(propertyName, out var property))
        {
            return string.Empty;
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString()?.Trim() ?? string.Empty,
            JsonValueKind.Number => property.GetRawText(),
            _ => string.Empty
        };
    }

    private static DateTimeOffset? ReadUnixTimestamp(
        JsonElement root,
        string sectionName,
        string propertyName)
    {
        var text = ReadString(root, sectionName, propertyName);
        return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unixSeconds)
            ? DateTimeOffset.FromUnixTimeSeconds(unixSeconds)
            : null;
    }
}
