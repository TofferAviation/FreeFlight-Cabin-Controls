using System.Net.Http.Headers;
using System.Net.Http;
using System.Text.Json;
using FreeFlight.CabinControl.Core.Configuration;

namespace FreeFlight.CabinControl.App.Services;

public sealed class FleetApiClient(HttpClient? httpClient = null) : IDisposable
{
    private readonly HttpClient _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
    private readonly bool _ownsHttpClient = httpClient is null;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<IReadOnlyList<FleetAircraftSummaryDto>> GetAircraftAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        var baseUrl = settings.FleetApiBaseUrl.Trim().TrimEnd('/');
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri) || baseUri.Scheme is not ("https" or "http"))
        {
            throw new FleetApiException("Enter the Fleet API website address in Settings before synchronizing.");
        }
        if (string.IsNullOrWhiteSpace(settings.FleetApiAccessKey))
        {
            throw new FleetApiException("Enter the Fleet device access key in Settings before synchronizing.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(baseUri, "/api/fleet/v1/aircraft"));
        request.Headers.Add("X-FreeFlight-Fleet-Key", settings.FleetApiAccessKey.Trim());
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new FleetApiException($"Fleet API returned {(int)response.StatusCode}: {ExtractError(content)}");
        }
        var payload = JsonSerializer.Deserialize<FleetAircraftEnvelope>(content, JsonOptions);
        return payload?.Aircraft ?? [];
    }

    private static string ExtractError(string json)
    {
        try { return JsonSerializer.Deserialize<FleetErrorEnvelope>(json, JsonOptions)?.Error ?? "Request failed."; }
        catch (JsonException) { return "Request failed."; }
    }

    public void Dispose()
    {
        if (_ownsHttpClient) _httpClient.Dispose();
    }
}

public sealed class FleetApiException(string message) : Exception(message);

public sealed record FleetAircraftEnvelope(IReadOnlyList<FleetAircraftSummaryDto>? Aircraft);
public sealed record FleetErrorEnvelope(string? Error);
public sealed record FleetAircraftSummaryDto(
    string Id,
    string Registration,
    string AircraftModel,
    string? Variant,
    string? IcaoType,
    string? Subfleet,
    string? CurrentStation,
    string OperationalStatus,
    string TechnicalStatus,
    string DispatchStatus,
    long AirframeHoursMinutes,
    long AirframeCycles,
    string? LastFlightAt,
    string? NextAssignedFlightReference,
    long StatusVersion);
