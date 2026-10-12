using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Windows.Input;
using FreeFlight.CabinControl.App.Infrastructure;
using FreeFlight.CabinControl.App.Services;

namespace FreeFlight.CabinControl.App.ViewModels;

/// <summary>
/// Ember's authenticated companion to the BAV community calendar. The live
/// operations board and notices are supplied by the BAV account session;
/// following an event is intentionally stored only on this Windows profile.
/// </summary>
public sealed class CommunityHubViewModel : PageViewModel, IDisposable
{
    private static readonly CrewRoomEventSeed[] PublishedCalendarSnapshot =
    [
        new(
            "evt-alpine-arrival-2026-10",
            "Alpine Arrival Challenge",
            "Special Challenge",
            new DateOnly(2026, 10, 15),
            "17:30 – 22:00 UTC",
            "EGLL → LSGG",
            "London Heathrow to Geneva",
            "Short-haul fleet recommended",
            141,
            "+175 VA · +20 Tier",
            "Precision arrival bonus"),
        new(
            "evt-tokyo-night-run-2026-10",
            "Tokyo Night Run",
            "Long-haul Event",
            new DateOnly(2026, 10, 24),
            "19:00 – 23:59 UTC",
            "EGLL → RJTT",
            "London Heathrow to Tokyo Haneda",
            "Wide-body fleet recommended",
            229,
            "+200 VA · +20 Tier",
            "Tokyo Night Run badge")
    ];

    private readonly CabinAccountViewModel _account;
    private readonly CrewRoomRegistrationStore _registrationStore;
    private readonly HashSet<string> _followedEventIds;
    private readonly RelayCommand _refreshCommand;
    private string _localFollowStatus = "Follow an event to keep it in this Crew Room on this PC.";

    public CommunityHubViewModel(CabinAccountViewModel account, string settingsDirectory)
        : base("Crew Room", "Your place in the British Airways Virtual community")
    {
        _account = account;
        _registrationStore = new CrewRoomRegistrationStore(settingsDirectory);
        _followedEventIds = _registrationStore.Load().ToHashSet(StringComparer.Ordinal);

        Events = new ObservableCollection<CrewRoomEvent>(
            PublishedCalendarSnapshot.Select(seed => new CrewRoomEvent(seed, _followedEventIds.Contains(seed.Id))));
        LiveOperations = [];
        Notices = [];

        ToggleEventFollowCommand = new RelayCommand(ToggleEventFollow);
        _refreshCommand = new RelayCommand(
            _ => _account.RefreshWebsiteFlightCommand.Execute(null),
            _ => _account.RefreshWebsiteFlightCommand.CanExecute(null));
        RefreshCommand = _refreshCommand;
        OpenBavEventsCommand = new RelayCommand(_ => OpenBavEvents());

        _account.SessionChanged += HandleAccountChanged;
        _account.OperationsFlightsRefreshed += HandleOperationsFlightsRefreshed;
        _account.PropertyChanged += HandleAccountPropertyChanged;

        RefreshIdentity();
        RefreshLiveOperations();
        RefreshNotices();
    }

    public ObservableCollection<CrewRoomEvent> Events { get; }

    public ObservableCollection<CrewRoomOperation> LiveOperations { get; }

    public ObservableCollection<CrewRoomNotice> Notices { get; }

    public ICommand ToggleEventFollowCommand { get; }

    public ICommand RefreshCommand { get; }

    public ICommand OpenBavEventsCommand { get; }

    public string PilotName => _account.DisplayName;

    public string PilotLabel => _account.PilotLabel;

    public string PilotInitials => string.IsNullOrWhiteSpace(_account.Initials) ? "BA" : _account.Initials;

    public string ConnectionLabel => _account.IsAcarsOperating ? "ACARS live" : "BAV connected";

    public string ConnectionDetail => _account.IsAcarsOperating
        ? _account.AcarsSessionLabel
        : "Your BAV identity is connected to Ember.";

    public string ActiveAssignmentLabel => _account.WebsiteFlightAssignmentLabel;

    public string ActiveAssignmentDetail => _account.WebsiteFlightAssignmentDetail;

    public int LiveOperationCount => LiveOperations.Count;

    public int FollowingEventCount => Events.Count(item => item.IsFollowed);

    public int NoticeCount => Notices.Count;

    public bool HasLiveOperations => LiveOperations.Count > 0;

    public bool HasNotices => Notices.Count > 0;

    public bool IsRefreshing => _account.IsBusy;

    public string LiveOperationsSummary => HasLiveOperations
        ? $"{LiveOperationCount} flight{(LiveOperationCount == 1 ? string.Empty : "s")} returned by BAV Operations"
        : "No current flight slots have been returned by BAV Operations.";

    public string FollowSummary => FollowingEventCount switch
    {
        0 => "No events followed on this PC",
        1 => "1 event followed on this PC",
        _ => $"{FollowingEventCount} events followed on this PC"
    };

    public string NoticeSummary => _account.NotificationsSummary;

    public string LocalFollowStatus
    {
        get => _localFollowStatus;
        private set => SetProperty(ref _localFollowStatus, value);
    }

    public void Dispose()
    {
        _account.SessionChanged -= HandleAccountChanged;
        _account.OperationsFlightsRefreshed -= HandleOperationsFlightsRefreshed;
        _account.PropertyChanged -= HandleAccountPropertyChanged;
    }

    private void HandleAccountChanged(object? sender, EventArgs e)
    {
        RefreshIdentity();
        RefreshLiveOperations();
        RefreshNotices();
    }

    private void HandleOperationsFlightsRefreshed(object? sender, EventArgs e) => RefreshLiveOperations();

    private void HandleAccountPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(CabinAccountViewModel.Notifications) or
            nameof(CabinAccountViewModel.UnreadNotificationCount))
        {
            RefreshNotices();
        }

        if (e.PropertyName == nameof(CabinAccountViewModel.IsBusy))
        {
            OnPropertyChanged(nameof(IsRefreshing));
            _refreshCommand.NotifyCanExecuteChanged();
        }

        if (e.PropertyName is nameof(CabinAccountViewModel.ActiveAcarsSession) or
            nameof(CabinAccountViewModel.AcarsSessionLabel))
        {
            OnPropertyChanged(nameof(ConnectionLabel));
            OnPropertyChanged(nameof(ConnectionDetail));
        }

        if (e.PropertyName is nameof(CabinAccountViewModel.WebsiteFlightAssignment) or
            nameof(CabinAccountViewModel.DisplayName) or
            nameof(CabinAccountViewModel.PilotLabel) or
            nameof(CabinAccountViewModel.Initials))
        {
            RefreshIdentity();
        }
    }

    private void RefreshIdentity()
    {
        OnPropertyChanged(nameof(PilotName));
        OnPropertyChanged(nameof(PilotLabel));
        OnPropertyChanged(nameof(PilotInitials));
        OnPropertyChanged(nameof(ConnectionLabel));
        OnPropertyChanged(nameof(ConnectionDetail));
        OnPropertyChanged(nameof(ActiveAssignmentLabel));
        OnPropertyChanged(nameof(ActiveAssignmentDetail));
    }

    private void RefreshLiveOperations()
    {
        LiveOperations.Clear();
        foreach (var flight in _account.OperationsFlights
                     .OrderBy(flight => flight.Departure, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(flight => flight.FlightNumber, StringComparer.OrdinalIgnoreCase)
                     .Take(8))
        {
            LiveOperations.Add(new CrewRoomOperation(flight));
        }

        OnPropertyChanged(nameof(LiveOperationCount));
        OnPropertyChanged(nameof(HasLiveOperations));
        OnPropertyChanged(nameof(LiveOperationsSummary));
    }

    private void RefreshNotices()
    {
        Notices.Clear();
        foreach (var notice in _account.Notifications.Take(4))
        {
            Notices.Add(new CrewRoomNotice(notice));
        }

        OnPropertyChanged(nameof(NoticeCount));
        OnPropertyChanged(nameof(HasNotices));
        OnPropertyChanged(nameof(NoticeSummary));
    }

    private void ToggleEventFollow(object? parameter)
    {
        if (parameter is not CrewRoomEvent selected)
        {
            return;
        }

        selected.IsFollowed = !selected.IsFollowed;
        if (selected.IsFollowed)
        {
            _followedEventIds.Add(selected.Id);
        }
        else
        {
            _followedEventIds.Remove(selected.Id);
        }

        var saved = _registrationStore.TrySave(_followedEventIds);
        LocalFollowStatus = saved
            ? selected.IsFollowed
                ? $"{selected.Title} is now saved in this Crew Room on this PC."
                : $"{selected.Title} was removed from this Crew Room on this PC."
            : "Ember could not save this preference on this PC. Your BAV event registration is unaffected.";
        OnPropertyChanged(nameof(FollowingEventCount));
        OnPropertyChanged(nameof(FollowSummary));
    }

    private static void OpenBavEvents()
    {
        try
        {
            Process.Start(new ProcessStartInfo($"{FreeFlight.CabinControl.Core.Configuration.AppSettings.BritishAirwaysVirtualWebsiteUrl}/events")
            {
                UseShellExecute = true
            });
        }
        catch
        {
            // The Crew Room remains usable when Windows cannot open a browser.
        }
    }
}

public sealed class CrewRoomEvent : ObservableObject
{
    public CrewRoomEvent(CrewRoomEventSeed seed, bool isFollowed)
    {
        Id = seed.Id;
        Title = seed.Title;
        TypeLabel = seed.TypeLabel;
        DateLabel = seed.Date.ToString("ddd, dd MMM", CultureInfo.InvariantCulture).ToUpperInvariant();
        TimeLabel = seed.TimeLabel;
        RouteLabel = seed.RouteLabel;
        RouteDetail = seed.RouteDetail;
        AircraftNote = seed.AircraftNote;
        PilotsLabel = $"{seed.PilotCount:N0} pilots planning to fly";
        RewardsLabel = seed.RewardsLabel;
        BadgeLabel = seed.BadgeLabel;
        _isFollowed = isFollowed;
    }

    private bool _isFollowed;

    public string Id { get; }
    public string Title { get; }
    public string TypeLabel { get; }
    public string DateLabel { get; }
    public string TimeLabel { get; }
    public string RouteLabel { get; }
    public string RouteDetail { get; }
    public string AircraftNote { get; }
    public string PilotsLabel { get; }
    public string RewardsLabel { get; }
    public string BadgeLabel { get; }

    public bool IsFollowed
    {
        get => _isFollowed;
        set
        {
            if (!SetProperty(ref _isFollowed, value))
            {
                return;
            }

            OnPropertyChanged(nameof(FollowButtonLabel));
            OnPropertyChanged(nameof(FollowStateLabel));
            OnPropertyChanged(nameof(FollowStateColor));
        }
    }

    public string FollowButtonLabel => IsFollowed ? "Following" : "Follow event";

    public string FollowStateLabel => IsFollowed ? "Saved in Crew Room" : PilotsLabel;

    public string FollowStateColor => IsFollowed ? "#72E5A3" : "#9ABCE2";
}

public sealed class CrewRoomOperation
{
    public CrewRoomOperation(FleetOperationsFlightDto flight)
    {
        FlightNumber = flight.FlightNumber;
        RouteLabel = string.IsNullOrWhiteSpace(flight.OriginIcao) || string.IsNullOrWhiteSpace(flight.DestinationIcao)
            ? $"{flight.Departure} → {flight.Arrival}"
            : $"{flight.OriginIcao} → {flight.DestinationIcao}";
        RouteDetail = $"{flight.Departure} to {flight.Arrival}";
        Aircraft = flight.Aircraft;
        Departure = flight.Departure;
        IsCurrentPilot = flight.IsCurrentPilot;
        PilotMarker = IsCurrentPilot ? "YOUR FLIGHT" : "BAV OPERATION";
        StatusLabel = string.IsNullOrWhiteSpace(flight.Status) ? "Scheduled" : flight.Status.ToUpperInvariant();
        StatusColor = (flight.Status ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "active" or "airborne" or "in progress" => "#72E5A3",
            "delayed" => "#FFB85A",
            "cancelled" => "#FF7889",
            _ => "#6EBEFF"
        };
    }

    public string FlightNumber { get; }
    public string RouteLabel { get; }
    public string RouteDetail { get; }
    public string Aircraft { get; }
    public string Departure { get; }
    public bool IsCurrentPilot { get; }
    public string PilotMarker { get; }
    public string StatusLabel { get; }
    public string StatusColor { get; }
}

public sealed class CrewRoomNotice
{
    public CrewRoomNotice(FleetPilotNotificationDto notice)
    {
        Title = notice.Title;
        Body = notice.Body;
        Timestamp = FormatTimestamp(notice.CreatedAt);
        LevelColor = (notice.Level ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "success" => "#72E5A3",
            "warning" => "#FFB85A",
            "danger" or "error" => "#FF7889",
            _ => "#6EBEFF"
        };
    }

    public string Title { get; }
    public string Body { get; }
    public string Timestamp { get; }
    public string LevelColor { get; }

    private static string FormatTimestamp(DateTimeOffset timestamp)
    {
        var age = DateTimeOffset.UtcNow - timestamp.ToUniversalTime();
        if (age < TimeSpan.FromMinutes(1)) return "just now";
        if (age < TimeSpan.FromHours(1)) return $"{Math.Max(1, (int)age.TotalMinutes)}m ago";
        if (age < TimeSpan.FromDays(1)) return $"{Math.Max(1, (int)age.TotalHours)}h ago";
        return timestamp.ToLocalTime().ToString("dd MMM", CultureInfo.InvariantCulture);
    }
}

public sealed record CrewRoomEventSeed(
    string Id,
    string Title,
    string TypeLabel,
    DateOnly Date,
    string TimeLabel,
    string RouteLabel,
    string RouteDetail,
    string AircraftNote,
    int PilotCount,
    string RewardsLabel,
    string BadgeLabel);
