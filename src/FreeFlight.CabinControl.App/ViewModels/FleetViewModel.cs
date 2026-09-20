using System.Collections.ObjectModel;
using System.Net.Http;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using FreeFlight.CabinControl.App.Infrastructure;
using FreeFlight.CabinControl.App.Services;
using FreeFlight.CabinControl.Core.Configuration;

namespace FreeFlight.CabinControl.App.ViewModels;

public sealed class FleetViewModel : PageViewModel, IDisposable
{
    private static readonly Brush SuccessBrush = CreateBrush(50, 203, 126);
    private static readonly Brush WarningBrush = CreateBrush(255, 190, 70);
    private static readonly Brush DangerBrush = CreateBrush(242, 99, 105);
    private static readonly Brush InfoBrush = CreateBrush(91, 169, 255);
    private static readonly Brush MutedBrush = CreateBrush(142, 161, 186);

    private readonly AppSettings _settings;
    private readonly FleetApiClient _fleetApiClient;
    private readonly Func<FleetAccountSession?> _accountSession;
    private readonly Func<FleetFlightAssignmentSubmissionDto> _flightContext;
    private readonly DispatcherTimer _syncTimer;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private string _connectionLabel = "Fleet connection required";
    private string _connectionDetail = "Sign in on the BAV Account page to view the live British Airways Virtual fleet.";
    private Brush _connectionColor = WarningBrush;
    private string _lastSynchronizedLabel = "No fleet data synchronized";
    private bool _isSynchronizing;
    private bool _replacingAircraft;
    private string _selectedFleetRegistration;
    private string _searchText = string.Empty;
    private FleetAircraftRow? _selectedAircraft;
    private CancellationTokenSource? _detailLoadCancellation;
    private bool _isDetailLoading;
    private string _detailStatusLabel = "Select an aircraft to load its live fleet record.";
    private Brush _detailStatusColor = MutedBrush;
    private FleetWorkspaceTab _selectedWorkspaceTab = FleetWorkspaceTab.Overview;
    private bool _isDefectReportOpen;
    private string _defectCategory = "Passenger seat";
    private string _defectLocation = string.Empty;
    private string _defectDescription = string.Empty;
    private string _defectSeverity = "normal";
    private string _defectDispatchImpact = "none";
    private string _defectReportStatus = string.Empty;
    private Brush _defectReportStatusColor = MutedBrush;
    private FleetFlightAssignmentDto? _activeFlightAssignment;
    private string _flightAssignmentStatus = "Sign in with your BAV account to reserve an aircraft for a flight.";
    private Brush _flightAssignmentStatusColor = MutedBrush;

    public FleetViewModel(
        AppSettings settings,
        FleetApiClient fleetApiClient,
        Func<FleetAccountSession?>? accountSession = null,
        Func<FleetFlightAssignmentSubmissionDto>? flightContext = null)
        : base("Fleet Management", "Pilot-facing aircraft status, dispatch availability and fleet awareness")
    {
        _settings = settings;
        _fleetApiClient = fleetApiClient;
        _accountSession = accountSession ?? (() => null);
        _flightContext = flightContext ?? (() => new FleetFlightAssignmentSubmissionDto("BAV PREVIEW", null, null));
        _selectedFleetRegistration = settings.SelectedFleetRegistration;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, exception => ConnectionDetail = exception.Message);
        SelectWorkspaceTabCommand = new RelayCommand(parameter => SelectWorkspaceTab(parameter as string));
        OpenDefectReportCommand = new RelayCommand(_ => OpenDefectReport());
        CancelDefectReportCommand = new RelayCommand(_ => CloseDefectReport());
        SubmitDefectReportCommand = new AsyncRelayCommand(SubmitDefectReportAsync, exception =>
        {
            DefectReportStatus = exception.Message;
            DefectReportStatusColor = WarningBrush;
        });
        ReserveAircraftCommand = new AsyncRelayCommand(ReserveSelectedAircraftAsync, ShowFlightAssignmentError);
        ReleaseAircraftCommand = new AsyncRelayCommand(ReleaseAircraftReservationAsync, ShowFlightAssignmentError);
        _syncTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(Math.Clamp(settings.FleetSyncIntervalSeconds, 60, 900)) };
        _syncTimer.Tick += async (_, _) => await RefreshAsync();
        if (settings.FleetAutoSync)
        {
            _syncTimer.Start();
        }

        _ = RefreshAsync();
    }

    public ObservableCollection<FleetAircraftRow> Aircraft { get; } = [];

    public ObservableCollection<FleetAircraftRow> VisibleAircraft { get; } = [];

    public ObservableCollection<FleetDefectLine> ActiveDefects { get; } = [];

    public ObservableCollection<FleetDefectLine> OpenDefects { get; } = [];

    public ObservableCollection<FleetDefectLine> DeferredDefects { get; } = [];

    public ObservableCollection<FleetDefectLine> OperationalRestrictions { get; } = [];

    public ObservableCollection<FleetMaintenanceLine> MaintenanceWatch { get; } = [];

    public ObservableCollection<FleetActivityLine> RecentActivity { get; } = [];

    public ICommand RefreshCommand { get; }

    public ICommand SelectWorkspaceTabCommand { get; }

    public ICommand OpenDefectReportCommand { get; }

    public ICommand CancelDefectReportCommand { get; }

    public ICommand SubmitDefectReportCommand { get; }

    public ICommand ReserveAircraftCommand { get; }

    public ICommand ReleaseAircraftCommand { get; }

    public bool IsSynchronizing
    {
        get => _isSynchronizing;
        private set => SetProperty(ref _isSynchronizing, value);
    }

    public string ConnectionLabel
    {
        get => _connectionLabel;
        private set => SetProperty(ref _connectionLabel, value);
    }

    public string ConnectionDetail
    {
        get => _connectionDetail;
        private set => SetProperty(ref _connectionDetail, value);
    }

    public Brush ConnectionColor
    {
        get => _connectionColor;
        private set => SetProperty(ref _connectionColor, value);
    }

    public string LastSynchronizedLabel
    {
        get => _lastSynchronizedLabel;
        private set => SetProperty(ref _lastSynchronizedLabel, value);
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                ApplyFilter();
            }
        }
    }

    public FleetAircraftRow? SelectedAircraft
    {
        get => _selectedAircraft;
        set
        {
            if (SetProperty(ref _selectedAircraft, value))
            {
                if (!_replacingAircraft)
                {
                    if (!string.IsNullOrWhiteSpace(value?.Registration))
                    {
                        _selectedFleetRegistration = value.Registration;
                        _settings.SelectedFleetRegistration = value.Registration;
                    }
                }
                OnPropertyChanged(nameof(HasSelectedAircraft));
                StartDetailLoad(value);
            }
        }
    }

    public bool IsDetailLoading
    {
        get => _isDetailLoading;
        private set => SetProperty(ref _isDetailLoading, value);
    }

    public string DetailStatusLabel
    {
        get => _detailStatusLabel;
        private set => SetProperty(ref _detailStatusLabel, value);
    }

    public Brush DetailStatusColor
    {
        get => _detailStatusColor;
        private set => SetProperty(ref _detailStatusColor, value);
    }

    public bool IsDefectReportOpen
    {
        get => _isDefectReportOpen;
        private set => SetProperty(ref _isDefectReportOpen, value);
    }

    public FleetFlightAssignmentDto? ActiveFlightAssignment
    {
        get => _activeFlightAssignment;
        private set
        {
            if (!SetProperty(ref _activeFlightAssignment, value)) return;
            OnPropertyChanged(nameof(HasActiveFlightAssignment));
            OnPropertyChanged(nameof(IsFlightReserved));
            OnPropertyChanged(nameof(IsFlightOperating));
            OnPropertyChanged(nameof(FlightAssignmentLabel));
            OnPropertyChanged(nameof(ActiveFlightAircraftId));
            OnPropertyChanged(nameof(ActiveFlightRegistration));
        }
    }

    public bool HasActiveFlightAssignment => ActiveFlightAssignment?.Status is "reserved" or "operating";

    public bool IsFlightReserved => ActiveFlightAssignment?.Status == "reserved";

    public bool IsFlightOperating => ActiveFlightAssignment?.Status == "operating";

    public string FlightAssignmentLabel => ActiveFlightAssignment is null
        ? "No aircraft reserved for this flight"
        : ActiveFlightAssignment.Status == "operating"
            ? $"{ActiveFlightAssignment.FlightReference} is operating"
            : $"{ActiveFlightAssignment.FlightReference} has an aircraft reserved";

    public string? ActiveFlightAircraftId => HasActiveFlightAssignment ? ActiveFlightAssignment?.AircraftId : null;

    public string? ActiveFlightRegistration => ActiveFlightAircraftId is { } aircraftId
        ? Aircraft.FirstOrDefault(item => string.Equals(item.Id, aircraftId, StringComparison.Ordinal))?.Registration
        : null;

    public string FlightAssignmentStatus
    {
        get => _flightAssignmentStatus;
        private set => SetProperty(ref _flightAssignmentStatus, value);
    }

    public Brush FlightAssignmentStatusColor
    {
        get => _flightAssignmentStatusColor;
        private set => SetProperty(ref _flightAssignmentStatusColor, value);
    }

    public IReadOnlyList<string> DefectCategories { get; } = ["Passenger seat", "IFE", "USB / power", "PSU / lighting", "Galley", "Lavatory", "Cabin door", "PA / interphone", "Other"];

    public IReadOnlyList<string> DefectSeverities { get; } = ["low", "normal", "high", "critical"];

    public IReadOnlyList<string> DefectDispatchImpacts { get; } = ["none", "restriction", "blocking"];

    public string DefectCategory
    {
        get => _defectCategory;
        set => SetProperty(ref _defectCategory, value);
    }

    public string DefectLocation
    {
        get => _defectLocation;
        set => SetProperty(ref _defectLocation, value.ToUpperInvariant());
    }

    public string DefectDescription
    {
        get => _defectDescription;
        set => SetProperty(ref _defectDescription, value);
    }

    public string DefectSeverity
    {
        get => _defectSeverity;
        set => SetProperty(ref _defectSeverity, value);
    }

    public string DefectDispatchImpact
    {
        get => _defectDispatchImpact;
        set => SetProperty(ref _defectDispatchImpact, value);
    }

    public string DefectReportStatus
    {
        get => _defectReportStatus;
        private set => SetProperty(ref _defectReportStatus, value);
    }

    public Brush DefectReportStatusColor
    {
        get => _defectReportStatusColor;
        private set => SetProperty(ref _defectReportStatusColor, value);
    }

    public int FleetCount => Aircraft.Count;

    public int InServiceCount => Aircraft.Count(item => item.IsInService);

    public int MaintenanceCount => Aircraft.Count(item => item.IsInMaintenance);

    public int UnavailableCount => Aircraft.Count(item => item.IsUnavailable);

    public int RestrictionCount => Aircraft.Count(item => item.HasRestrictions);

    public int OtherCount => Aircraft.Count(item => !item.IsInService && !item.IsInMaintenance && !item.IsUnavailable && !item.HasRestrictions);

    public int SelectedOpenDefectCount => OpenDefects.Count;

    public int SelectedDeferredDefectCount => DeferredDefects.Count;

    public int SelectedRestrictionCount => OperationalRestrictions.Count;

    public bool HasAircraft => Aircraft.Count > 0;

    public bool HasVisibleAircraft => VisibleAircraft.Count > 0;

    public bool HasSelectedAircraft => SelectedAircraft is not null;

    public bool IsOverviewTab => _selectedWorkspaceTab == FleetWorkspaceTab.Overview;

    public bool IsStatusTab => _selectedWorkspaceTab == FleetWorkspaceTab.Status;

    public bool IsDefectsTab => _selectedWorkspaceTab == FleetWorkspaceTab.Defects;

    public bool IsMaintenanceTab => _selectedWorkspaceTab == FleetWorkspaceTab.Maintenance;

    public bool IsLogbookTab => _selectedWorkspaceTab == FleetWorkspaceTab.Logbook;

    public string EmptyAircraftMessage => string.IsNullOrWhiteSpace(SearchText)
        ? "No aircraft have been received yet. Sign in on the BAV Account page, then refresh."
        : "No aircraft match your search.";

    public async Task RefreshAsync()
    {
        await _refreshGate.WaitAsync();
        try
        {
            IsSynchronizing = true;
            var aircraft = await _fleetApiClient.GetAircraftAsync(_settings, RequireAccount());
            ActiveFlightAssignment = await _fleetApiClient.GetActiveFlightAssignmentAsync(_settings, RequireAccount());
            var selectedRegistration = _selectedFleetRegistration;
            var activeAircraftId = ActiveFlightAssignment?.AircraftId;
            if (string.IsNullOrWhiteSpace(selectedRegistration))
            {
                selectedRegistration = SelectedAircraft?.Registration;
            }
            if (string.IsNullOrWhiteSpace(selectedRegistration))
            {
                selectedRegistration = _settings.SelectedFleetRegistration;
            }

            _replacingAircraft = true;
            try
            {
                Aircraft.Clear();
                foreach (var item in aircraft.OrderBy(item => item.Registration, StringComparer.OrdinalIgnoreCase))
                {
                    Aircraft.Add(new FleetAircraftRow(item));
                }

                ApplyFilter();
                SelectedAircraft = Aircraft.FirstOrDefault(item => string.Equals(
                                       item.Id,
                                       activeAircraftId,
                                       StringComparison.Ordinal))
                                   ?? Aircraft.FirstOrDefault(item => string.Equals(
                                       item.Registration,
                                       selectedRegistration,
                                       StringComparison.OrdinalIgnoreCase))
                                   ?? Aircraft.FirstOrDefault();
            }
            finally
            {
                _replacingAircraft = false;
            }
            if (!string.IsNullOrWhiteSpace(SelectedAircraft?.Registration))
            {
                _selectedFleetRegistration = SelectedAircraft.Registration;
                _settings.SelectedFleetRegistration = SelectedAircraft.Registration;
            }

            ConnectionLabel = "Fleet data live";
            ConnectionDetail = $"{Aircraft.Count} aircraft synchronized from the authoritative Fleet API.";
            ConnectionColor = SuccessBrush;
            LastSynchronizedLabel = $"Last synchronized {DateTime.Now:t}";
            if (ActiveFlightAssignment is not null)
            {
                FlightAssignmentStatus = ActiveFlightAssignment.Status == "operating"
                    ? $"{ActiveFlightAssignment.FlightReference} is operating with {SelectedAircraft?.Registration ?? "your assigned registration"}. Aircraft hours and cycle tracking are live."
                    : $"{ActiveFlightAssignment.FlightReference} has {SelectedAircraft?.Registration ?? "an aircraft"} reserved. This assignment survives app restarts.";
                FlightAssignmentStatusColor = SuccessBrush;
            }
            RefreshSummary();
        }
        catch (Exception exception) when (exception is FleetApiException or HttpRequestException or TaskCanceledException)
        {
            ConnectionLabel = Aircraft.Count > 0 ? "Fleet data stale" : "Fleet connection required";
            ConnectionDetail = exception.Message;
            ConnectionColor = WarningBrush;
            LastSynchronizedLabel = Aircraft.Count > 0
                ? $"Showing last synchronized data · {LastSynchronizedLabel}"
                : "No cached fleet data is available.";
            RefreshSummary();
        }
        finally
        {
            IsSynchronizing = false;
            _refreshGate.Release();
        }
    }

    /// <summary>
    /// Makes a newly selected BAV aircraft type the active Fleet workspace
    /// selection. A reservation is still an explicit pilot action: automatic
    /// selection must not take an available aircraft away from another pilot.
    /// </summary>
    public bool SelectAircraftForBavAssignment(FleetWebsiteFlightAssignmentDto assignment)
    {
        ArgumentNullException.ThrowIfNull(assignment);

        if (HasActiveFlightAssignment)
        {
            var assignedAircraft = Aircraft.FirstOrDefault(item => string.Equals(
                item.Id,
                ActiveFlightAssignment?.AircraftId,
                StringComparison.Ordinal));
            if (assignedAircraft is not null)
            {
                SelectedAircraft = assignedAircraft;
            }

            FlightAssignmentStatus = $"Your existing {ActiveFlightAssignment!.FlightReference} aircraft reservation was kept unchanged.";
            FlightAssignmentStatusColor = WarningBrush;
            return false;
        }

        var matchingAircraft = Aircraft
            .Where(item => MatchesBavAircraft(item, assignment.Aircraft))
            .OrderByDescending(item => item.IsInService)
            .ThenBy(item => item.Registration, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (matchingAircraft is null)
        {
            FlightAssignmentStatus = $"No live fleet airframe matches the BAV aircraft {assignment.Aircraft}. Your current selection was left unchanged.";
            FlightAssignmentStatusColor = WarningBrush;
            return false;
        }

        SelectedAircraft = matchingAircraft;
        FlightAssignmentStatus = $"{matchingAircraft.Registration} ({matchingAircraft.AircraftType}) is selected for {assignment.FlightNumber}. Reserve it to acquire the airframe.";
        FlightAssignmentStatusColor = matchingAircraft.IsInService ? SuccessBrush : WarningBrush;
        return true;
    }

    public async Task RecordHardLandingAssessmentAsync(string aircraftId, int landingFpm, string? station)
    {
        try
        {
            var assessment = await _fleetApiClient.RecordHardLandingAssessmentAsync(
                _settings,
                RequireAccount(),
                aircraftId,
                new FleetLandingAssessmentSubmissionDto(landingFpm, station));
            DetailStatusLabel = assessment.Outcome == "maintenance_required"
                ? $"{assessment.Registration} is held for maintenance after a {assessment.LandingFpm} fpm landing."
                : $"{assessment.Registration} requires a visual inspection after a {assessment.LandingFpm} fpm landing.";
            DetailStatusColor = assessment.Outcome == "maintenance_required" ? DangerBrush : WarningBrush;
            await RefreshAsync();
        }
        catch (Exception exception) when (exception is FleetApiException or HttpRequestException or TaskCanceledException)
        {
            DetailStatusLabel = $"Hard-landing assessment could not be recorded: {exception.Message}";
            DetailStatusColor = WarningBrush;
        }
    }

    public void Dispose()
    {
        _syncTimer.Stop();
        _detailLoadCancellation?.Cancel();
        _detailLoadCancellation?.Dispose();
        _fleetApiClient.Dispose();
        _refreshGate.Dispose();
    }

    private void ApplyFilter()
    {
        var query = SearchText.Trim();
        VisibleAircraft.Clear();
        foreach (var aircraft in Aircraft.Where(item => string.IsNullOrWhiteSpace(query) || item.Matches(query)))
        {
            VisibleAircraft.Add(aircraft);
        }

        OnPropertyChanged(nameof(EmptyAircraftMessage));
        OnPropertyChanged(nameof(HasVisibleAircraft));
    }

    private void SelectWorkspaceTab(string? value)
    {
        _selectedWorkspaceTab = value?.ToLowerInvariant() switch
        {
            "status" => FleetWorkspaceTab.Status,
            "defects" => FleetWorkspaceTab.Defects,
            "maintenance" => FleetWorkspaceTab.Maintenance,
            "logbook" => FleetWorkspaceTab.Logbook,
            _ => FleetWorkspaceTab.Overview
        };

        OnPropertyChanged(nameof(IsOverviewTab));
        OnPropertyChanged(nameof(IsStatusTab));
        OnPropertyChanged(nameof(IsDefectsTab));
        OnPropertyChanged(nameof(IsMaintenanceTab));
        OnPropertyChanged(nameof(IsLogbookTab));
    }

    private void RefreshSummary()
    {
        OnPropertyChanged(nameof(FleetCount));
        OnPropertyChanged(nameof(InServiceCount));
        OnPropertyChanged(nameof(MaintenanceCount));
        OnPropertyChanged(nameof(UnavailableCount));
        OnPropertyChanged(nameof(RestrictionCount));
        OnPropertyChanged(nameof(OtherCount));
        OnPropertyChanged(nameof(HasAircraft));
        OnPropertyChanged(nameof(EmptyAircraftMessage));
    }

    private void RefreshDefectSummary()
    {
        OnPropertyChanged(nameof(SelectedOpenDefectCount));
        OnPropertyChanged(nameof(SelectedDeferredDefectCount));
        OnPropertyChanged(nameof(SelectedRestrictionCount));
    }

    private static bool MatchesBavAircraft(FleetAircraftRow aircraft, string? requestedAircraft)
    {
        var requestedIcao = ResolveBavAircraftIcao(requestedAircraft);
        if (!string.IsNullOrWhiteSpace(requestedIcao) &&
            string.Equals(aircraft.IcaoType, requestedIcao, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var requested = string.Concat((requestedAircraft ?? string.Empty).Where(char.IsLetterOrDigit));
        var fleetType = string.Concat(aircraft.AircraftType.Where(char.IsLetterOrDigit));
        return !string.IsNullOrWhiteSpace(requested) &&
               fleetType.Contains(requested, StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveBavAircraftIcao(string? aircraft)
    {
        var normalized = string.Concat((aircraft ?? string.Empty).Where(char.IsLetterOrDigit)).ToUpperInvariant();
        return normalized switch
        {
            var value when value.Contains("777200") || value.Contains("B772") || value.Contains("B77E") => "B772",
            var value when value.Contains("777300") || value.Contains("B773") || value.Contains("B77W") => "B773",
            var value when value.Contains("7878") || value.Contains("B788") => "B788",
            var value when value.Contains("7879") || value.Contains("B789") => "B789",
            var value when value.Contains("78710") || value.Contains("B78X") => "B78X",
            var value when value.Contains("A319") => "A319",
            var value when value.Contains("A320NEO") || value.Contains("A20N") => "A20N",
            var value when value.Contains("A320") => "A320",
            var value when value.Contains("A321NEO") || value.Contains("A21N") => "A21N",
            var value when value.Contains("A321") => "A321",
            var value when value.Contains("A350") || value.Contains("A359") || value.Contains("A35K") => "A359",
            var value when value.Contains("E190") => "E190",
            _ => string.Empty
        };
    }

    private FleetAccountSession RequireAccount() => _accountSession()
        ?? throw new FleetApiException("Sign in on the BAV Account page to use the live fleet.");

    private FleetFlightAssignmentSubmissionDto CurrentFlightContext()
    {
        var context = _flightContext();
        if (string.IsNullOrWhiteSpace(context.FlightReference))
        {
            throw new FleetApiException("Load a flight before reserving an aircraft.");
        }

        return context with { FlightReference = context.FlightReference.Trim() };
    }

    private async Task ReserveSelectedAircraftAsync()
    {
        if (HasActiveFlightAssignment)
        {
            throw new FleetApiException("Release or complete your current aircraft assignment before selecting another registration.");
        }

        var aircraft = SelectedAircraft ?? throw new FleetApiException("Select an aircraft before reserving it for a flight.");
        var flightContext = CurrentFlightContext();
        var account = RequireAccount();
        FlightAssignmentStatus = $"Reserving {aircraft.Registration} for {flightContext.FlightReference}…";
        FlightAssignmentStatusColor = InfoBrush;
        var assignment = await _fleetApiClient.ReserveAircraftForFlightAsync(
            _settings,
            account,
            aircraft.Id,
            flightContext);
        ActiveFlightAssignment = assignment;
        FlightAssignmentStatus = $"{aircraft.Registration} is reserved for {assignment.FlightReference}. Other pilots cannot select it.";
        FlightAssignmentStatusColor = SuccessBrush;
        await RefreshAsync();
    }

    public async Task<bool> StartAssignedFlightAsync()
    {
        if (IsFlightOperating)
        {
            return true;
        }
        if (!IsFlightReserved || ActiveFlightAssignment is null)
        {
            return false;
        }

        try
        {
            var assignment = ActiveFlightAssignment;
            ActiveFlightAssignment = await _fleetApiClient.StartAircraftFlightAsync(
                _settings,
                RequireAccount(),
                assignment.AircraftId,
                new FleetFlightAssignmentSubmissionDto(
                    assignment.FlightReference,
                    assignment.DepartureStation,
                    assignment.ArrivalStation));
            FlightAssignmentStatus = $"{assignment.FlightReference} is operating. Aircraft hours and cycle tracking are live.";
            FlightAssignmentStatusColor = SuccessBrush;
            await RefreshAsync();
            return true;
        }
        catch (Exception exception) when (exception is FleetApiException or HttpRequestException or TaskCanceledException)
        {
            FlightAssignmentStatus = $"Could not start the aircraft assignment: {exception.Message}";
            FlightAssignmentStatusColor = WarningBrush;
            return false;
        }
    }

    public async Task CompleteAssignedFlightAsync()
    {
        if (!IsFlightOperating || ActiveFlightAssignment is null)
        {
            return;
        }

        var assignment = ActiveFlightAssignment;
        try
        {
            await _fleetApiClient.CompleteAircraftFlightAsync(
                _settings,
                RequireAccount(),
                assignment.AircraftId,
                new FleetFlightAssignmentSubmissionDto(
                    assignment.FlightReference,
                    assignment.DepartureStation,
                    assignment.ArrivalStation));
            ActiveFlightAssignment = null;
            FlightAssignmentStatus = $"{assignment.FlightReference} completed. Flight hours, cycle, station and technical log were updated.";
            FlightAssignmentStatusColor = SuccessBrush;
            await RefreshAsync();
        }
        catch (Exception exception) when (exception is FleetApiException or HttpRequestException or TaskCanceledException)
        {
            FlightAssignmentStatus = $"Could not complete the aircraft assignment: {exception.Message}";
            FlightAssignmentStatusColor = WarningBrush;
        }
    }

    public async Task ReleaseAircraftReservationAsync()
    {
        if (!IsFlightReserved || ActiveFlightAssignment is null)
        {
            return;
        }

        var assignment = ActiveFlightAssignment;
        await _fleetApiClient.CancelAircraftReservationAsync(
            _settings,
            RequireAccount(),
            assignment.AircraftId,
            new FleetFlightAssignmentSubmissionDto(
                assignment.FlightReference,
                assignment.DepartureStation,
                assignment.ArrivalStation));
        ActiveFlightAssignment = null;
        FlightAssignmentStatus = $"{assignment.FlightReference} reservation released. The registration is available again.";
        FlightAssignmentStatusColor = MutedBrush;
        await RefreshAsync();
    }

    private void ShowFlightAssignmentError(Exception exception)
    {
        FlightAssignmentStatus = exception.Message;
        FlightAssignmentStatusColor = WarningBrush;
    }

    private void StartDetailLoad(FleetAircraftRow? aircraft)
    {
        _detailLoadCancellation?.Cancel();
        _detailLoadCancellation?.Dispose();
        _detailLoadCancellation = null;
        ActiveDefects.Clear();
        OpenDefects.Clear();
        DeferredDefects.Clear();
        OperationalRestrictions.Clear();
        MaintenanceWatch.Clear();
        RecentActivity.Clear();
        RefreshDefectSummary();

        if (aircraft is null)
        {
            DetailStatusLabel = "Select an aircraft to load its live fleet record.";
            DetailStatusColor = MutedBrush;
            return;
        }

        var cancellation = new CancellationTokenSource();
        _detailLoadCancellation = cancellation;
        _ = LoadDetailAsync(aircraft, cancellation.Token);
    }

    private void OpenDefectReport()
    {
        if (SelectedAircraft is null)
        {
            DetailStatusLabel = "Select an aircraft before reporting a cabin defect.";
            DetailStatusColor = WarningBrush;
            return;
        }

        DefectLocation = string.Empty;
        DefectDescription = string.Empty;
        DefectSeverity = "normal";
        DefectDispatchImpact = "none";
        DefectReportStatus = "Reports are saved only after the Fleet server accepts them.";
        DefectReportStatusColor = InfoBrush;
        IsDefectReportOpen = true;
    }

    private void CloseDefectReport()
    {
        IsDefectReportOpen = false;
        DefectReportStatus = string.Empty;
    }

    private async Task SubmitDefectReportAsync()
    {
        var aircraft = SelectedAircraft;
        if (aircraft is null)
        {
            throw new FleetApiException("Select an aircraft before reporting a cabin defect.");
        }
        if (string.IsNullOrWhiteSpace(DefectCategory) || DefectDescription.Trim().Length < 3)
        {
            throw new FleetApiException("Choose a category and enter a clear defect description of at least three characters.");
        }

        DefectReportStatus = "Submitting defect report…";
        DefectReportStatusColor = InfoBrush;
        var defect = await _fleetApiClient.ReportDefectAsync(_settings, RequireAccount(), aircraft.Id, new FleetDefectSubmissionDto(
            DefectCategory,
            DefectDescription.Trim(),
            DefectSeverity,
            DefectDispatchImpact,
            aircraft.Station,
            string.IsNullOrWhiteSpace(DefectLocation) ? null : DefectLocation));
        DefectReportStatus = $"{defect.Reference} accepted by Fleet. Refreshing the aircraft record…";
        DefectReportStatusColor = SuccessBrush;
        await RefreshAsync();
        IsDefectReportOpen = false;
    }

    private async Task LoadDetailAsync(FleetAircraftRow aircraft, CancellationToken cancellationToken)
    {
        IsDetailLoading = true;
        DetailStatusLabel = "Loading authoritative aircraft record…";
        DetailStatusColor = InfoBrush;
        try
        {
            var record = await _fleetApiClient.GetAircraftRecordAsync(_settings, RequireAccount(), aircraft.Id, cancellationToken);
            if (cancellationToken.IsCancellationRequested || !ReferenceEquals(SelectedAircraft, aircraft))
            {
                return;
            }

            foreach (var defect in (record.Defects ?? []).Where(item => item.Status is not ("closed" or "rectified" or "voided")))
            {
                var line = new FleetDefectLine(defect);
                if (line.IsDeferred)
                {
                    DeferredDefects.Add(line);
                    if (!string.IsNullOrWhiteSpace(line.Restriction)) OperationalRestrictions.Add(line);
                }
                else
                {
                    OpenDefects.Add(line);
                    if (ActiveDefects.Count < 3) ActiveDefects.Add(line);
                }
            }
            RefreshDefectSummary();
            foreach (var maintenance in (record.MaintenanceDue ?? []).Where(item => item.DueStatus is "due_soon" or "overdue").Take(3))
            {
                MaintenanceWatch.Add(new FleetMaintenanceLine(maintenance));
            }
            foreach (var history in (record.StatusHistory ?? []).Take(3))
            {
                RecentActivity.Add(new FleetActivityLine(history));
            }
            foreach (var log in (record.Logbook ?? []).Take(Math.Max(0, 3 - RecentActivity.Count)))
            {
                RecentActivity.Add(new FleetActivityLine(log));
            }

            DetailStatusLabel = record.Availability?.Available == false
                ? (record.Availability.Reasons?.FirstOrDefault() ?? "The aircraft is currently not dispatchable.")
                : "Authoritative aircraft record is current.";
            DetailStatusColor = record.Availability?.Available == false ? WarningBrush : SuccessBrush;
        }
        catch (OperationCanceledException)
        {
            // A newer selection superseded this request.
        }
        catch (Exception exception)
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                DetailStatusLabel = $"Could not load aircraft detail: {exception.Message}";
                DetailStatusColor = WarningBrush;
            }
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested && ReferenceEquals(SelectedAircraft, aircraft))
            {
                IsDetailLoading = false;
            }
        }
    }

    private static Brush CreateBrush(byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(Color.FromRgb(red, green, blue));
        brush.Freeze();
        return brush;
    }

    public static Brush ResolveStatusBrush(string status) => status.ToLowerInvariant() switch
    {
        "available" or "in_service" or "serviceable" or "dispatchable" => SuccessBrush,
        "dispatchable_with_restrictions" or "serviceable_with_deferred_defects" or "inspection_required" or "scheduled_maintenance" or "awaiting_parts" or "awaiting_engineering" => WarningBrush,
        "repaint" => InfoBrush,
        "grounded" or "aog" or "in_maintenance" or "damage_inspection" or "not_dispatchable" or "retired" => DangerBrush,
        _ => MutedBrush
    };
}

public enum FleetWorkspaceTab
{
    Overview,
    Status,
    Defects,
    Maintenance,
    Logbook
}

public sealed class FleetAircraftRow : ObservableObject
{
    public FleetAircraftRow(FleetAircraftSummaryDto aircraft)
    {
        Id = aircraft.Id;
        Registration = aircraft.Registration;
        AircraftType = string.IsNullOrWhiteSpace(aircraft.Variant) ? aircraft.AircraftModel : $"{aircraft.AircraftModel} · {aircraft.Variant}";
        IcaoType = aircraft.IcaoType;
        Station = aircraft.CurrentStation ?? "Station not reported";
        OperationalStatus = Label(aircraft.OperationalStatus);
        TechnicalStatus = Label(aircraft.TechnicalStatus);
        DispatchStatus = Label(aircraft.DispatchStatus);
        FlightHours = $"{Math.Max(0, aircraft.AirframeHoursMinutes) / 60:N0}:{Math.Max(0, aircraft.AirframeHoursMinutes) % 60:00}";
        Cycles = aircraft.AirframeCycles.ToString("N0");
        LastFlightLabel = FormatTimestamp(aircraft.LastFlightAt, "No flight recorded");
        NextFlightReference = string.IsNullOrWhiteSpace(aircraft.NextAssignedFlightReference)
            ? "No flight assigned"
            : aircraft.NextAssignedFlightReference;
        OperationalStatusBrush = FleetViewModel.ResolveStatusBrush(aircraft.OperationalStatus);
        TechnicalStatusBrush = FleetViewModel.ResolveStatusBrush(aircraft.TechnicalStatus);
        DispatchStatusBrush = FleetViewModel.ResolveStatusBrush(aircraft.DispatchStatus);
        IsInService = aircraft.DispatchStatus == "dispatchable" && aircraft.TechnicalStatus == "serviceable" && aircraft.OperationalStatus is not ("storage" or "retired");
        IsInMaintenance = aircraft.TechnicalStatus is "scheduled_maintenance" or "in_maintenance" or "awaiting_parts" or "awaiting_engineering";
        IsUnavailable = aircraft.DispatchStatus == "not_dispatchable" || aircraft.TechnicalStatus is "grounded" or "aog" or "damage_inspection";
        HasRestrictions = aircraft.DispatchStatus == "dispatchable_with_restrictions" || aircraft.TechnicalStatus is "serviceable_with_deferred_defects" or "inspection_required";
        AircraftImageUri = Uri.TryCreate(aircraft.Image?.Url, UriKind.Absolute, out var imageUri) && imageUri.Scheme is "https" or "http" ? imageUri : null;
        AircraftImageAttribution = AircraftImageUri is null ? string.Empty : string.IsNullOrWhiteSpace(aircraft.Image?.Credit)
            ? aircraft.Image?.Source ?? "Fleet image catalogue"
            : $"{aircraft.Image.Source} · {aircraft.Image.Credit}";
    }

    public string Id { get; }
    public string Registration { get; }
    public string AircraftType { get; }
    public string? IcaoType { get; }
    public string Station { get; }
    public string OperationalStatus { get; }
    public string TechnicalStatus { get; }
    public string DispatchStatus { get; }
    public string FlightHours { get; }
    public string Cycles { get; }
    public string LastFlightLabel { get; }
    public string NextFlightReference { get; }
    public Brush OperationalStatusBrush { get; }
    public Brush TechnicalStatusBrush { get; }
    public Brush DispatchStatusBrush { get; }
    public bool IsInService { get; }
    public bool IsInMaintenance { get; }
    public bool IsUnavailable { get; }
    public bool HasRestrictions { get; }
    public Uri? AircraftImageUri { get; }
    public bool HasAircraftImage => AircraftImageUri is not null;
    public string AircraftImageAttribution { get; }
    public string AircraftImageStatus => HasAircraftImage ? AircraftImageAttribution : "No approved fleet photo has been added yet";

    public bool Matches(string query) =>
        Registration.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        AircraftType.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        Station.Contains(query, StringComparison.OrdinalIgnoreCase);

    public static string Label(string value) => string.Join(" ", value.Split('_', StringSplitOptions.RemoveEmptyEntries).Select(part => char.ToUpperInvariant(part[0]) + part[1..]));

    private static string FormatTimestamp(string? value, string fallback) =>
        DateTimeOffset.TryParse(value, out var timestamp)
            ? timestamp.ToLocalTime().ToString("dd MMM · HH:mm")
            : fallback;
}

public sealed class FleetDefectLine(FleetDefectDto defect)
{
    public string Reference { get; } = defect.Reference;
    public string Description { get; } = defect.Description;
    public string Location { get; } = defect.SeatNumber ?? defect.ReportingStation ?? "Location not recorded";
    public string Status { get; } = FleetAircraftRow.Label(defect.Status);
    public Brush StatusBrush { get; } = FleetViewModel.ResolveStatusBrush(defect.DispatchImpact == "blocking" ? "not_dispatchable" : defect.Severity);
    // A valid MEL/CDL record is authoritative even if a legacy endpoint omits
    // the status text. This prevents a deferred item being hidden as an empty
    // open defect during a sync.
    public bool IsDeferred { get; } =
        string.Equals(defect.Status, "deferred", StringComparison.OrdinalIgnoreCase) ||
        defect.Deferral is not null;
    public string Category { get; } = defect.Category;
    public string Severity { get; } = FleetAircraftRow.Label(defect.Severity);
    public string DispatchImpact { get; } = FleetAircraftRow.Label(defect.DispatchImpact);
    public string Restriction { get; } = defect.Deferral?.Restriction ?? string.Empty;
    public string DeferralReference { get; } = defect.Deferral is null ? string.Empty : $"{defect.Deferral.DeferralKind.ToUpperInvariant()} {defect.Deferral.Reference}";
}

public sealed class FleetMaintenanceLine(FleetMaintenanceDueDto maintenance)
{
    public string Reference { get; } = maintenance.TaskCode;
    public string Description { get; } = maintenance.TaskName;
    public string Due { get; } = string.IsNullOrWhiteSpace(maintenance.DueReason) ? "Due information not available" : maintenance.DueReason;
    public string Status { get; } = FleetAircraftRow.Label(maintenance.DueStatus);
    public Brush StatusBrush { get; } = FleetViewModel.ResolveStatusBrush(maintenance.DueStatus == "overdue" ? "not_dispatchable" : "scheduled_maintenance");
}

public sealed class FleetActivityLine
{
    public FleetActivityLine(FleetStatusHistoryDto history)
    {
        Timestamp = Format(history.EffectiveAt);
        Description = history.Reason;
        Context = string.Join(" · ", new[] { history.Station, FleetAircraftRow.Label(history.TechnicalStatus) }.Where(value => !string.IsNullOrWhiteSpace(value)));
        StatusBrush = FleetViewModel.ResolveStatusBrush(history.DispatchStatus);
    }

    public FleetActivityLine(FleetLogEntryDto entry)
    {
        Timestamp = Format(entry.OccurredAt);
        Description = entry.Description;
        Context = string.Join(" · ", new[] { entry.Station, FleetAircraftRow.Label(entry.Category) }.Where(value => !string.IsNullOrWhiteSpace(value)));
        StatusBrush = FleetViewModel.ResolveStatusBrush(entry.Status);
    }

    public string Timestamp { get; }
    public string Description { get; }
    public string Context { get; }
    public Brush StatusBrush { get; }

    private static string Format(string value) => DateTimeOffset.TryParse(value, out var timestamp)
        ? timestamp.ToLocalTime().ToString("dd MMM · HH:mm")
        : "Time not recorded";
}
