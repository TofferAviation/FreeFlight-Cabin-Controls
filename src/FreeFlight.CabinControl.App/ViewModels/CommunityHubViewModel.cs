using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Windows.Input;
using System.Windows.Threading;
using FreeFlight.CabinControl.App.Infrastructure;
using FreeFlight.CabinControl.App.Services;
using FreeFlight.CabinControl.Core.Configuration;

namespace FreeFlight.CabinControl.App.ViewModels;

/// <summary>
/// Ember's BAV-authenticated community lounge. Live operations and notices
/// remain private account data; Crew Lounge messages are separately stored and
/// moderated by BAV rather than being simulated on one pilot's PC.
/// </summary>
public sealed class CommunityHubViewModel : PageViewModel, IDisposable
{
    private static readonly DateOnly CalendarAnchor = new(2026, 10, 16);

    // Ember mirrors the website's curated calendar style locally so the Crew
    // Lounge remains useful between website refreshes. These are event ideas
    // and group-size guides, never a claim of live registrations or attendance.
    private static readonly CrewRoomEventTemplate[] CalendarRotation =
    [
        new("friday-night-oslo", "Friday Night in Oslo", "Community Flight", "17:30 – 22:00 UTC", "EGLL → ENGM", "London Heathrow to Oslo Gardermoen", "Short-haul fleet recommended", "+125 VA · +10 Tier", "Nordic Friday badge"),
        new("european-city-hopper", "European City Hopper", "Regional Event", "18:00 – 22:00 UTC", "EGLL → LOWW", "London Heathrow to Vienna", "Short-haul fleet recommended", "+100 VA · +10 Tier", "City Hopper badge"),
        new("atlantic-friday", "Atlantic Friday", "Long-haul Event", "17:00 – 23:59 UTC", "EGLL → KJFK", "London Heathrow to New York JFK", "Wide-body fleet recommended", "+175 VA · +15 Tier", "Atlantic crossing badge"),
        new("alpine-arrival", "Alpine Arrival Challenge", "Special Challenge", "17:30 – 22:00 UTC", "EGLL → LSGG", "London Heathrow to Geneva", "Short-haul fleet recommended", "+175 VA · +20 Tier", "Precision arrival badge"),
        new("tokyo-night-run", "Tokyo Night Run", "Long-haul Event", "19:00 – 23:59 UTC", "EGLL → RJTT", "London Heathrow to Tokyo Haneda", "Wide-body fleet recommended", "+200 VA · +20 Tier", "Tokyo Night Run badge")
    ];

    private readonly AppSettings _settings;
    private readonly FleetApiClient _apiClient;
    private readonly CabinAccountViewModel _account;
    private readonly CrewRoomRegistrationStore _registrationStore;
    private readonly HashSet<string> _followedEventIds;
    private readonly RelayCommand _refreshCommand;
    private readonly DispatcherTimer _chatPollTimer;
    private bool _isChatRefreshing;
    private string _messageDraft = string.Empty;
    private string _chatStatus = "Sign in with BAV to join the Crew Lounge.";
    private string _localFollowStatus = "Follow an event to keep it in this Crew Lounge on this PC.";

    public CommunityHubViewModel(
        AppSettings settings,
        FleetApiClient apiClient,
        CabinAccountViewModel account,
        string settingsDirectory)
        : base("Crew Lounge", "A warmer corner of Ember for every British Airways Virtual pilot")
    {
        _settings = settings;
        _apiClient = apiClient;
        _account = account;
        _registrationStore = new CrewRoomRegistrationStore(settingsDirectory);
        _followedEventIds = _registrationStore.Load().ToHashSet(StringComparer.Ordinal);

        Events = new ObservableCollection<CrewRoomEvent>(
            BuildCalendar(DateOnly.FromDateTime(DateTime.Today))
                .Select(seed => new CrewRoomEvent(seed, _followedEventIds.Contains(seed.Id))));
        LiveOperations = [];
        Notices = [];
        ChatMessages = [];

        ToggleEventFollowCommand = new RelayCommand(ToggleEventFollow);
        _refreshCommand = new RelayCommand(
            _ => _account.RefreshWebsiteFlightCommand.Execute(null),
            _ => _account.RefreshWebsiteFlightCommand.CanExecute(null));
        RefreshCommand = _refreshCommand;
        RefreshChatCommand = new AsyncRelayCommand(
            () => RefreshCrewChatAsync(manualRefresh: true),
            exception => ChatStatus = DescribeChatFailure(exception));
        SendChatMessageCommand = new AsyncRelayCommand(
            SendCrewChatMessageAsync,
            exception => ChatStatus = DescribeChatFailure(exception));
        OpenBavEventsCommand = new RelayCommand(_ => OpenBavEvents());

        _chatPollTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(25)
        };
        _chatPollTimer.Tick += HandleChatPollTimerTick;

        _account.SessionChanged += HandleAccountChanged;
        _account.OperationsFlightsRefreshed += HandleOperationsFlightsRefreshed;
        _account.PropertyChanged += HandleAccountPropertyChanged;

        RefreshIdentity();
        RefreshLiveOperations();
        RefreshNotices();
        UpdateChatAccountState();
    }

    public ObservableCollection<CrewRoomEvent> Events { get; }
    public ObservableCollection<CrewRoomOperation> LiveOperations { get; }
    public ObservableCollection<CrewRoomNotice> Notices { get; }
    public ObservableCollection<CrewRoomChatMessage> ChatMessages { get; }

    public CrewRoomEvent FeaturedEvent => Events[0];
    public ICommand ToggleEventFollowCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand RefreshChatCommand { get; }
    public ICommand SendChatMessageCommand { get; }
    public ICommand OpenBavEventsCommand { get; }

    public string PilotName => _account.DisplayName;
    public string PilotLabel => _account.PilotLabel;
    public string PilotInitials => string.IsNullOrWhiteSpace(_account.Initials) ? "BA" : _account.Initials;
    public string ConnectionLabel => _account.IsAcarsOperating ? "ACARS live" : "BAV connected";
    public string ConnectionDetail => _account.IsAcarsOperating ? _account.AcarsSessionLabel : "Your BAV identity is connected to Ember.";
    public string ActiveAssignmentLabel => _account.WebsiteFlightAssignmentLabel;
    public string ActiveAssignmentDetail => _account.WebsiteFlightAssignmentDetail;
    public string CurrentAircraftLabel => _account.WebsiteFlightAssignment is { Aircraft: { Length: > 0 } aircraft }
        ? aircraft
        : "Choose a BAV flight to add an aircraft";
    public string WelcomeLabel => string.IsNullOrWhiteSpace(PilotName) ? "Make yourself at home." : $"Welcome back, {PilotName.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? PilotName}.";

    public int LiveOperationCount => LiveOperations.Count;
    public int FollowingEventCount => Events.Count(item => item.IsFollowed);
    public int NoticeCount => Notices.Count;
    public bool HasLiveOperations => LiveOperations.Count > 0;
    public bool HasNotices => Notices.Count > 0;
    public bool IsRefreshing => _account.IsBusy;
    public bool HasChatMessages => ChatMessages.Count > 0;
    public bool IsChatRefreshing
    {
        get => _isChatRefreshing;
        private set
        {
            if (!SetProperty(ref _isChatRefreshing, value)) return;
            OnPropertyChanged(nameof(ChatConnectionLabel));
            OnPropertyChanged(nameof(CanSendCrewMessage));
        }
    }

    public string MessageDraft
    {
        get => _messageDraft;
        set
        {
            var truncated = value?.Length > 600 ? value[..600] : value ?? string.Empty;
            if (!SetProperty(ref _messageDraft, truncated)) return;
            OnPropertyChanged(nameof(CanSendCrewMessage));
        }
    }

    public string ChatStatus
    {
        get => _chatStatus;
        private set => SetProperty(ref _chatStatus, value);
    }

    public bool CanSendCrewMessage => _account.Session is not null && !IsChatRefreshing && !string.IsNullOrWhiteSpace(MessageDraft);

    public string ChatConnectionLabel => _account.Session is null
        ? "BAV sign-in needed"
        : IsChatRefreshing
            ? "Updating lounge"
            : "BAV crew chat";

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
        _chatPollTimer.Stop();
        _chatPollTimer.Tick -= HandleChatPollTimerTick;
        _account.SessionChanged -= HandleAccountChanged;
        _account.OperationsFlightsRefreshed -= HandleOperationsFlightsRefreshed;
        _account.PropertyChanged -= HandleAccountPropertyChanged;
        _apiClient.Dispose();
        GC.SuppressFinalize(this);
    }

    private void HandleAccountChanged(object? sender, EventArgs e)
    {
        RefreshIdentity();
        RefreshLiveOperations();
        RefreshNotices();
        UpdateChatAccountState();
    }

    private void HandleOperationsFlightsRefreshed(object? sender, EventArgs e) => RefreshLiveOperations();

    private void HandleAccountPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(CabinAccountViewModel.Notifications) or nameof(CabinAccountViewModel.UnreadNotificationCount))
        {
            RefreshNotices();
        }

        if (e.PropertyName == nameof(CabinAccountViewModel.IsBusy))
        {
            OnPropertyChanged(nameof(IsRefreshing));
            _refreshCommand.NotifyCanExecuteChanged();
        }

        if (e.PropertyName is nameof(CabinAccountViewModel.ActiveAcarsSession) or nameof(CabinAccountViewModel.AcarsSessionLabel))
        {
            OnPropertyChanged(nameof(ConnectionLabel));
            OnPropertyChanged(nameof(ConnectionDetail));
        }

        if (e.PropertyName is nameof(CabinAccountViewModel.WebsiteFlightAssignment) or nameof(CabinAccountViewModel.DisplayName) or nameof(CabinAccountViewModel.PilotLabel) or nameof(CabinAccountViewModel.Initials))
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
        OnPropertyChanged(nameof(CurrentAircraftLabel));
        OnPropertyChanged(nameof(WelcomeLabel));
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

    private void UpdateChatAccountState()
    {
        if (_account.Session is null)
        {
            _chatPollTimer.Stop();
            ChatMessages.Clear();
            ChatStatus = "Sign in with BAV to join the Crew Lounge.";
        }
        else
        {
            ChatStatus = "Refresh the lounge when you are ready to check in with the crew.";
        }

        OnPropertyChanged(nameof(HasChatMessages));
        OnPropertyChanged(nameof(ChatConnectionLabel));
        OnPropertyChanged(nameof(CanSendCrewMessage));
    }

    private async Task RefreshCrewChatAsync(bool manualRefresh)
    {
        if (IsChatRefreshing) return;
        if (_account.Session is not { } account)
        {
            ChatStatus = "Sign in with BAV to join the Crew Lounge.";
            return;
        }

        IsChatRefreshing = true;
        try
        {
            var messages = await _apiClient.GetCrewRoomChatMessagesAsync(_settings, account);
            ChatMessages.Clear();
            foreach (var message in messages.OrderBy(message => message.CreatedAt).TakeLast(60))
            {
                ChatMessages.Add(new CrewRoomChatMessage(message, account.PilotId));
            }

            ChatStatus = ChatMessages.Count == 0
                ? "The lounge is quiet for the moment. Be the first to say hello."
                : "BAV-backed crew chat · refreshed just now.";
            _chatPollTimer.Start();
        }
        catch (Exception exception)
        {
            ChatStatus = DescribeChatFailure(exception);
            if (manualRefresh) throw;
        }
        finally
        {
            IsChatRefreshing = false;
            OnPropertyChanged(nameof(HasChatMessages));
        }
    }

    private async Task SendCrewChatMessageAsync()
    {
        if (_account.Session is not { } account)
        {
            throw new InvalidOperationException("Sign in with BAV before sending a Crew Lounge message.");
        }

        var body = MessageDraft.Trim();
        if (body.Length == 0) throw new InvalidOperationException("Write a message before sending it to the Crew Lounge.");
        if (body.Length > 600) throw new InvalidOperationException("Crew Lounge messages are limited to 600 characters.");

        IsChatRefreshing = true;
        try
        {
            var sentMessage = await _apiClient.SendCrewRoomChatMessageAsync(_settings, account, body);
            MessageDraft = string.Empty;
            ChatMessages.Add(new CrewRoomChatMessage(sentMessage, account.PilotId));
            ChatStatus = "Your note has reached the Crew Lounge.";
            _chatPollTimer.Start();
        }
        finally
        {
            IsChatRefreshing = false;
            OnPropertyChanged(nameof(HasChatMessages));
        }
    }

    private void HandleChatPollTimerTick(object? sender, EventArgs e) => _ = RefreshCrewChatAsync(manualRefresh: false);

    private static string DescribeChatFailure(Exception exception) => exception is FleetApiException { StatusCode: System.Net.HttpStatusCode.NotFound }
        ? "Crew Lounge chat is being brought online by BAV. No message was saved on this PC."
        : exception is FleetApiException { StatusCode: System.Net.HttpStatusCode.TooManyRequests }
            ? "The lounge is taking a breather—please wait a moment before trying again."
            : exception.Message;

    private void ToggleEventFollow(object? parameter)
    {
        if (parameter is not CrewRoomEvent selected) return;

        selected.IsFollowed = !selected.IsFollowed;
        if (selected.IsFollowed) _followedEventIds.Add(selected.Id); else _followedEventIds.Remove(selected.Id);

        var saved = _registrationStore.TrySave(_followedEventIds);
        LocalFollowStatus = saved
            ? selected.IsFollowed
                ? $"{selected.Title} is now saved in this Crew Lounge on this PC."
                : $"{selected.Title} was removed from this Crew Lounge on this PC."
            : "Ember could not save this preference on this PC. Your BAV event registration is unaffected.";
        OnPropertyChanged(nameof(FollowingEventCount));
        OnPropertyChanged(nameof(FollowSummary));
    }

    private static void OpenBavEvents()
    {
        try
        {
            Process.Start(new ProcessStartInfo($"{AppSettings.BritishAirwaysVirtualWebsiteUrl}/events") { UseShellExecute = true });
        }
        catch
        {
            // The Crew Lounge remains usable when Windows cannot open a browser.
        }
    }

    private static IReadOnlyList<CrewRoomEventSeed> BuildCalendar(DateOnly today)
    {
        var daysUntilFriday = ((int)DayOfWeek.Friday - (int)today.DayOfWeek + 7) % 7;
        var firstEventDate = today.AddDays(daysUntilFriday);
        var firstRotationIndex = PositiveModulo((firstEventDate.DayNumber - CalendarAnchor.DayNumber) / 7, CalendarRotation.Length);

        return Enumerable.Range(0, 4)
            .Select(offset =>
            {
                var date = firstEventDate.AddDays(offset * 7);
                var template = CalendarRotation[(firstRotationIndex + offset) % CalendarRotation.Length];
                var communityTarget = 72 + PositiveModulo(date.DayNumber * 19 + offset * 37, 161);
                return new CrewRoomEventSeed(
                    $"ember-calendar-{date:yyyyMMdd}-{template.Id}", template.Title, template.TypeLabel, date, template.TimeLabel,
                    template.RouteLabel, template.RouteDetail, template.AircraftNote, communityTarget, template.RewardsLabel, template.BadgeLabel);
            })
            .ToArray();
    }

    private static int PositiveModulo(int value, int divisor)
    {
        var remainder = value % divisor;
        return remainder < 0 ? remainder + divisor : remainder;
    }
}

public sealed class CrewRoomChatMessage
{
    public CrewRoomChatMessage(FleetCrewRoomChatMessageDto message, string currentPilotId)
    {
        Id = message.Id;
        PilotId = message.PilotId;
        PilotName = message.PilotName;
        PilotNumber = message.PilotNumber;
        Rank = string.IsNullOrWhiteSpace(message.Rank) ? "BAV pilot" : message.Rank;
        Body = message.Body;
        IsOwnMessage = string.Equals(message.PilotId, currentPilotId, StringComparison.Ordinal);
        Initials = string.Concat(message.PilotName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(part => char.ToUpperInvariant(part[0])));
        if (string.IsNullOrWhiteSpace(Initials)) Initials = "BA";
        TimeLabel = FormatTimestamp(message.CreatedAt);
    }

    public string Id { get; }
    public string PilotId { get; }
    public string PilotName { get; }
    public string PilotNumber { get; }
    public string Rank { get; }
    public string Body { get; }
    public string Initials { get; }
    public string TimeLabel { get; }
    public bool IsOwnMessage { get; }

    private static string FormatTimestamp(DateTimeOffset timestamp)
    {
        var age = DateTimeOffset.UtcNow - timestamp.ToUniversalTime();
        if (age < TimeSpan.FromMinutes(1)) return "just now";
        if (age < TimeSpan.FromHours(1)) return $"{Math.Max(1, (int)age.TotalMinutes)}m ago";
        if (age < TimeSpan.FromDays(1)) return $"{Math.Max(1, (int)age.TotalHours)}h ago";
        return timestamp.ToLocalTime().ToString("ddd, HH:mm", CultureInfo.InvariantCulture);
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
        PilotsLabel = $"Calendar group · {seed.PilotCount:N0} pilots";
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
            if (!SetProperty(ref _isFollowed, value)) return;
            OnPropertyChanged(nameof(FollowButtonLabel));
            OnPropertyChanged(nameof(FollowStateLabel));
            OnPropertyChanged(nameof(FollowStateColor));
        }
    }

    public string FollowButtonLabel => IsFollowed ? "Following" : "Follow event";
    public string FollowStateLabel => IsFollowed ? "Saved in Crew Lounge" : PilotsLabel;
    public string FollowStateColor => IsFollowed ? "#A9E39B" : "#D9B66B";
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
            "active" or "airborne" or "in progress" => "#A9E39B",
            "delayed" => "#F0B66A",
            "cancelled" => "#E9848D",
            _ => "#A9CDEE"
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
            "success" => "#A9E39B",
            "warning" => "#F0B66A",
            "danger" or "error" => "#E9848D",
            _ => "#A9CDEE"
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

public sealed record CrewRoomEventSeed(string Id, string Title, string TypeLabel, DateOnly Date, string TimeLabel, string RouteLabel, string RouteDetail, string AircraftNote, int PilotCount, string RewardsLabel, string BadgeLabel);
public sealed record CrewRoomEventTemplate(string Id, string Title, string TypeLabel, string TimeLabel, string RouteLabel, string RouteDetail, string AircraftNote, string RewardsLabel, string BadgeLabel);
