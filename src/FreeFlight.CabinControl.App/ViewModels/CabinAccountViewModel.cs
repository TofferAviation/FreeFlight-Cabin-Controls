using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Input;
using FreeFlight.CabinControl.App.Infrastructure;
using FreeFlight.CabinControl.App.Services;
using FreeFlight.CabinControl.Core.Configuration;

namespace FreeFlight.CabinControl.App.ViewModels;

/// <summary>
    /// The Ember account is a short-lived session authenticated by the
/// British Airways Virtual website. Only the resulting session token is kept
/// in memory; the website password is cleared immediately after sign-in.
/// </summary>
public sealed class CabinAccountViewModel : PageViewModel, IDisposable
{
    private readonly AppSettings _settings;
    private readonly FleetApiClient _apiClient;
    private readonly BavAccountSessionStore? _sessionStore;
    private string _email = string.Empty;
    private string _password = string.Empty;
    private FleetAccountSession? _session;
    private FleetWebsiteFlightAssignmentDto? _websiteFlightAssignment;
    private IReadOnlyList<FleetOperationsFlightDto> _operationsFlights = [];
    private FleetAcarsSessionDto? _activeAcarsSession;
    private string _flightPlanLinkLabel = "Choose a BAV flight, then import SimBrief to verify the route.";
    private string _flightPlanLinkDetail = "Ember will prevent an aircraft lifecycle from starting when the two flights disagree.";
    private bool _hasSimBriefFlightPlan;
    private bool _isFlightPlanLinked;
    private string _acarsSessionLabel = "No active ACARS flight";
    private string _lastFlightCompletionLabel = "No ACARS flight has been completed in this Ember session.";
    private string _statusMessage = "Sign in with your British Airways Virtual website account to reserve an aircraft for a flight.";
    private bool _isBusy;
    private bool _rememberSignIn = true;
    private ImageSource? _profileImageSource;

    public CabinAccountViewModel(AppSettings settings, FleetApiClient apiClient, BavAccountSessionStore? sessionStore = null)
        : base("BAV Account", "Your British Airways Virtual identity for Fleet operations")
    {
        _settings = settings;
        _apiClient = apiClient;
        _sessionStore = sessionStore;
        SignInCommand = new AsyncRelayCommand(SignInAsync, exception => StatusMessage = exception.Message);
        SignOutCommand = new AsyncRelayCommand(SignOutAsync, exception => StatusMessage = exception.Message);
        RefreshWebsiteFlightCommand = new AsyncRelayCommand(RefreshWebsiteFlightAsync, exception => StatusMessage = exception.Message);
    }

    public event EventHandler? SessionChanged;
    public event EventHandler<WebsiteFlightAssignmentRefreshedEventArgs>? WebsiteFlightAssignmentRefreshed;
    public event EventHandler? OperationsFlightsRefreshed;

    public ICommand SignInCommand { get; }
    public ICommand SignOutCommand { get; }
    public ICommand RefreshWebsiteFlightCommand { get; }

    public string Email
    {
        get => _email;
        set => SetProperty(ref _email, value);
    }

    public string Password
    {
        get => _password;
        set => SetProperty(ref _password, value);
    }

    public FleetAccountSession? Session
    {
        get => _session;
        private set
        {
            if (!SetProperty(ref _session, value)) return;
            OnPropertyChanged(nameof(IsAuthenticated));
            OnPropertyChanged(nameof(DisplayName));
            OnPropertyChanged(nameof(PilotLabel));
            OnPropertyChanged(nameof(PilotRank));
            OnPropertyChanged(nameof(PilotRankInsigniaSource));
            OnPropertyChanged(nameof(PilotRankStripeCount));
            OnPropertyChanged(nameof(HasRankStripeOne));
            OnPropertyChanged(nameof(HasRankStripeTwo));
            OnPropertyChanged(nameof(HasRankStripeThree));
            OnPropertyChanged(nameof(HasRankStripeFour));
            OnPropertyChanged(nameof(IsFirstOfficer));
            OnPropertyChanged(nameof(IsSeniorCaptain));
            OnPropertyChanged(nameof(IsTrainingCaptain));
            OnPropertyChanged(nameof(Initials));
            ProfileImageSource = LoadProfileImage(value?.ProfileImage);
            SessionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool IsAuthenticated => Session is not null;
    public string DisplayName => Session?.Name ?? "British Airways Virtual pilot";
    public string PilotRank => Session is null ? string.Empty : NormalizePilotRank(Session.Rank);
    public string PilotLabel => Session is null ? "Not signed in" : $"Pilot {Session.PilotNumber} · {PilotRank}";
    public ImageSource? PilotRankInsigniaSource => Session is null ? null : LoadPilotRankInsignia(PilotRank);
    public int PilotRankStripeCount => PilotRank switch
    {
        "Second Officer" => 1,
        "First Officer" => 2,
        "Senior First Officer" => 3,
        "Captain" or "Senior Captain" or "Training Captain" => 4,
        _ => 0,
    };
    public bool HasRankStripeOne => PilotRankStripeCount >= 1;
    public bool HasRankStripeTwo => PilotRankStripeCount >= 2;
    public bool HasRankStripeThree => PilotRankStripeCount >= 3;
    public bool HasRankStripeFour => PilotRankStripeCount >= 4;
    public bool IsFirstOfficer => PilotRank == "First Officer";
    public bool IsSeniorCaptain => PilotRank == "Senior Captain";
    public bool IsTrainingCaptain => PilotRank == "Training Captain";
    public string Initials => string.Concat(DisplayName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(part => char.ToUpperInvariant(part[0])));

    public bool RememberSignIn
    {
        get => _rememberSignIn;
        set => SetProperty(ref _rememberSignIn, value);
    }

    public ImageSource? ProfileImageSource
    {
        get => _profileImageSource;
        private set
        {
            if (!SetProperty(ref _profileImageSource, value)) return;
            OnPropertyChanged(nameof(HasProfileImage));
        }
    }

    public bool HasProfileImage => ProfileImageSource is not null;

    public FleetWebsiteFlightAssignmentDto? WebsiteFlightAssignment
    {
        get => _websiteFlightAssignment;
        private set
        {
            if (!SetProperty(ref _websiteFlightAssignment, value)) return;
            OnPropertyChanged(nameof(HasWebsiteFlightAssignment));
            OnPropertyChanged(nameof(WebsiteFlightAssignmentLabel));
            OnPropertyChanged(nameof(WebsiteFlightAssignmentDetail));
            SessionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool HasWebsiteFlightAssignment => WebsiteFlightAssignment is not null;

    public IReadOnlyList<FleetOperationsFlightDto> OperationsFlights
    {
        get => _operationsFlights;
        private set
        {
            _operationsFlights = value;
            OperationsFlightsRefreshed?.Invoke(this, EventArgs.Empty);
        }
    }

    public string FlightPlanLinkLabel
    {
        get => _flightPlanLinkLabel;
        private set => SetProperty(ref _flightPlanLinkLabel, value);
    }

    public string FlightPlanLinkDetail
    {
        get => _flightPlanLinkDetail;
        private set => SetProperty(ref _flightPlanLinkDetail, value);
    }

    public bool HasSimBriefFlightPlan
    {
        get => _hasSimBriefFlightPlan;
        private set => SetProperty(ref _hasSimBriefFlightPlan, value);
    }

    public bool IsFlightPlanLinked
    {
        get => _isFlightPlanLinked;
        private set => SetProperty(ref _isFlightPlanLinked, value);
    }

    public FleetAcarsSessionDto? ActiveAcarsSession
    {
        get => _activeAcarsSession;
        private set
        {
            if (!SetProperty(ref _activeAcarsSession, value)) return;
            OnPropertyChanged(nameof(IsAcarsOperating));
        }
    }

    public bool IsAcarsOperating => ActiveAcarsSession?.Status == "active";

    public string AcarsSessionLabel
    {
        get => _acarsSessionLabel;
        private set => SetProperty(ref _acarsSessionLabel, value);
    }

    public string LastFlightCompletionLabel
    {
        get => _lastFlightCompletionLabel;
        private set => SetProperty(ref _lastFlightCompletionLabel, value);
    }

    public string WebsiteFlightAssignmentLabel => WebsiteFlightAssignment is null
        ? "No BAV flight selected"
        : $"{WebsiteFlightAssignment.FlightNumber} · {WebsiteFlightAssignment.From} → {WebsiteFlightAssignment.To}";

    public string WebsiteFlightAssignmentDetail => WebsiteFlightAssignment is null
        ? "Choose a flight on the BAV website, then refresh it here."
        : $"{WebsiteFlightAssignment.Date} · {WebsiteFlightAssignment.Aircraft} · dep {WebsiteFlightAssignment.Departure}";

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    private async Task SignInAsync()
    {
        IsBusy = true;
        StatusMessage = "Signing in securely…";
        try
        {
            var signedInSession = await _apiClient.SignInAsync(_settings, Email, Password, RememberSignIn);
            Session = signedInSession;
            var wasRemembered = !RememberSignIn ||
                                (!string.IsNullOrWhiteSpace(signedInSession.DeviceSessionToken) && _sessionStore?.TrySave(signedInSession) == true);
            if (!RememberSignIn)
            {
                _sessionStore?.Clear();
            }
            Password = string.Empty;
            await RefreshWebsiteFlightAsync();
            await RecoverActiveAcarsSessionAsync();
            if (!wasRemembered)
            {
                StatusMessage = "Signed in, but Ember could not securely remember this account on this Windows profile.";
            }
        }
        finally
        {
            Password = string.Empty;
            IsBusy = false;
        }
    }

    private async Task SignOutAsync()
    {
        var signedInSession = Session;
        _sessionStore?.Clear();
        Session = null;
        WebsiteFlightAssignment = null;
        OperationsFlights = [];
        ActiveAcarsSession = null;
        AcarsSessionLabel = "No active ACARS flight";
        Password = string.Empty;
        IsBusy = true;
        try
        {
            if (signedInSession is not null)
            {
                await _apiClient.RevokeAccountSessionAsync(_settings, signedInSession);
            }
            StatusMessage = "Signed out. This device session has been revoked and your website password was never stored by Ember.";
        }
        catch (FleetApiException)
        {
            StatusMessage = "Signed out on this PC. Ember could not reach BAV to revoke the device session, so sign in and try again when you are online if this device is shared.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Verifies a session saved for this Windows user before allowing Ember to
    /// enter the operational workspace. Invalid or expired sessions stay on
    /// the sign-in page and the stored credential is removed.
    /// </summary>
    public async Task<bool> RestoreSessionAsync()
    {
        var saved = _sessionStore?.Load();
        if (saved is null)
        {
            StatusMessage = "Sign in with your British Airways Virtual website account to begin.";
            return false;
        }

        IsBusy = true;
        StatusMessage = "Restoring your secure BAV sign-in…";
        try
        {
            var refreshedSession = await _apiClient.RefreshAccountSessionAsync(_settings, saved);
            Session = refreshedSession;
            _sessionStore?.TrySave(refreshedSession);
            await RefreshWebsiteFlightAsync();
            await RecoverActiveAcarsSessionAsync();
            return true;
        }
        catch (FleetApiException exception) when (exception.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
        {
            _sessionStore?.Clear();
            Session = null;
            StatusMessage = "Your saved BAV sign-in has expired or was revoked. Sign in again to continue.";
            return false;
        }
        catch (FleetApiException)
        {
            Session = null;
            StatusMessage = "Ember could not verify your saved BAV sign-in. Check the website connection, then sign in to continue.";
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public FleetFlightAssignmentSubmissionDto GetCurrentFlightContext()
    {
        var assignment = WebsiteFlightAssignment
            ?? throw new FleetApiException("Choose a flight on the BAV website, then select Refresh website flight in Ember.");
        return new FleetFlightAssignmentSubmissionDto(assignment.FlightNumber, assignment.OriginIcao ?? assignment.From, assignment.DestinationIcao ?? assignment.To);
    }

    public void RefreshFlightPlanLink(string? simBriefFlightNumber, string? simBriefOrigin, string? simBriefDestination)
    {
        var hasFlightPlan = !string.IsNullOrWhiteSpace(simBriefFlightNumber) &&
                            !string.IsNullOrWhiteSpace(simBriefOrigin) &&
                            !string.IsNullOrWhiteSpace(simBriefDestination);
        HasSimBriefFlightPlan = hasFlightPlan;

        if (WebsiteFlightAssignment is null)
        {
            IsFlightPlanLinked = false;
            FlightPlanLinkLabel = "No BAV flight selected";
            FlightPlanLinkDetail = "Choose the flight on the BAV website and refresh it here before reserving an aircraft.";
            return;
        }

        if (!hasFlightPlan)
        {
            IsFlightPlanLinked = false;
            FlightPlanLinkLabel = "BAV flight selected · SimBrief validation pending";
            FlightPlanLinkDetail = "Import the SimBrief OFP when available. The BAV flight can still be reserved before then.";
            return;
        }

        var flightMatches = string.Equals(NormalizeFlightNumber(WebsiteFlightAssignment.FlightNumber), NormalizeFlightNumber(simBriefFlightNumber), StringComparison.Ordinal);
        var originMatches = string.Equals(NormalizeAirport(WebsiteFlightAssignment.OriginIcao ?? WebsiteFlightAssignment.From), NormalizeAirport(simBriefOrigin), StringComparison.Ordinal);
        var destinationMatches = string.Equals(NormalizeAirport(WebsiteFlightAssignment.DestinationIcao ?? WebsiteFlightAssignment.To), NormalizeAirport(simBriefDestination), StringComparison.Ordinal);
        IsFlightPlanLinked = flightMatches && originMatches && destinationMatches;

        if (IsFlightPlanLinked)
        {
            FlightPlanLinkLabel = "BAV website flight and SimBrief OFP match";
            FlightPlanLinkDetail = $"{WebsiteFlightAssignment.FlightNumber} · {NormalizeAirport(WebsiteFlightAssignment.OriginIcao ?? WebsiteFlightAssignment.From)} → {NormalizeAirport(WebsiteFlightAssignment.DestinationIcao ?? WebsiteFlightAssignment.To)} is ready for aircraft operations.";
            return;
        }

        FlightPlanLinkLabel = "BAV website flight and SimBrief OFP do not match";
        FlightPlanLinkDetail = $"BAV: {WebsiteFlightAssignment.FlightNumber} {NormalizeAirport(WebsiteFlightAssignment.OriginIcao ?? WebsiteFlightAssignment.From)} → {NormalizeAirport(WebsiteFlightAssignment.DestinationIcao ?? WebsiteFlightAssignment.To)}. SimBrief: {simBriefFlightNumber?.Trim()} {NormalizeAirport(simBriefOrigin)} → {NormalizeAirport(simBriefDestination)}. Select or import the correct flight before pushback.";
    }

    public void EnsureFlightPlanLink(string? simBriefFlightNumber, string? simBriefOrigin, string? simBriefDestination)
    {
        RefreshFlightPlanLink(simBriefFlightNumber, simBriefOrigin, simBriefDestination);
        if (HasSimBriefFlightPlan && !IsFlightPlanLinked)
        {
            throw new FleetApiException("The selected BAV flight does not match the loaded SimBrief OFP. Select or import the correct flight before pushback.");
        }
    }

    public async Task StartAcarsSessionAsync(string simulator)
    {
        if (IsAcarsOperating)
        {
            return;
        }

        var account = Session ?? throw new FleetApiException("Sign in with your BAV website account before starting a flight.");
        ActiveAcarsSession = await _apiClient.StartAcarsSessionAsync(_settings, account, simulator);
        AcarsSessionLabel = $"ACARS active · {ActiveAcarsSession.FlightNumber} · {ActiveAcarsSession.From} → {ActiveAcarsSession.To}";
        StatusMessage = $"ACARS is running for {ActiveAcarsSession.FlightNumber}. Ember will record the flight in the background.";
    }

    public async Task SendAcarsTelemetryAsync(FleetAcarsTelemetryDto telemetry)
    {
        var account = Session;
        var session = ActiveAcarsSession;
        if (account is null || session is null || session.Status != "active")
        {
            return;
        }

        await _apiClient.SendAcarsTelemetryAsync(_settings, account, session.Id, telemetry);
    }

    public async Task CompleteAcarsSessionAsync(int? landingFpm)
    {
        var account = Session;
        var session = ActiveAcarsSession;
        if (account is null || session is null || session.Status != "active")
        {
            return;
        }

        var completed = await _apiClient.CompleteAcarsSessionAsync(_settings, account, session.Id, landingFpm);
        ActiveAcarsSession = null;
        AcarsSessionLabel = "No active ACARS flight";
        LastFlightCompletionLabel = FormatCompletion(completed.Pirep);
        StatusMessage = "ACARS flight completed. Your BAV flight history and PIREP have been updated.";
    }

    public void ReportBackgroundAcarsFailure(Exception exception)
    {
        StatusMessage = $"ACARS needs attention: {exception.Message}";
    }

    public void ReportFlightLinkFailure(Exception exception)
    {
        StatusMessage = exception.Message;
    }

    public void ReportAutomaticAssignmentImport(string message) => StatusMessage = message;

    private async Task RefreshWebsiteFlightAsync()
    {
        var account = Session ?? throw new FleetApiException("Sign in with your BAV website account first.");
        IsBusy = true;
        try
        {
            try
            {
                var latestProfile = await _apiClient.GetAccountProfileAsync(_settings, account);
                Session = account with
                {
                    PilotNumber = latestProfile.PilotNumber,
                    Name = latestProfile.Name,
                    Email = latestProfile.Email,
                    ProfileImage = latestProfile.ProfileImage,
                    Rank = latestProfile.Rank
                };
                account = Session ?? account;
            }
            catch (FleetApiException)
            {
                // A profile refresh must never prevent pilots from refreshing
                // their active website flight during a temporary API failure.
            }
            var previousAssignmentId = WebsiteFlightAssignment?.Id;
            var assignment = await _apiClient.GetWebsiteFlightAssignmentAsync(_settings, account);
            WebsiteFlightAssignment = assignment;
            try
            {
                OperationsFlights = await _apiClient.GetOperationsFlightsAsync(_settings, account);
            }
            catch (FleetApiException)
            {
                // The operational board supplements the pilot's own assignment;
                // it must never prevent the active flight from loading.
                OperationsFlights = [];
            }
            StatusMessage = WebsiteFlightAssignment is null
                ? "No active flight is selected on the BAV website. Choose one there, then refresh this page."
                : $"Loaded {WebsiteFlightAssignment.FlightNumber} from your BAV account. Fleet aircraft selection now uses this flight.";
            if (assignment is not null)
            {
                WebsiteFlightAssignmentRefreshed?.Invoke(
                    this,
                    new WebsiteFlightAssignmentRefreshedEventArgs(
                        assignment,
                        !string.Equals(previousAssignmentId, assignment.Id, StringComparison.Ordinal)));
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RecoverActiveAcarsSessionAsync()
    {
        var account = Session ?? throw new FleetApiException("Sign in with your BAV website account first.");
        var recovered = await _apiClient.GetActiveAcarsSessionAsync(_settings, account);
        ActiveAcarsSession = recovered;
        if (recovered is null)
        {
            AcarsSessionLabel = "No active ACARS flight";
            return;
        }

        AcarsSessionLabel = $"ACARS recovered · {recovered.FlightNumber} · {recovered.From} → {recovered.To}";
        StatusMessage = $"Recovered active ACARS session for {recovered.FlightNumber}. Telemetry will continue in the background.";
    }

    public void Dispose() => _apiClient.Dispose();

    private static ImageSource? LoadProfileImage(string? dataUrl)
    {
        if (string.IsNullOrWhiteSpace(dataUrl) || !dataUrl.StartsWith("data:image/webp;base64,", StringComparison.Ordinal))
        {
            return null;
        }

        try
        {
            var base64 = dataUrl["data:image/webp;base64,".Length..];
            using var stream = new MemoryStream(Convert.FromBase64String(base64));
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (FormatException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private static string NormalizePilotRank(string? rank) => rank?.Trim() switch
    {
        "Second Officer" => "Second Officer",
        "First Officer" => "First Officer",
        "Senior First Officer" => "Senior First Officer",
        "Captain" => "Captain",
        "Senior Captain" => "Senior Captain",
        "Training Captain" => "Training Captain",
        _ => "Cadet",
    };

    private static ImageSource LoadPilotRankInsignia(string rank)
    {
        var assetName = rank switch
        {
            "Second Officer" => "SO",
            "First Officer" => "FO",
            "Senior First Officer" => "SFO",
            "Captain" => "C",
            "Senior Captain" => "SC",
            "Training Captain" => "TC",
            _ => "cadet",
        };

        return new BitmapImage(new Uri($"pack://application:,,,/FreeFlight.CabinControl;component/Assets/PilotRanks/{assetName}.png", UriKind.Absolute));
    }

    private static string NormalizeFlightNumber(string? value)
    {
        var normalized = string.Concat((value ?? string.Empty).Where(char.IsLetterOrDigit)).ToUpperInvariant();
        return normalized.StartsWith("BAW", StringComparison.Ordinal) && normalized[3..].All(char.IsDigit)
            ? $"BA{normalized[3..]}"
            : normalized;
    }

    private static string NormalizeAirport(string? value) => (value ?? string.Empty).Trim().ToUpperInvariant() switch
    {
        "EGLL" => "LHR", "EGKK" => "LGW", "EGLC" => "LCY", "ENGM" => "OSL",
        "KJFK" => "JFK", "KLAX" => "LAX", "KPDX" => "PDX", "KSEA" => "SEA", "KSFO" => "SFO", "KIAH" => "IAH",
        "OMDB" => "DXB", "WSSS" => "SIN", "RJTT" => "HND", "FACT" => "CPT", "YSSY" => "SYD", "FAOR" => "JNB",
        var airport => airport
    };

    private static string FormatCompletion(FleetPirepDto pirep)
    {
        var block = TimeSpan.FromMinutes(Math.Max(0, pirep.BlockMinutes));
        var landing = pirep.LandingFpm is null ? "landing rate unavailable" : $"{pirep.LandingFpm.Value} fpm";
        var fuel = pirep.FuelUsedKg is null ? "fuel use unavailable" : $"{pirep.FuelUsedKg.Value:N0} kg used";
        return $"PIREP submitted · {pirep.FlightNumber} {pirep.From} → {pirep.To} · block {block:h\\:mm} · {pirep.DistanceNm:N0} nm · {landing} · {fuel}.";
    }
}

public sealed record WebsiteFlightAssignmentRefreshedEventArgs(
    FleetWebsiteFlightAssignmentDto Assignment,
    bool IsNewAssignment);
