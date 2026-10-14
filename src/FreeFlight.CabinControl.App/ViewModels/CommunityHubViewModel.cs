using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Input;
using System.Windows.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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
    private readonly AppSettings _settings;
    private readonly FleetApiClient _apiClient;
    private readonly CabinAccountViewModel _account;
    private readonly RelayCommand _refreshCommand;
    private readonly DispatcherTimer _chatPollTimer;
    private bool _isChatRefreshing;
    private string _messageDraft = string.Empty;
    private string _chatStatus = "Sign in with BAV to pull up a chair in the Crew Lounge.";
    private string _hostName = "BAV Community";
    private string _hostMessage = "The Crew Lounge is open. Settle in, share a route idea, or leave a friendly note for a fellow pilot.";
    private string _welcomePrompt = "What would you like to fly—or talk about—next?";
    private string _planningStatus = "Choose a BAV event table to leave an optional plan for the crew.";
    private CrewRoomEvent _featuredEvent = CrewRoomEvent.General;
    private CrewRoomEvent _activeTable = CrewRoomEvent.General;
    private string? _featuredHeroRevision;
    private ImageSource? _featuredHeroImageSource;
    // BAV returns current portraits alongside the authenticated room response.
    // Keep them in memory only; they are never copied into message history.
    private readonly Dictionary<string, ImageSource?> _chatPortraits = new(StringComparer.Ordinal);

    public CommunityHubViewModel(
        AppSettings settings,
        FleetApiClient apiClient,
        CabinAccountViewModel account,
        string settingsDirectory)
        : base("Crew Room", "A place to arrive, share a plan, and fly alongside the British Airways Virtual community")
    {
        _settings = settings;
        _apiClient = apiClient;
        _account = account;
        _ = settingsDirectory;
        Events = [];
        LiveOperations = [];
        Notices = [];
        ChatMessages = [];
        Tablemates = [];

        SelectCrewTableCommand = new RelayCommand(SelectCrewTable);
        _refreshCommand = new RelayCommand(
            _ => _account.RefreshWebsiteFlightCommand.Execute(null),
            _ => _account.RefreshWebsiteFlightCommand.CanExecute(null));
        RefreshCommand = _refreshCommand;
        RefreshChatCommand = new AsyncRelayCommand(
            () => RefreshCrewRoomAsync(manualRefresh: true),
            exception => ChatStatus = DescribeCrewRoomFailure(exception));
        SendChatMessageCommand = new AsyncRelayCommand(
            SendCrewChatMessageAsync,
            exception => ChatStatus = DescribeCrewRoomFailure(exception));
        MarkPlanningCommand = new AsyncRelayCommand(
            MarkPlanningAsync,
            exception => PlanningStatus = DescribeCrewRoomFailure(exception));
        RemovePlanningCommand = new AsyncRelayCommand(
            RemovePlanningAsync,
            exception => PlanningStatus = DescribeCrewRoomFailure(exception));
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
        UpdateCrewRoomAccountState();
    }

    public ObservableCollection<CrewRoomEvent> Events { get; }
    public ObservableCollection<CrewRoomOperation> LiveOperations { get; }
    public ObservableCollection<CrewRoomNotice> Notices { get; }
    public ObservableCollection<CrewRoomChatMessage> ChatMessages { get; }
    public ObservableCollection<CrewRoomIntent> Tablemates { get; }

    public CrewRoomEvent FeaturedEvent => _featuredEvent;
    public CrewRoomEvent ActiveTable => _activeTable;
    public ImageSource? FeaturedHeroImageSource
    {
        get => _featuredHeroImageSource;
        private set => SetProperty(ref _featuredHeroImageSource, value);
    }
    public ICommand SelectCrewTableCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand RefreshChatCommand { get; }
    public ICommand SendChatMessageCommand { get; }
    public ICommand MarkPlanningCommand { get; }
    public ICommand RemovePlanningCommand { get; }
    public ICommand OpenBavEventsCommand { get; }

    public string PilotName => _account.DisplayName;
    public string PilotLabel => _account.PilotLabel;
    public string PilotInitials => string.IsNullOrWhiteSpace(_account.Initials) ? "BA" : _account.Initials;
    /// <summary>
    /// The authenticated pilot's locally decoded BAV portrait. The Crew Lounge
    /// deliberately reuses the account view model's in-memory image rather
    /// than opening an unauthenticated URL or copying profile media to a
    /// community endpoint.
    /// </summary>
    public ImageSource? PilotAvatarImageSource => _account.ProfileImageSource;
    public bool HasPilotAvatarImage => PilotAvatarImageSource is not null;
    public string ConnectionLabel => _account.IsAcarsOperating ? "ACARS connected" : "BAV account connected";
    public string ConnectionDetail => _account.IsAcarsOperating ? _account.AcarsSessionLabel : "Your BAV identity is ready to use in the Crew Lounge.";
    public string ActiveAssignmentLabel => _account.WebsiteFlightAssignmentLabel;
    public string ActiveAssignmentDetail => _account.WebsiteFlightAssignmentDetail;
    public string CurrentAircraftLabel => _account.WebsiteFlightAssignment is { Aircraft: { Length: > 0 } aircraft }
        ? aircraft
        : "Choose a BAV flight to add an aircraft";
    public string WelcomeLabel => string.IsNullOrWhiteSpace(PilotName)
        ? "There’s a seat for you here."
        : $"Welcome, {PilotName.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? PilotName}. There’s a seat for you here.";
    public string ArrivalTitle => FeaturedEvent.IsGeneral ? "Start wherever you are." : FeaturedEvent.Title;
    public string ArrivalDetail => FeaturedEvent.IsGeneral
        ? "A low-pressure place for route ideas, pre-flight questions, and the small moments between flights."
        : "The event table is open for pilots who would like to compare plans before they fly.";
    public string HostName => _hostName;
    public string HostMessage => _hostMessage;
    public string WelcomePrompt => _welcomePrompt;
    public string ActiveTableTitle => ActiveTable.IsGeneral ? "The shared crew table" : ActiveTable.Title;
    public string ActiveTableCaption => ActiveTable.IsGeneral ? "THE LONG TABLE" : "BAV EVENT TABLE";
    public string ActiveTableDescription => ActiveTable.IsGeneral
        ? "Pull up a chair—compare routes, share a quick hello, or talk through the flight ahead."
        : "A relaxed conversation table for pilots considering this BAV calendar event. Official registration stays on BAV.";
    public string ChatEmptyTitle => ActiveTable.IsGeneral ? "The lounge is quiet for now." : "This table is quiet for now.";
    public string ChatEmptyDetail => _account.Session is null
        ? "Sign in with BAV to pull up a chair and join the conversation."
        : ActiveTable.IsGeneral
            ? "Start with a hello, a route idea, or a question for the crew."
            : "Say hello to pilots considering this event. Every plan here is optional.";
    public string MessagePrompt => ActiveTable.IsGeneral
        ? "Say hello, share a route, or ask who is flying."
        : "Share a route idea or a quick note for this event.";
    public string PlanningBoardHeading => IsEventTable ? "Who’s thinking of flying?" : "Make a loose plan";
    public string TablemateEmptyMessage => IsEventTable
        ? "No voluntary plans here yet. Leave yours if you would like the crew to know you are considering this flight."
        : "Choose an event table to see voluntary plans from pilots who want to share them.";
    public string CommunityPulseEmptyTitle => "No live crew flights are listed right now.";
    public string CommunityPulseEmptyDetail => "We only show flights returned by BAV Operations, so the board stays honest when there is nothing live to share.";

    public int LiveOperationCount => LiveOperations.Count;
    public int EventCount => Events.Count;
    public int NoticeCount => Notices.Count;
    public int TablemateCount => Tablemates.Count;
    public bool HasLiveOperations => LiveOperations.Count > 0;
    public bool HasNotices => Notices.Count > 0;
    public bool IsRefreshing => _account.IsBusy;
    public bool HasChatMessages => ChatMessages.Count > 0;
    public bool HasTablemates => Tablemates.Count > 0;
    public bool IsChatRefreshing
    {
        get => _isChatRefreshing;
        private set
        {
            if (!SetProperty(ref _isChatRefreshing, value)) return;
            OnPropertyChanged(nameof(ChatConnectionLabel));
            OnPropertyChanged(nameof(CanSendCrewMessage));
            OnPropertyChanged(nameof(CanPlanEvent));
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
    public bool IsEventTable => !ActiveTable.IsGeneral;
    public bool IsPlanning => IsEventTable && _account.Session is { } account && Tablemates.Any(item => string.Equals(item.PilotId, account.PilotId, StringComparison.Ordinal));
    public bool CanPlanEvent => IsEventTable && _account.Session is not null && !IsChatRefreshing;
    public string PlanningButtonLabel => IsPlanning ? "My plan is visible" : "I’m thinking of flying";
    public string PlanningBoardLabel => IsEventTable
        ? "A loose plan for fellow pilots—not a roster or BAV registration."
        : "Open an event table to see who is considering a flight.";

    public string ChatConnectionLabel => _account.Session is null
        ? "Sign in to join"
        : IsChatRefreshing
            ? "Refreshing the room"
            : ActiveTable.IsGeneral ? "Crew conversation" : "Event conversation";

    public string LiveOperationsSummary => HasLiveOperations
        ? $"{LiveOperationCount} flight{(LiveOperationCount == 1 ? string.Empty : "s")} returned by BAV Operations"
        : "No live crew flights returned by BAV Operations just now.";

    public string EventSummary => EventCount switch
    {
        0 => "No published BAV events right now",
        1 => "1 published BAV event",
        _ => $"{EventCount} published BAV events"
    };

    public string NoticeSummary => _account.NotificationsSummary;

    public string PlanningStatus
    {
        get => _planningStatus;
        private set => SetProperty(ref _planningStatus, value);
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
        _chatPortraits.Clear();
        RefreshIdentity();
        RefreshLiveOperations();
        RefreshNotices();
        UpdateCrewRoomAccountState();
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

        if (e.PropertyName is nameof(CabinAccountViewModel.WebsiteFlightAssignment)
            or nameof(CabinAccountViewModel.DisplayName)
            or nameof(CabinAccountViewModel.PilotLabel)
            or nameof(CabinAccountViewModel.Initials)
            or nameof(CabinAccountViewModel.ProfileImageSource)
            or nameof(CabinAccountViewModel.HasProfileImage))
        {
            RefreshIdentity();
            OnPropertyChanged(nameof(IsPlanning));
        }
    }

    private void RefreshIdentity()
    {
        OnPropertyChanged(nameof(PilotName));
        OnPropertyChanged(nameof(PilotLabel));
        OnPropertyChanged(nameof(PilotInitials));
        OnPropertyChanged(nameof(PilotAvatarImageSource));
        OnPropertyChanged(nameof(HasPilotAvatarImage));
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

    private void UpdateCrewRoomAccountState()
    {
        if (_account.Session is null)
        {
            _chatPollTimer.Stop();
            ChatMessages.Clear();
            Tablemates.Clear();
            _chatPortraits.Clear();
            ChatStatus = "Sign in with BAV to pull up a chair in the Crew Lounge.";
            PlanningStatus = "Sign in with BAV to leave an optional event plan for the crew.";
        }
        else
        {
            ChatStatus = "Opening the Crew Lounge…";
            _ = RefreshCrewRoomAsync(manualRefresh: false);
        }

        RaiseTableProperties();
        OnPropertyChanged(nameof(HasChatMessages));
        OnPropertyChanged(nameof(ChatConnectionLabel));
        OnPropertyChanged(nameof(CanSendCrewMessage));
    }

    private async Task RefreshCrewRoomAsync(bool manualRefresh)
    {
        if (IsChatRefreshing) return;
        if (_account.Session is not { } account)
        {
            ChatStatus = "Sign in with BAV to pull up a chair in the Crew Lounge.";
            return;
        }

        IsChatRefreshing = true;
        try
        {
            var experience = await _apiClient.GetCrewRoomExperienceAsync(_settings, account);
            await ApplyExperienceAsync(experience, account);
            await RefreshActiveTableContentAsync(account);
            _chatPollTimer.Start();
        }
        catch (Exception exception)
        {
            ChatStatus = DescribeCrewRoomFailure(exception);
            if (manualRefresh) throw;
        }
        finally
        {
            IsChatRefreshing = false;
            OnPropertyChanged(nameof(HasChatMessages));
        }
    }

    private async Task ApplyExperienceAsync(FleetCrewRoomExperienceDto experience, FleetAccountSession account)
    {
        var priorTableId = ActiveTable.Id;
        Events.Clear();
        foreach (var eventDto in experience.Events ?? [])
        {
            Events.Add(new CrewRoomEvent(eventDto));
        }

        var settings = experience.Settings;
        _hostName = string.IsNullOrWhiteSpace(settings?.HostName) ? "BAV Community" : settings.HostName;
        _hostMessage = string.IsNullOrWhiteSpace(settings?.HostMessage)
            ? "The Crew Lounge is open. Settle in, share a route idea, or leave a friendly note for a fellow pilot."
            : settings.HostMessage;
        _welcomePrompt = string.IsNullOrWhiteSpace(settings?.WelcomePrompt) ? "What would you like to fly—or talk about—next?" : settings.WelcomePrompt;

        _featuredEvent = experience.FeaturedEvent is null ? CrewRoomEvent.General : new CrewRoomEvent(experience.FeaturedEvent);
        var retained = Events.FirstOrDefault(item => string.Equals(item.Id, priorTableId, StringComparison.Ordinal));
        _activeTable = retained ?? (_featuredEvent.IsGeneral ? CrewRoomEvent.General : _featuredEvent);

        OnPropertyChanged(nameof(FeaturedEvent));
        OnPropertyChanged(nameof(HostName));
        OnPropertyChanged(nameof(HostMessage));
        OnPropertyChanged(nameof(WelcomePrompt));
        OnPropertyChanged(nameof(EventCount));
        OnPropertyChanged(nameof(EventSummary));
        RaiseTableProperties();
        await RefreshFeaturedHeroAsync(experience.FeaturedHero, account);
    }

    private async Task RefreshFeaturedHeroAsync(FleetCrewRoomEventHeroDto? hero, FleetAccountSession account)
    {
        var revision = hero is not null && string.Equals(hero.EventId, FeaturedEvent.Id, StringComparison.Ordinal)
            ? hero.EventId + ":" + hero.UpdatedAt
            : null;
        if (string.Equals(_featuredHeroRevision, revision, StringComparison.Ordinal)) return;

        _featuredHeroRevision = revision;
        FeaturedHeroImageSource = null;
        if (revision is null) return;

        try
        {
            var bytes = await _apiClient.GetFeaturedCrewRoomHeroAsync(_settings, account);
            if (!string.Equals(_featuredHeroRevision, revision, StringComparison.Ordinal)) return;
            FeaturedHeroImageSource = LoadCrewRoomHeroImage(bytes);
        }
        catch (Exception)
        {
            // The warm default hero remains available when staff media cannot be reached.
        }
    }

    private static ImageSource? LoadCrewRoomHeroImage(byte[]? bytes)
    {
        if (bytes is not { Length: > 0 and <= 5 * 1024 * 1024 }) return null;
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }

    private void CacheChatPortraits(IEnumerable<FleetCrewRoomChatPortraitDto> portraits, FleetAccountSession account)
    {
        _chatPortraits[account.PilotId] = PilotAvatarImageSource;
        foreach (var portrait in portraits)
        {
            if (string.IsNullOrWhiteSpace(portrait.PilotId) || string.Equals(portrait.PilotId, account.PilotId, StringComparison.Ordinal)) continue;
            _chatPortraits[portrait.PilotId] = LoadCrewRoomProfileImage(portrait.ProfileImage);
        }
    }

    private ImageSource? GetChatPortrait(string pilotId, FleetAccountSession account) =>
        string.Equals(pilotId, account.PilotId, StringComparison.Ordinal)
            ? PilotAvatarImageSource
            : _chatPortraits.TryGetValue(pilotId, out var portrait) ? portrait : null;

    private static ImageSource? LoadCrewRoomProfileImage(string? dataUrl)
    {
        const string Prefix = "data:image/webp;base64,";
        if (string.IsNullOrWhiteSpace(dataUrl) || dataUrl.Length > 350_000 || !dataUrl.StartsWith(Prefix, StringComparison.Ordinal)) return null;

        try
        {
            using var stream = new MemoryStream(Convert.FromBase64String(dataUrl[Prefix.Length..]));
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

    private async Task RefreshActiveTableContentAsync(FleetAccountSession account)
    {
        var chat = await _apiClient.GetCrewRoomChatMessagesAsync(_settings, account, ActiveTable.TableChannel);
        CacheChatPortraits(chat.Portraits, account);
        ChatMessages.Clear();
        foreach (var message in chat.Messages.OrderBy(message => message.CreatedAt).TakeLast(60))
        {
            ChatMessages.Add(new CrewRoomChatMessage(message, account.PilotId, GetChatPortrait(message.PilotId, account)));
        }

        ChatStatus = ChatMessages.Count == 0
            ? $"{ChatEmptyTitle} {ChatEmptyDetail}"
            : $"{ActiveTableCaption} is up to date with the latest BAV-backed messages.";

        Tablemates.Clear();
        if (IsEventTable)
        {
            var intentions = await _apiClient.GetCrewRoomIntentsAsync(_settings, account, ActiveTable.Id);
            foreach (var intention in intentions)
            {
                Tablemates.Add(new CrewRoomIntent(intention, account.PilotId));
            }
            PlanningStatus = IsPlanning
                ? "Your loose plan is visible to the crew. It is not a BAV registration or roster."
                : "Leave a loose plan if you would like other pilots to find you here. This never registers you for the event.";
        }
        else
        {
            PlanningStatus = "Open a BAV event table when you are ready to leave a loose plan for the crew.";
        }
        RaiseTableProperties();
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
            var sentMessage = await _apiClient.SendCrewRoomChatMessageAsync(_settings, account, ActiveTable.TableChannel, body);
            MessageDraft = string.Empty;
            _chatPortraits[account.PilotId] = PilotAvatarImageSource;
            ChatMessages.Add(new CrewRoomChatMessage(sentMessage, account.PilotId, GetChatPortrait(sentMessage.PilotId, account)));
            ChatStatus = "Your note is at the table.";
            _chatPollTimer.Start();
        }
        finally
        {
            IsChatRefreshing = false;
            OnPropertyChanged(nameof(HasChatMessages));
        }
    }

    private async void SelectCrewTable(object? parameter)
    {
        if (parameter is not CrewRoomEvent selected || string.Equals(selected.Id, ActiveTable.Id, StringComparison.Ordinal)) return;
        if (_account.Session is not { } account)
        {
            ChatStatus = "Sign in with BAV to pull up a chair at this table.";
            return;
        }
        if (IsChatRefreshing) return;

        _activeTable = selected;
        ChatMessages.Clear();
        Tablemates.Clear();
        RaiseTableProperties();
        IsChatRefreshing = true;
        try
        {
            await RefreshActiveTableContentAsync(account);
        }
        catch (Exception exception)
        {
            ChatStatus = DescribeCrewRoomFailure(exception);
        }
        finally
        {
            IsChatRefreshing = false;
        }
    }

    private async Task MarkPlanningAsync()
    {
        if (_account.Session is not { } account) throw new InvalidOperationException("Sign in with BAV before sharing an event plan.");
        if (!IsEventTable) throw new InvalidOperationException("Open a BAV event table before sharing a plan.");

        IsChatRefreshing = true;
        try
        {
            var assignment = _account.WebsiteFlightAssignment;
            await _apiClient.SetCrewRoomIntentAsync(
                _settings, account, ActiveTable.Id,
                assignment?.FlightNumber,
                assignment is null ? null : $"{assignment.From} → {assignment.To}",
                assignment?.Aircraft);
            await RefreshActiveTableContentAsync(account);
        }
        finally
        {
            IsChatRefreshing = false;
        }
    }

    private async Task RemovePlanningAsync()
    {
        if (_account.Session is not { } account) throw new InvalidOperationException("Sign in with BAV before updating your event plan.");
        if (!IsEventTable) return;

        IsChatRefreshing = true;
        try
        {
            await _apiClient.RemoveCrewRoomIntentAsync(_settings, account, ActiveTable.Id);
            await RefreshActiveTableContentAsync(account);
            PlanningStatus = "Your loose plan has been cleared from this table. Your BAV registration was never changed.";
        }
        finally
        {
            IsChatRefreshing = false;
        }
    }

    private void RaiseTableProperties()
    {
        OnPropertyChanged(nameof(ActiveTable));
        OnPropertyChanged(nameof(ActiveTableTitle));
        OnPropertyChanged(nameof(ActiveTableCaption));
        OnPropertyChanged(nameof(ActiveTableDescription));
        OnPropertyChanged(nameof(ArrivalTitle));
        OnPropertyChanged(nameof(ArrivalDetail));
        OnPropertyChanged(nameof(ChatEmptyTitle));
        OnPropertyChanged(nameof(ChatEmptyDetail));
        OnPropertyChanged(nameof(MessagePrompt));
        OnPropertyChanged(nameof(IsEventTable));
        OnPropertyChanged(nameof(TablemateCount));
        OnPropertyChanged(nameof(HasTablemates));
        OnPropertyChanged(nameof(IsPlanning));
        OnPropertyChanged(nameof(CanPlanEvent));
        OnPropertyChanged(nameof(PlanningButtonLabel));
        OnPropertyChanged(nameof(PlanningBoardLabel));
        OnPropertyChanged(nameof(PlanningBoardHeading));
        OnPropertyChanged(nameof(TablemateEmptyMessage));
    }

    private void HandleChatPollTimerTick(object? sender, EventArgs e) => _ = RefreshCrewRoomAsync(manualRefresh: false);

    private static string DescribeCrewRoomFailure(Exception exception) => exception is FleetApiException { StatusCode: System.Net.HttpStatusCode.NotFound }
        ? "This Crew Table is being opened by BAV. No message or plan was saved on this PC."
        : exception is FleetApiException { StatusCode: System.Net.HttpStatusCode.TooManyRequests }
            ? "The lounge is taking a breather—please wait a moment before trying again."
            : exception.Message;

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

}
public sealed class CrewRoomChatMessage
{
    public CrewRoomChatMessage(FleetCrewRoomChatMessageDto message, string currentPilotId, ImageSource? avatarImageSource)
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
        AvatarImageSource = avatarImageSource;
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
    public ImageSource? AvatarImageSource { get; }
    public bool HasAvatarImage => AvatarImageSource is not null;

    private static string FormatTimestamp(DateTimeOffset timestamp)
    {
        var age = DateTimeOffset.UtcNow - timestamp.ToUniversalTime();
        if (age < TimeSpan.FromMinutes(1)) return "just now";
        if (age < TimeSpan.FromHours(1)) return $"{Math.Max(1, (int)age.TotalMinutes)}m ago";
        if (age < TimeSpan.FromDays(1)) return $"{Math.Max(1, (int)age.TotalHours)}h ago";
        return timestamp.ToLocalTime().ToString("ddd, HH:mm", CultureInfo.InvariantCulture);
    }
}

public sealed class CrewRoomEvent
{
    public static readonly CrewRoomEvent General = new("crew-lounge", "The general Crew Lounge", "Community space", "BAV crew space", "Live conversation", "", "", "", "", "", "crew-lounge", true);

    public CrewRoomEvent(FleetCrewRoomEventDto source)
        : this(
            source.Id,
            source.Title,
            source.TypeLabel,
            FormatDate(source.Date),
            FormatTime(source.StartUtc, source.EndUtc),
            source.Route is null ? "BAV calendar event" : $"{source.Route.From} → {source.Route.To}",
            source.Route is null ? "Open the BAV calendar for route details." : $"{source.Route.FromName ?? source.Route.From} to {source.Route.ToName ?? source.Route.To}",
            source.AircraftNote ?? "All aircraft welcome",
            source.Rewards is null ? "" : $"+{source.Rewards.VaPoints} VA · +{source.Rewards.TierPoints} Tier",
            source.Rewards?.Badge ?? "",
            source.TableChannel,
            false)
    {
    }

    private CrewRoomEvent(string id, string title, string typeLabel, string dateLabel, string timeLabel, string routeLabel, string routeDetail, string aircraftNote, string rewardsLabel, string badgeLabel, string tableChannel, bool isGeneral)
    {
        Id = id;
        Title = title;
        TypeLabel = typeLabel;
        DateLabel = dateLabel;
        TimeLabel = timeLabel;
        RouteLabel = routeLabel;
        RouteDetail = routeDetail;
        AircraftNote = aircraftNote;
        RewardsLabel = rewardsLabel;
        BadgeLabel = badgeLabel;
        TableChannel = tableChannel;
        IsGeneral = isGeneral;
    }

    public string Id { get; }
    public string Title { get; }
    public string TypeLabel { get; }
    public string DateLabel { get; }
    public string TimeLabel { get; }
    public string RouteLabel { get; }
    public string RouteDetail { get; }
    public string AircraftNote { get; }
    public string RewardsLabel { get; }
    public string BadgeLabel { get; }
    public string TableChannel { get; }
    public bool IsGeneral { get; }
    public string TableButtonLabel => IsGeneral ? "Open lounge" : "Open table";
    public string TableStateLabel => IsGeneral ? "Shared BAV conversation" : "Event planning and crew chat";
    public string TableStateColor => IsGeneral ? "#A9CDEE" : "#A9E39B";

    private static string FormatDate(string date) => DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
        ? parsed.ToString("ddd, dd MMM", CultureInfo.InvariantCulture).ToUpperInvariant()
        : date;

    private static string FormatTime(string start, string end) => string.IsNullOrWhiteSpace(start)
        ? "See BAV calendar"
        : string.IsNullOrWhiteSpace(end) ? $"{start} UTC" : $"{start} – {end} UTC";
}
public sealed class CrewRoomIntent
{
    public CrewRoomIntent(FleetCrewRoomIntentDto source, string currentPilotId)
    {
        PilotId = source.PilotId;
        PilotName = source.PilotName;
        PilotNumber = source.PilotNumber;
        Rank = string.IsNullOrWhiteSpace(source.Rank) ? "BAV pilot" : source.Rank;
        IsCurrentPilot = string.Equals(source.PilotId, currentPilotId, StringComparison.Ordinal);
        Initials = string.Concat(PilotName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(part => char.ToUpperInvariant(part[0])));
        if (string.IsNullOrWhiteSpace(Initials)) Initials = "BA";
        PlanDetail = string.Join(" · ", new[] { source.FlightNumber, source.Route, source.Aircraft }.Where(item => !string.IsNullOrWhiteSpace(item)));
        if (string.IsNullOrWhiteSpace(PlanDetail)) PlanDetail = "Planning to join the crew";
        UpdatedLabel = FormatTimestamp(source.UpdatedAt);
    }

    public string PilotId { get; }
    public string PilotName { get; }
    public string PilotNumber { get; }
    public string Rank { get; }
    public string Initials { get; }
    public string PlanDetail { get; }
    public string UpdatedLabel { get; }
    public bool IsCurrentPilot { get; }

    private static string FormatTimestamp(DateTimeOffset timestamp)
    {
        var age = DateTimeOffset.UtcNow - timestamp.ToUniversalTime();
        if (age < TimeSpan.FromMinutes(1)) return "checked in just now";
        if (age < TimeSpan.FromHours(1)) return $"checked in {Math.Max(1, (int)age.TotalMinutes)}m ago";
        return $"checked in {timestamp.ToLocalTime():dd MMM}";
    }
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
