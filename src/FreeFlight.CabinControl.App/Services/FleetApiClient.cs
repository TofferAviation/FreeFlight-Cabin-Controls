using System.Net.Http.Headers;
using System.Net.Http;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FreeFlight.CabinControl.Core.Configuration;

namespace FreeFlight.CabinControl.App.Services;

public sealed class FleetApiClient(HttpClient? httpClient = null) : IDisposable
{
    private readonly HttpClient _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
    private readonly bool _ownsHttpClient = httpClient is null;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly JsonSerializerOptions JsonWriteOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public async Task<IReadOnlyList<FleetAircraftSummaryDto>> GetAircraftAsync(
        AppSettings settings,
        FleetAccountSession account,
        CancellationToken cancellationToken = default)
    {
        var payload = await SendBavAsync<FleetAircraftEnvelope>(settings, account, HttpMethod.Get, "/api/fleet/v1/aircraft", null, cancellationToken);
        return payload?.Aircraft ?? [];
    }

    public async Task<FleetAircraftRecordDto> GetAircraftRecordAsync(
        AppSettings settings,
        FleetAccountSession account,
        string aircraftId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(aircraftId);
        var payload = await SendBavAsync<FleetAircraftRecordEnvelope>(
            settings,
            account,
            HttpMethod.Get,
            $"/api/fleet/v1/aircraft/{Uri.EscapeDataString(aircraftId)}",
            null,
            cancellationToken);
        return payload?.Aircraft ?? throw new FleetApiException("The Fleet API returned an empty aircraft record.");
    }

    public async Task<FleetDefectDto> ReportDefectAsync(
        AppSettings settings,
        FleetAccountSession account,
        string aircraftId,
        FleetDefectSubmissionDto defect,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(aircraftId);
        var payload = await SendBavAsync<FleetDefectEnvelope>(
            settings,
            account,
            HttpMethod.Post,
            $"/api/fleet/v1/aircraft/{Uri.EscapeDataString(aircraftId)}/defects",
            new { defect },
            cancellationToken);
        return payload?.Defect ?? throw new FleetApiException("The Fleet API did not confirm the defect report.");
    }

    public async Task<FleetLandingAssessmentDto> RecordHardLandingAssessmentAsync(
        AppSettings settings,
        FleetAccountSession account,
        string aircraftId,
        FleetLandingAssessmentSubmissionDto landing,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(aircraftId);
        var payload = await SendBavAsync<FleetLandingAssessmentEnvelope>(
            settings,
            account,
            HttpMethod.Post,
            $"/api/fleet/v1/aircraft/{Uri.EscapeDataString(aircraftId)}/landing-assessment",
            new { landing },
            cancellationToken);
        return payload?.Assessment ?? throw new FleetApiException("The Fleet API did not confirm the hard-landing assessment.");
    }

    public async Task<FleetAccountSession> SignInAsync(
        AppSettings settings,
        string email,
        string password,
        bool rememberDevice,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            throw new FleetApiException("Enter your British Airways Virtual email address and password.");
        }

        var baseUri = ResolveBaseUri(settings);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(baseUri, "/api/acars/v1/auth"))
        {
            Content = new StringContent(JsonSerializer.Serialize(new { email = email.Trim(), password, rememberDevice }, JsonWriteOptions), Encoding.UTF8, "application/json")
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new FleetApiException($"British Airways Virtual sign-in failed: {ExtractError(content)}", response.StatusCode);
        }

        var payload = JsonSerializer.Deserialize<FleetAccountEnvelope>(content, JsonOptions)
                      ?? throw new FleetApiException("British Airways Virtual returned an empty sign-in response.");
        if (string.IsNullOrWhiteSpace(payload.Token) || payload.Pilot is null)
        {
            throw new FleetApiException("British Airways Virtual did not return a usable account session.");
        }

        return new FleetAccountSession(payload.Token, payload.Pilot.Id, payload.Pilot.PilotNumber, payload.Pilot.Name, payload.Pilot.Email, payload.Pilot.ProfileImage, payload.DeviceSessionToken, payload.Pilot.Rank);
    }

    public async Task<FleetAccountPilotDto> GetAccountProfileAsync(
        AppSettings settings,
        FleetAccountSession account,
        CancellationToken cancellationToken = default)
    {
        var payload = await SendBavAsync<FleetAccountProfileEnvelope>(
            settings,
            account,
            HttpMethod.Get,
            "/api/acars/v1/profile",
            null,
            cancellationToken);
        return payload?.Pilot ?? throw new FleetApiException("British Airways Virtual did not return your account profile.");
    }

    /// <summary>
    /// Rotates a revocable BAV device credential without ever sending or
    /// storing the pilot's website password again.
    /// </summary>
    public async Task<FleetAccountSession> RefreshAccountSessionAsync(
        AppSettings settings,
        FleetAccountSession account,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(account.DeviceSessionToken))
        {
            throw new FleetApiException("This BAV session is not saved on this Windows PC.");
        }

        var baseUri = ResolveBaseUri(settings);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(baseUri, "/api/acars/v1/auth/refresh"))
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { deviceSessionToken = account.DeviceSessionToken }, JsonWriteOptions),
                Encoding.UTF8,
                "application/json")
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new FleetApiException($"British Airways Virtual could not restore your sign-in: {ExtractError(content)}", response.StatusCode);
        }

        var payload = JsonSerializer.Deserialize<FleetAccountEnvelope>(content, JsonOptions)
                      ?? throw new FleetApiException("British Airways Virtual returned an empty restored session.");
        if (string.IsNullOrWhiteSpace(payload.Token) || payload.Pilot is null)
        {
            throw new FleetApiException("British Airways Virtual did not return a usable restored session.");
        }

        if (string.IsNullOrWhiteSpace(payload.DeviceSessionToken))
        {
            throw new FleetApiException("British Airways Virtual did not return a usable device session.");
        }

        return new FleetAccountSession(payload.Token, payload.Pilot.Id, payload.Pilot.PilotNumber, payload.Pilot.Name, payload.Pilot.Email, payload.Pilot.ProfileImage, payload.DeviceSessionToken, payload.Pilot.Rank);
    }

    public async Task RevokeAccountSessionAsync(
        AppSettings settings,
        FleetAccountSession account,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(account.DeviceSessionToken)) return;
        var baseUri = ResolveBaseUri(settings);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(baseUri, "/api/acars/v1/auth/logout"))
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { deviceSessionToken = account.DeviceSessionToken }, JsonWriteOptions),
                Encoding.UTF8,
                "application/json")
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new FleetApiException($"British Airways Virtual could not sign this device out: {ExtractError(content)}", response.StatusCode);
        }
    }

    public async Task<FleetWebsiteFlightAssignmentDto?> GetWebsiteFlightAssignmentAsync(
        AppSettings settings,
        FleetAccountSession account,
        CancellationToken cancellationToken = default)
    {
        var baseUri = ResolveBaseUri(settings);
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(baseUri, "/api/acars/v1/assignment"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", account.Token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new FleetApiException($"Could not load your selected BAV flight: {ExtractError(content)}", response.StatusCode);
        }

        return JsonSerializer.Deserialize<FleetWebsiteFlightAssignmentEnvelope>(content, JsonOptions)?.Assignment;
    }

    public async Task<IReadOnlyList<FleetOperationsFlightDto>> GetOperationsFlightsAsync(
        AppSettings settings,
        FleetAccountSession account,
        CancellationToken cancellationToken = default)
    {
        var payload = await SendBavAsync<FleetOperationsFlightsEnvelope>(
            settings,
            account,
            HttpMethod.Get,
            "/api/acars/v1/operations/flights",
            null,
            cancellationToken);
        return payload?.Flights ?? [];
    }

    public async Task<FleetAcarsSessionDto> StartAcarsSessionAsync(
        AppSettings settings,
        FleetAccountSession account,
        string simulator,
        CancellationToken cancellationToken = default)
    {
        var payload = await SendBavAsync<FleetAcarsSessionEnvelope>(
            settings,
            account,
            HttpMethod.Post,
            "/api/acars/v1/sessions/start",
            new { simulator },
            cancellationToken);
        return payload?.Session ?? throw new FleetApiException("British Airways Virtual did not confirm the ACARS session.");
    }

    public async Task<FleetAcarsSessionDto?> GetActiveAcarsSessionAsync(
        AppSettings settings,
        FleetAccountSession account,
        CancellationToken cancellationToken = default)
    {
        var payload = await SendBavAsync<FleetAcarsSessionEnvelope>(
            settings,
            account,
            HttpMethod.Get,
            "/api/acars/v1/sessions/active",
            null,
            cancellationToken);
        return payload?.Session;
    }

    public async Task SendAcarsTelemetryAsync(
        AppSettings settings,
        FleetAccountSession account,
        string sessionId,
        FleetAcarsTelemetryDto telemetry,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        _ = await SendBavAsync<FleetAcarsTelemetryEnvelope>(
            settings,
            account,
            HttpMethod.Post,
            $"/api/acars/v1/sessions/{Uri.EscapeDataString(sessionId)}/telemetry",
            telemetry,
            cancellationToken);
    }

    public async Task<FleetAcarsCompletionDto> CompleteAcarsSessionAsync(
        AppSettings settings,
        FleetAccountSession account,
        string sessionId,
        int? landingFpm,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        var payload = await SendBavAsync<FleetAcarsCompleteEnvelope>(
            settings,
            account,
            HttpMethod.Post,
            $"/api/acars/v1/sessions/{Uri.EscapeDataString(sessionId)}/end",
            new { landingFpm },
            cancellationToken);
        if (payload?.Session is null || payload.Pirep is null)
        {
            throw new FleetApiException("British Airways Virtual did not confirm the completed ACARS flight.");
        }

        return new FleetAcarsCompletionDto(payload.Session, payload.Pirep);
    }

    public async Task<FleetFlightAssignmentDto> ReserveAircraftForFlightAsync(
        AppSettings settings,
        FleetAccountSession account,
        string aircraftId,
        FleetFlightAssignmentSubmissionDto assignment,
        CancellationToken cancellationToken = default) =>
        await SendFlightAssignmentAsync(settings, account, HttpMethod.Post, $"/api/fleet/v1/aircraft/{Uri.EscapeDataString(aircraftId)}/flight-assignment", assignment, cancellationToken);

    public async Task<FleetFlightAssignmentDto?> GetActiveFlightAssignmentAsync(
        AppSettings settings,
        FleetAccountSession account,
        CancellationToken cancellationToken = default)
    {
        var payload = await SendBavAsync<FleetFlightAssignmentEnvelope>(
            settings,
            account,
            HttpMethod.Get,
            "/api/fleet/v1/flight-assignment",
            null,
            cancellationToken);
        return payload?.Assignment;
    }

    public async Task<FleetFlightAssignmentDto> StartAircraftFlightAsync(
        AppSettings settings,
        FleetAccountSession account,
        string aircraftId,
        FleetFlightAssignmentSubmissionDto assignment,
        CancellationToken cancellationToken = default) =>
        await SendFlightAssignmentAsync(settings, account, HttpMethod.Post, $"/api/fleet/v1/aircraft/{Uri.EscapeDataString(aircraftId)}/flight-assignment/start", assignment, cancellationToken);

    public async Task<FleetFlightAssignmentDto> CompleteAircraftFlightAsync(
        AppSettings settings,
        FleetAccountSession account,
        string aircraftId,
        FleetFlightAssignmentSubmissionDto assignment,
        CancellationToken cancellationToken = default) =>
        await SendFlightAssignmentAsync(settings, account, HttpMethod.Post, $"/api/fleet/v1/aircraft/{Uri.EscapeDataString(aircraftId)}/flight-assignment/complete", assignment, cancellationToken);

    public async Task<FleetFlightAssignmentDto> CancelAircraftReservationAsync(
        AppSettings settings,
        FleetAccountSession account,
        string aircraftId,
        FleetFlightAssignmentSubmissionDto assignment,
        CancellationToken cancellationToken = default) =>
        await SendFlightAssignmentAsync(settings, account, HttpMethod.Delete, $"/api/fleet/v1/aircraft/{Uri.EscapeDataString(aircraftId)}/flight-assignment", assignment, cancellationToken);

    private async Task<FleetFlightAssignmentDto> SendFlightAssignmentAsync(
        AppSettings settings,
        FleetAccountSession account,
        HttpMethod method,
        string route,
        FleetFlightAssignmentSubmissionDto assignment,
        CancellationToken cancellationToken)
    {
        var baseUri = ResolveBaseUri(settings);
        using var request = new HttpRequestMessage(method, new Uri(baseUri, route));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", account.Token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = new StringContent(JsonSerializer.Serialize(new { assignment }, JsonWriteOptions), Encoding.UTF8, "application/json");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new FleetApiException($"Fleet aircraft selection failed: {ExtractError(content)}", response.StatusCode);
        }

        var payload = JsonSerializer.Deserialize<FleetFlightAssignmentEnvelope>(content, JsonOptions);
        return payload?.Assignment ?? throw new FleetApiException("The Fleet API did not confirm the aircraft assignment.");
    }

    private async Task<T?> SendBavAsync<T>(
        AppSettings settings,
        FleetAccountSession account,
        HttpMethod method,
        string route,
        object? payload,
        CancellationToken cancellationToken)
    {
        var baseUri = ResolveBaseUri(settings);
        using var request = new HttpRequestMessage(method, new Uri(baseUri, route));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", account.Token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (payload is not null)
        {
            request.Content = new StringContent(JsonSerializer.Serialize(payload, JsonWriteOptions), Encoding.UTF8, "application/json");
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new FleetApiException($"British Airways Virtual ACARS request failed: {ExtractError(content)}", response.StatusCode);
        }

        return JsonSerializer.Deserialize<T>(content, JsonOptions);
    }

    private static Uri ResolveBaseUri(AppSettings settings)
    {
        // The BAV app is intentionally a production client, not a local API
        // browser. Keep the in-memory value synchronized for old settings
        // files, then use the bundled HTTPS origin for every request.
        settings.FleetApiBaseUrl = AppSettings.BritishAirwaysVirtualWebsiteUrl;
        return new Uri(AppSettings.BritishAirwaysVirtualWebsiteUrl, UriKind.Absolute);
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

public sealed class FleetApiException(string message, HttpStatusCode? statusCode = null) : Exception(message)
{
    public HttpStatusCode? StatusCode { get; } = statusCode;
}

public sealed record FleetAircraftEnvelope(IReadOnlyList<FleetAircraftSummaryDto>? Aircraft);
public sealed record FleetErrorEnvelope(string? Error);
public sealed record FleetAircraftRecordEnvelope(FleetAircraftRecordDto? Aircraft);
public sealed record FleetDefectEnvelope(FleetDefectDto? Defect);
public sealed record FleetLandingAssessmentEnvelope(FleetLandingAssessmentDto? Assessment);
public sealed record FleetAccountEnvelope(string? Token, FleetAccountPilotDto? Pilot, long? ExpiresInSeconds, string? DeviceSessionToken = null);
public sealed record FleetAccountProfileEnvelope(FleetAccountPilotDto? Pilot);
public sealed record FleetAccountPilotDto(string Id, string PilotNumber, string Name, string Email, string? ProfileImage = null, string? Rank = null);
public sealed record FleetAccountSession(
    string Token,
    string PilotId,
    string PilotNumber,
    string Name,
    string Email,
    string? ProfileImage = null,
    string? DeviceSessionToken = null,
    string? Rank = null);
public sealed record FleetWebsiteFlightAssignmentEnvelope(FleetWebsiteFlightAssignmentDto? Assignment);
public sealed record FleetWebsiteFlightAssignmentDto(
    string Id,
    string FlightNumber,
    string From,
    string To,
    string Aircraft,
    string Departure,
    string Arrival,
    string Date,
    string Status,
    string? OriginIcao = null,
    string? DestinationIcao = null);
public sealed record FleetOperationsFlightsEnvelope(IReadOnlyList<FleetOperationsFlightDto>? Flights);
public sealed record FleetOperationsFlightDto(
    string Id,
    string FlightNumber,
    string? OriginIcao,
    string? DestinationIcao,
    string Aircraft,
    string Departure,
    string Arrival,
    string Date,
    string Status,
    bool IsCurrentPilot);
public sealed record FleetFlightAssignmentEnvelope(FleetFlightAssignmentDto? Assignment);
public sealed record FleetFlightAssignmentSubmissionDto(string FlightReference, string? DepartureStation, string? ArrivalStation);
public sealed record FleetFlightAssignmentDto(string Id, string AircraftId, string PilotSubject, string PilotDisplayName, string FlightReference, string? DepartureStation, string? ArrivalStation, string Status, string ReservedAt, string? OffBlockAt, string? OnBlockAt, int? BlockMinutes);
public sealed record FleetAcarsSessionEnvelope(FleetAcarsSessionDto? Session);
public sealed record FleetAcarsTelemetryEnvelope(bool Ok, string? UpdatedAt, double? DistanceNm);
public sealed record FleetAcarsCompleteEnvelope(FleetAcarsSessionDto? Session, FleetPirepDto? Pirep);
public sealed record FleetAcarsSessionDto(string Id, string FlightNumber, string From, string To, string Aircraft, string Simulator, string Status, string StartedAt, FleetAcarsSnapshotDto? LastSnapshot = null);
public sealed record FleetAcarsCompletionDto(FleetAcarsSessionDto Session, FleetPirepDto Pirep);
public sealed record FleetAcarsSnapshotDto(bool FlightStarted);
public sealed record FleetPirepDto(string Id, string FlightNumber, string From, string To, string Aircraft, int BlockMinutes, int DistanceNm, int? LandingFpm, int? FuelUsedKg, string Status, string Source, string Simulator);
public sealed record FleetAcarsTelemetryDto(
    double Latitude,
    double Longitude,
    double AltitudeFt,
    double GroundSpeedKt,
    double HeadingDeg,
    double? IndicatedAirspeedKt,
    string? Squawk,
    bool BeaconOn,
    double? FuelKg,
    bool EnginesRunning,
    bool ParkingBrakeSet,
    bool OnGround,
    double? VerticalSpeedFpm,
    bool FlightStarted,
    string? Registration);
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
    long StatusVersion,
    FleetAircraftImageDto? Image);
public sealed record FleetAircraftImageDto(string Url, string Source, string? Credit, string? SourcePageUrl);
public sealed record FleetAvailabilityDto(string DispatchStatus, bool Available, IReadOnlyList<string>? Reasons);
// The Fleet API uses snake_case for its technical-record fields.  These names
// must be explicit: case-insensitive JSON matching does not translate '_' to
// PascalCase, which otherwise makes a deferred defect look like an empty one.
public sealed record FleetDeferralDto(
    [property: JsonPropertyName("deferral_kind")] string DeferralKind,
    [property: JsonPropertyName("reference")] string Reference,
    [property: JsonPropertyName("restriction")] string Restriction,
    [property: JsonPropertyName("operational_procedure")] string? OperationalProcedure,
    [property: JsonPropertyName("maintenance_procedure")] string? MaintenanceProcedure,
    [property: JsonPropertyName("due_at")] string? DueAt,
    [property: JsonPropertyName("due_cycles")] long? DueCycles,
    [property: JsonPropertyName("due_hours_minutes")] long? DueHoursMinutes);

public sealed record FleetDefectDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("reference")] string Reference,
    [property: JsonPropertyName("reporting_station")] string? ReportingStation,
    [property: JsonPropertyName("category")] string Category,
    [property: JsonPropertyName("seat_number")] string? SeatNumber,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("severity")] string Severity,
    [property: JsonPropertyName("dispatch_impact")] string DispatchImpact,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("version")] long Version,
    [property: JsonPropertyName("deferral")] FleetDeferralDto? Deferral);
public sealed record FleetDefectSubmissionDto(
    string Category,
    string Description,
    string Severity,
    string DispatchImpact,
    string? Station,
    string? SeatNumber,
    string Source = "cabin_crew");
public sealed record FleetLandingAssessmentSubmissionDto(int LandingFpm, string? Station);
public sealed record FleetLandingAssessmentDto(string Outcome, int LandingFpm, string Registration, string Reference);
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
    FleetAircraftImageDto? Image,
    FleetAvailabilityDto? Availability,
    IReadOnlyList<FleetDefectDto>? Defects,
    IReadOnlyList<FleetMaintenanceDueDto>? MaintenanceDue,
    IReadOnlyList<FleetStatusHistoryDto>? StatusHistory,
    IReadOnlyList<FleetLogEntryDto>? Logbook);
