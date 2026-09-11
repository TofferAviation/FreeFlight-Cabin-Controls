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
    private readonly DispatcherTimer _syncTimer;
    private string _connectionLabel = "Fleet connection required";
    private string _connectionDetail = "Add the protected website address and desktop access key in Settings to see your airline's live fleet.";
    private Brush _connectionColor = WarningBrush;
    private string _lastSynchronizedLabel = "No fleet data synchronized";
    private bool _isSynchronizing;
    private string _searchText = string.Empty;
    private FleetAircraftRow? _selectedAircraft;
    private CancellationTokenSource? _detailLoadCancellation;
    private bool _isDetailLoading;
    private string _detailStatusLabel = "Select an aircraft to load its live fleet record.";
    private Brush _detailStatusColor = MutedBrush;
    private bool _isDefectReportOpen;
    private string _defectCategory = "Passenger seat";
    private string _defectLocation = string.Empty;
    private string _defectDescription = string.Empty;
    private string _defectSeverity = "normal";
    private string _defectDispatchImpact = "none";
    private string _defectReportStatus = string.Empty;
    private Brush _defectReportStatusColor = MutedBrush;

    public FleetViewModel(AppSettings settings, FleetApiClient fleetApiClient)
        : base("Fleet Management", "Pilot-facing aircraft status, dispatch availability and fleet awareness")
    {
        _settings = settings;
        _fleetApiClient = fleetApiClient;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, exception => ConnectionDetail = exception.Message);
        OpenDefectReportCommand = new RelayCommand(_ => OpenDefectReport());
        CancelDefectReportCommand = new RelayCommand(_ => CloseDefectReport());
        SubmitDefectReportCommand = new AsyncRelayCommand(SubmitDefectReportAsync, exception =>
        {
            DefectReportStatus = exception.Message;
            DefectReportStatusColor = WarningBrush;
        });
        _syncTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(Math.Clamp(settings.FleetSyncIntervalSeconds, 10, 300)) };
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

    public ObservableCollection<FleetMaintenanceLine> MaintenanceWatch { get; } = [];

    public ObservableCollection<FleetActivityLine> RecentActivity { get; } = [];

    public ICommand RefreshCommand { get; }

    public ICommand OpenDefectReportCommand { get; }

    public ICommand CancelDefectReportCommand { get; }

    public ICommand SubmitDefectReportCommand { get; }

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
                _settings.SelectedFleetRegistration = value?.Registration ?? string.Empty;
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

    public bool HasAircraft => Aircraft.Count > 0;

    public bool HasVisibleAircraft => VisibleAircraft.Count > 0;

    public bool HasSelectedAircraft => SelectedAircraft is not null;

    public string EmptyAircraftMessage => string.IsNullOrWhiteSpace(SearchText)
        ? "No aircraft have been received yet. Configure the Fleet website address and desktop access key in Settings, then refresh."
        : "No aircraft match your search.";

    public async Task RefreshAsync()
    {
        if (IsSynchronizing)
        {
            return;
        }

        IsSynchronizing = true;
        try
        {
            var aircraft = await _fleetApiClient.GetAircraftAsync(_settings);
            Aircraft.Clear();
            foreach (var item in aircraft.OrderBy(item => item.Registration, StringComparer.OrdinalIgnoreCase))
            {
                Aircraft.Add(new FleetAircraftRow(item));
            }

            ApplyFilter();
            SelectedAircraft = Aircraft.FirstOrDefault(item => string.Equals(
                                   item.Registration,
                                   _settings.SelectedFleetRegistration,
                                   StringComparison.OrdinalIgnoreCase))
                               ?? Aircraft.FirstOrDefault();

            ConnectionLabel = "Fleet data live";
            ConnectionDetail = $"{Aircraft.Count} aircraft synchronized from the authoritative Fleet API.";
            ConnectionColor = SuccessBrush;
            LastSynchronizedLabel = $"Last synchronized {DateTime.Now:t}";
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
        }
    }

    public void Dispose()
    {
        _syncTimer.Stop();
        _detailLoadCancellation?.Cancel();
        _detailLoadCancellation?.Dispose();
        _fleetApiClient.Dispose();
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

    private void RefreshSummary()
    {
        OnPropertyChanged(nameof(FleetCount));
        OnPropertyChanged(nameof(InServiceCount));
        OnPropertyChanged(nameof(MaintenanceCount));
        OnPropertyChanged(nameof(UnavailableCount));
        OnPropertyChanged(nameof(RestrictionCount));
        OnPropertyChanged(nameof(HasAircraft));
        OnPropertyChanged(nameof(EmptyAircraftMessage));
    }

    private void StartDetailLoad(FleetAircraftRow? aircraft)
    {
        _detailLoadCancellation?.Cancel();
        _detailLoadCancellation?.Dispose();
        _detailLoadCancellation = null;
        ActiveDefects.Clear();
        MaintenanceWatch.Clear();
        RecentActivity.Clear();

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
        var defect = await _fleetApiClient.ReportDefectAsync(_settings, aircraft.Id, new FleetDefectSubmissionDto(
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
            var record = await _fleetApiClient.GetAircraftRecordAsync(_settings, aircraft.Id, cancellationToken);
            if (cancellationToken.IsCancellationRequested || !ReferenceEquals(SelectedAircraft, aircraft))
            {
                return;
            }

            foreach (var defect in (record.Defects ?? []).Where(item => !string.Equals(item.Status, "closed", StringComparison.OrdinalIgnoreCase)).Take(3))
            {
                ActiveDefects.Add(new FleetDefectLine(defect));
            }
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
        catch (Exception exception) when (exception is FleetApiException or HttpRequestException or TaskCanceledException)
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

public sealed class FleetAircraftRow
{
    public FleetAircraftRow(FleetAircraftSummaryDto aircraft)
    {
        Id = aircraft.Id;
        Registration = aircraft.Registration;
        AircraftType = string.IsNullOrWhiteSpace(aircraft.Variant) ? aircraft.AircraftModel : $"{aircraft.AircraftModel} · {aircraft.Variant}";
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
    }

    public string Id { get; }
    public string Registration { get; }
    public string AircraftType { get; }
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
