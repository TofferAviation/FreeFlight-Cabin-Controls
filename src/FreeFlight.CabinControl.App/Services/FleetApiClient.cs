using System.Net.Http.Headers;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using FreeFlight.CabinControl.Core.Configuration;

namespace FreeFlight.CabinControl.App.Services;

public sealed class FleetApiClient(HttpClient? httpClient = null) : IDisposable
{
    private readonly HttpClient _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
    private readonly bool _ownsHttpClient = httpClient is null;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly JsonSerializerOptions JsonWriteOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public async Task<IReadOnlyList<FleetAircraftSummaryDto>> GetAircraftAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        var payload = await GetAsync<FleetAircraftEnvelope>(settings, "/api/fleet/v1/aircraft", cancellationToken);
        return payload?.Aircraft ?? [];
    }

    public async Task<FleetAircraftRecordDto> GetAircraftRecordAsync(
        AppSettings settings,
        string aircraftId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(aircraftId);
        var payload = await GetAsync<FleetAircraftRecordEnvelope>(
            settings,
            $"/api/fleet/v1/aircraft/{Uri.EscapeDataString(aircraftId)}",
            cancellationToken);
        return payload?.Aircraft ?? throw new FleetApiException("The Fleet API returned an empty aircraft record.");
    }

    public async Task<FleetDefectDto> ReportDefectAsync(
        AppSettings settings,
        string aircraftId,
        FleetDefectSubmissionDto defect,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(aircraftId);
        var payload = await SendAsync<FleetDefectEnvelope>(
            settings,
            HttpMethod.Post,
            $"/api/fleet/v1/aircraft/{Uri.EscapeDataString(aircraftId)}/defects",
            new { defect },
            cancellationToken);
        return payload?.Defect ?? throw new FleetApiException("The Fleet API did not confirm the defect report.");
    }

    private async Task<T?> GetAsync<T>(AppSettings settings, string route, CancellationToken cancellationToken)
    {
        return await SendAsync<T>(settings, HttpMethod.Get, route, null, cancellationToken);
    }

    private async Task<T?> SendAsync<T>(
        AppSettings settings,
        HttpMethod method,
        string route,
        object? payload,
        CancellationToken cancellationToken)
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

        using var request = new HttpRequestMessage(method, new Uri(baseUri, route));
        request.Headers.Add("X-FreeFlight-Fleet-Key", settings.FleetApiAccessKey.Trim());
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (payload is not null)
        {
            request.Content = new StringContent(JsonSerializer.Serialize(payload, JsonWriteOptions), Encoding.UTF8, "application/json");
        }
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new FleetApiException($"Fleet API returned {(int)response.StatusCode}: {ExtractError(content)}");
        }

        return JsonSerializer.Deserialize<T>(content, JsonOptions);
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
public sealed record FleetAircraftRecordEnvelope(FleetAircraftRecordDto? Aircraft);
public sealed record FleetDefectEnvelope(FleetDefectDto? Defect);
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
public sealed record FleetAvailabilityDto(string DispatchStatus, bool Available, IReadOnlyList<string>? Reasons);
public sealed record FleetDefectDto(string Reference, string? ReportingStation, string Category, string? SeatNumber, string Description, string Severity, string DispatchImpact, string Status);
public sealed record FleetDefectSubmissionDto(
    string Category,
    string Description,
    string Severity,
    string DispatchImpact,
    string? Station,
    string? SeatNumber,
    string Source = "cabin_crew");
public sealed record FleetMaintenanceDueDto(string TaskCode, string TaskName, string? DueDate, long? DueHoursMinutes, long? DueCycles, string DueStatus, string? DueReason);
public sealed record FleetStatusHistoryDto(string OperationalStatus, string TechnicalStatus, string DispatchStatus, string Reason, string? Remarks, string? Station, string EffectiveAt, string Source);
public sealed record FleetLogEntryDto(string Reference, string OccurredAt, string? Station, string Category, string Description, string Status);
public sealed record FleetAircraftRecordDto(
    string Id,
    string Registration,
    string AircraftModel,
    string? Variant,
    string? CurrentStation,
    string OperationalStatus,
    string TechnicalStatus,
    string DispatchStatus,
    long AirframeHoursMinutes,
    long AirframeCycles,
    string? FleetNumber,
    string? Msn,
    string? HomeBase,
    string? CurrentLivery,
    FleetAvailabilityDto? Availability,
    IReadOnlyList<FleetDefectDto>? Defects,
    IReadOnlyList<FleetMaintenanceDueDto>? MaintenanceDue,
    IReadOnlyList<FleetStatusHistoryDto>? StatusHistory,
    IReadOnlyList<FleetLogEntryDto>? Logbook);
