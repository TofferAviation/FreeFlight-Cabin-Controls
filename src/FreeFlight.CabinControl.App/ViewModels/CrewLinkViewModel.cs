using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using System.Windows.Threading;
using System.Text.Json;
using FreeFlight.CabinControl.App.Infrastructure;
using FreeFlight.CabinControl.App.Services;
using FreeFlight.CabinControl.Core.Configuration;
using FreeFlight.CabinControl.Core.Integration;

namespace FreeFlight.CabinControl.App.ViewModels;

/// <summary>
/// A BAV-account-only shared operations room. It deliberately synchronises
/// crew presence and Ember ownership, not a generic set of aircraft switches,
/// flight plans or credentials.
/// </summary>
public sealed class CrewLinkViewModel : PageViewModel, IDisposable
{
    private readonly AppSettings _settings;
    private readonly FleetApiClient _apiClient;
    private readonly Func<FleetAccountSession?> _account;
    private readonly ISharedFlightProfileDiagnostics? _sharedFlightProfileDiagnostics;
    private readonly DispatcherTimer _pollTimer;
    private FleetCrewLinkSessionDto? _session;
    private string _inviteCodeInput = string.Empty;
    private string _statusMessage = "Open a private flight crew room or join an invitation from your Captain.";
    private string _simulator = "No simulator connected";
    private bool _simulatorConnected;
    private bool _isBusy;
    private bool _refreshPending;

    public CrewLinkViewModel(
        AppSettings settings,
        FleetApiClient apiClient,
        Func<FleetAccountSession?> account,
        ISharedFlightProfileDiagnostics? sharedFlightProfileDiagnostics = null)
        : base("CrewLink", "Coordinate a shared BAV operation from Ember—without another cockpit application.")
    {
        _settings = settings;
        _apiClient = apiClient;
        _account = account;
        _sharedFlightProfileDiagnostics = sharedFlightProfileDiagnostics;
        CreateCommand = new AsyncRelayCommand(CreateAsync, exception => StatusMessage = exception.Message);
        JoinCommand = new AsyncRelayCommand(JoinAsync, exception => StatusMessage = exception.Message);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, exception => StatusMessage = exception.Message);
        LeaveCommand = new AsyncRelayCommand(LeaveAsync, exception => StatusMessage = exception.Message);
        HandoverCommand = new AsyncRelayCommand(HandoverAsync, exception => StatusMessage = exception.Message);
        SaveXPlaneProfileReportCommand = new AsyncRelayCommand(SaveXPlaneProfileReportAsync, exception => StatusMessage = exception.Message);
        _pollTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(8) };
        _pollTimer.Tick += HandlePollTimerTick;
        RefreshXPlaneProfile();
    }

    public ICommand CreateCommand { get; }
    public ICommand JoinCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand LeaveCommand { get; }
    public ICommand HandoverCommand { get; }
    public ICommand SaveXPlaneProfileReportCommand { get; }
    public ObservableCollection<FleetCrewLinkMemberDto> Members { get; } = [];
    public ObservableCollection<string> XPlaneProfileChecks { get; } = [];

    public string InviteCodeInput
    {
        get => _inviteCodeInput;
        set => SetProperty(ref _inviteCodeInput, value.ToUpperInvariant());
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    public bool HasSession => _session is not null;
    public bool IsHost => _session is not null && _account()?.PilotId == _session.HostPilotId;
    public bool HasFirstOfficer => Members.Any(member => member.Role == "first_officer");
    public bool IsControlOwner => _session is not null && _account()?.PilotId == _session.ControlOwnerPilotId;
    public string SessionFlightLabel => _session is null ? "No shared operation selected" : $"{_session.FlightNumber} · {_session.From} → {_session.To}";
    public string SessionAircraftLabel => _session?.Aircraft ?? "Your BAV assignment will remain private until the crew room opens.";
    public string InviteCodeLabel => string.IsNullOrWhiteSpace(_session?.InviteCode) ? "Invite code is visible to the Captain only." : _session.InviteCode;
    public string CrewRoomStatusLabel => _session?.Status.ToUpperInvariant() ?? "READY TO PAIR";
    public string ControlOwnerLabel => _session is null
        ? "Captain control will be assigned when a crew room opens."
        : Members.FirstOrDefault(member => member.PilotId == _session.ControlOwnerPilotId)?.PilotName is { Length: > 0 } owner
            ? $"{owner} has Ember operational control"
            : "Captain has Ember operational control";
    public string SimulatorLabel => _simulatorConnected ? $"{_simulator} connected" : "No simulator connected";
    public string SimulatorDetail => _simulatorConnected
        ? "CrewLink is sharing your Ember readiness—not aircraft switches or flight controls."
        : "Connect your simulator when ready; pairing can safely begin before then.";
    public string HandoverLabel => IsControlOwner && HasFirstOfficer ? "Hand Ember control to First Officer" : "Captain handover available when First Officer joins";
    public string SafeSyncDetail => "CrewLink never shares passwords, SayIntentions keys, flight-plan files or another pilot’s PIREP. Aircraft switch and FMS syncing are intentionally not enabled in this first protected release.";
    public SharedFlightProfileReport XPlaneProfileReport => _sharedFlightProfileDiagnostics?.GetSharedFlightProfileReport() ?? SharedFlightProfileReport.Offline;
    public string XPlaneProfileName => XPlaneProfileReport.ProfileName;
    public string XPlaneProfileStatus => XPlaneProfileReport.State switch
    {
        SharedFlightProfileState.ReadyForGuidedValidation => "READY FOR GUIDED VALIDATION",
        SharedFlightProfileState.NeedsGuidedValidation => "SIGNALS NEED REVIEW",
        SharedFlightProfileState.UnrecognisedAircraft => "NOT IN THIS TEST GROUP",
        _ => "WAITING FOR X-PLANE"
    };
    public string XPlaneProfileSummary => XPlaneProfileReport.Summary;
    public string XPlaneProfileAircraft => string.IsNullOrWhiteSpace(XPlaneProfileReport.AircraftDescription)
        ? "Load an X-Plane aircraft to begin the local check."
        : string.IsNullOrWhiteSpace(XPlaneProfileReport.AircraftIcao)
            ? XPlaneProfileReport.AircraftDescription
            : $"{XPlaneProfileReport.AircraftIcao} · {XPlaneProfileReport.AircraftDescription}";
    public bool CanSaveXPlaneProfileReport => XPlaneProfileReport.State is not SharedFlightProfileState.SimulatorNotConnected;

    public void AccountChanged()
    {
        if (_account() is null)
        {
            ClearSession();
            return;
        }
        _ = RefreshAsync();
    }

    public void UpdateSimulatorStatus(BridgeStatus status)
    {
        _simulatorConnected = status.State == BridgeConnectionState.Connected;
        _simulator = _simulatorConnected ? status.Simulator : "No simulator connected";
        OnPropertyChanged(nameof(SimulatorLabel));
        OnPropertyChanged(nameof(SimulatorDetail));
        RefreshXPlaneProfile();
        if (HasSession) _ = PublishPresenceAsync();
    }

    public void Dispose()
    {
        _pollTimer.Stop();
        _pollTimer.Tick -= HandlePollTimerTick;
        _apiClient.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task CreateAsync()
    {
        var account = RequireAccount();
        IsBusy = true;
        try
        {
            ApplySession(await _apiClient.CreateCrewLinkSessionAsync(_settings, account, SimulatorKey()));
            StatusMessage = "Private CrewLink room opened. Share the invite code only with your operating crew.";
        }
        finally { IsBusy = false; }
    }

    private async Task JoinAsync()
    {
        if (string.IsNullOrWhiteSpace(InviteCodeInput)) throw new InvalidOperationException("Enter the Captain’s CrewLink invite code.");
        var account = RequireAccount();
        IsBusy = true;
        try
        {
            ApplySession(await _apiClient.JoinCrewLinkSessionAsync(_settings, account, InviteCodeInput, SimulatorKey()));
            InviteCodeInput = string.Empty;
            StatusMessage = "CrewLink connected. Your BAV account, flight and records remain your own.";
        }
        finally { IsBusy = false; }
    }

    private async Task RefreshAsync()
    {
        if (_refreshPending || _account() is not { } account) return;
        _refreshPending = true;
        try
        {
            var session = await _apiClient.GetCrewLinkSessionAsync(_settings, account);
            if (session is null) ClearSession(); else ApplySession(session);
        }
        finally { _refreshPending = false; }
    }

    private async Task PublishPresenceAsync()
    {
        if (_refreshPending || _session is null || _account() is not { } account) return;
        _refreshPending = true;
        try
        {
            var session = await _apiClient.UpdateCrewLinkPresenceAsync(_settings, account, _session.Id, SimulatorKey(), _simulatorConnected);
            if (session is null) ClearSession(); else ApplySession(session);
        }
        catch (FleetApiException exception) { StatusMessage = exception.Message; }
        finally { _refreshPending = false; }
    }

    private async Task HandoverAsync()
    {
        if (_session is null || !IsHost || !IsControlOwner) throw new InvalidOperationException("Only the Captain with current Ember control can hand it over.");
        var firstOfficer = Members.FirstOrDefault(member => member.Role == "first_officer");
        if (firstOfficer is null) throw new InvalidOperationException("A First Officer must join before control can be handed over.");
        var account = RequireAccount();
        IsBusy = true;
        try
        {
            ApplySession(await _apiClient.TransferCrewLinkControlAsync(_settings, account, _session.Id, firstOfficer.PilotId));
            StatusMessage = $"Ember operational control handed to {firstOfficer.PilotName}.";
        }
        finally { IsBusy = false; }
    }

    private async Task LeaveAsync()
    {
        if (_session is null) return;
        var account = RequireAccount();
        IsBusy = true;
        try
        {
            await _apiClient.LeaveCrewLinkSessionAsync(_settings, account, _session.Id);
            ClearSession();
            StatusMessage = "You left CrewLink. No BAV flight or ACARS record was changed.";
        }
        finally { IsBusy = false; }
    }

    private async Task SaveXPlaneProfileReportAsync()
    {
        var report = XPlaneProfileReport;
        if (report.State == SharedFlightProfileState.SimulatorNotConnected)
        {
            throw new InvalidOperationException("Connect X-Plane and load an aircraft before saving a profile report.");
        }

        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FreeFlight",
            "CabinControl",
            "crewlink-xplane-reports");
        Directory.CreateDirectory(directory);
        var safeProfile = string.IsNullOrWhiteSpace(report.ProfileId) ? "unrecognised-aircraft" : report.ProfileId;
        var fileName = $"crewlink-{safeProfile}-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-utc.json";
        var path = Path.Combine(directory, fileName);
        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(path, json).ConfigureAwait(true);
        StatusMessage = $"X-Plane profile report saved locally: {path}";
    }

    private FleetAccountSession RequireAccount() => _account() ?? throw new InvalidOperationException("Sign in to your BAV account to use CrewLink.");

    private string? SimulatorKey() => !_simulatorConnected ? null
        : _simulator.Contains("X-Plane", StringComparison.OrdinalIgnoreCase) ? "xplane12"
        : _simulator.Contains("2020", StringComparison.OrdinalIgnoreCase) ? "msfs2020"
        : _simulator.Contains("MSFS", StringComparison.OrdinalIgnoreCase) || _simulator.Contains("Microsoft", StringComparison.OrdinalIgnoreCase) ? "msfs2024"
        : null;

    private void ApplySession(FleetCrewLinkSessionDto session)
    {
        _session = session;
        Members.Clear();
        foreach (var member in session.Members ?? []) Members.Add(member);
        _pollTimer.Start();
        NotifySessionChanged();
    }

    private void ClearSession()
    {
        _session = null;
        Members.Clear();
        _pollTimer.Stop();
        NotifySessionChanged();
    }

    private void NotifySessionChanged()
    {
        OnPropertyChanged(nameof(HasSession));
        OnPropertyChanged(nameof(IsHost));
        OnPropertyChanged(nameof(HasFirstOfficer));
        OnPropertyChanged(nameof(IsControlOwner));
        OnPropertyChanged(nameof(SessionFlightLabel));
        OnPropertyChanged(nameof(SessionAircraftLabel));
        OnPropertyChanged(nameof(InviteCodeLabel));
        OnPropertyChanged(nameof(CrewRoomStatusLabel));
        OnPropertyChanged(nameof(ControlOwnerLabel));
        OnPropertyChanged(nameof(HandoverLabel));
    }

    private void RefreshXPlaneProfile()
    {
        var report = XPlaneProfileReport;
        XPlaneProfileChecks.Clear();
        foreach (var check in report.Checks)
        {
            var marker = check.Passed ? "✓" : check.Required ? "!" : "–";
            XPlaneProfileChecks.Add($"{marker} {check.Name}: {check.Detail}");
        }
        OnPropertyChanged(nameof(XPlaneProfileReport));
        OnPropertyChanged(nameof(XPlaneProfileName));
        OnPropertyChanged(nameof(XPlaneProfileStatus));
        OnPropertyChanged(nameof(XPlaneProfileSummary));
        OnPropertyChanged(nameof(XPlaneProfileAircraft));
        OnPropertyChanged(nameof(CanSaveXPlaneProfileReport));
    }

    private void HandlePollTimerTick(object? sender, EventArgs e) => _ = PublishPresenceAsync();
}
