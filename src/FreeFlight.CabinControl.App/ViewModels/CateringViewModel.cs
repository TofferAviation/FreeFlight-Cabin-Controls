using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Input;
using System.Windows.Threading;
using FreeFlight.CabinControl.App.Infrastructure;
using FreeFlight.CabinControl.App.Services;
using FreeFlight.CabinControl.Core.Passengers;

namespace FreeFlight.CabinControl.App.ViewModels;

public sealed class CateringViewModel : PageViewModel, IDisposable
{
    private readonly PassengerFlowViewModel _passengers;
    private readonly DispatcherTimer _timer;
    private readonly string? _settingsDirectory;
    private readonly HashSet<int> _servedPassengerIds = [];
    private readonly HashSet<int> _highLifeCafePurchaserIds = [];
    private CateringMenuPack _pack;
    private CateringProfileSelection _selection;
    private string _selectedTab = "Menu";
    private string _selectedCabin = string.Empty;
    private string _selectedCourse = "All";
    private string _selectedOnboardMenuRoute = "ShortHaul";
    private string _flightKey = string.Empty;
    private string _inventoryStatus = "Awaiting flight catering profile";
    private string _serviceStatus = "Catering is ready to load";
    private string _currentServiceTitle = "Service not started";
    private string _nextServiceTitle = "Import SimBrief to build the service plan";
    private double _serviceProgress;
    private bool _autoProgress = true;
    private bool _serviceManuallyHeld;
    private bool _isTimingEditorVisible;
    private int _serviceTimingOffsetMinutes;
    private double _timelineProgress;
    private CateringPassengerPreferenceViewModel? _selectedPassengerPreference;

    public CateringViewModel(PassengerFlowViewModel passengers, string? settingsDirectory = null)
        : base("Catering & Meal Service", "British Airways dining selected for the live flight, cabin and service time")
    {
        _passengers = passengers;
        _settingsDirectory = settingsDirectory;
        _pack = CateringMenuPackLoader.Load(settingsDirectory, DateTimeOffset.Now);
        _selection = BritishAirwaysCateringProfileSelector.Select(CreateFlightContext());
        SelectTabCommand = new RelayCommand(parameter => SelectedTab = parameter?.ToString() ?? "Menu");
        SelectCabinCommand = new RelayCommand(parameter => SelectCabin(parameter?.ToString()));
        SelectCourseCommand = new RelayCommand(parameter => SelectCourse(parameter?.ToString()));
        SelectOnboardMenuRouteCommand = new RelayCommand(parameter => SelectOnboardMenuRoute(parameter?.ToString()));
        SelectPassengerCommand = new RelayCommand(parameter => SelectedPassengerPreference = parameter as CateringPassengerPreferenceViewModel);
        ResetInventoryCommand = new RelayCommand(_ => ResetInventory());
        SkipServiceCommand = new RelayCommand(_ => SkipService());
        ToggleServiceHoldCommand = new RelayCommand(_ => ToggleServiceHold());
        ToggleTimingEditorCommand = new RelayCommand(_ => IsTimingEditorVisible = !IsTimingEditorVisible);
        AdvanceServiceTimingCommand = new RelayCommand(_ => AdjustServiceTiming(-15));
        DelayServiceTimingCommand = new RelayCommand(_ => AdjustServiceTiming(15));
        ResetServiceTimingCommand = new RelayCommand(_ => ResetServiceTiming());
        _passengers.PropertyChanged += HandlePassengersPropertyChanged;
        _passengers.PassengerManifest.CollectionChanged += HandleManifestCollectionChanged;
        RebuildFlightCatering(force: true);
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += HandleTimerTick;
        _timer.Start();
    }

    public ObservableCollection<CateringCabinViewModel> Cabins { get; } = [];
    public ObservableCollection<CateringCourseOptionViewModel> CourseOptions { get; } = [];
    public ObservableCollection<CateringInventoryItemViewModel> VisibleMenuItems { get; } = [];
    public ObservableCollection<CateringInventoryItemViewModel> Inventory { get; } = [];
    public ObservableCollection<CateringInventoryItemViewModel> VisibleInventory { get; } = [];
    public ObservableCollection<OnboardMenuCardViewModel> OnboardMenuCards { get; } = [];
    public ObservableCollection<CateringPassengerPreferenceViewModel> PassengerPreferences { get; } = [];
    public ObservableCollection<CateringPassengerPreferenceViewModel> VisiblePassengerPreferences { get; } = [];
    public ObservableCollection<CateringPassengerPreferenceViewModel> SpecialMeals { get; } = [];
    public ObservableCollection<CateringServicePhaseViewModel> ServicePhases { get; } = [];
    public ObservableCollection<string> ActivityLog { get; } = [];

    public ICommand SelectTabCommand { get; }
    public ICommand SelectCabinCommand { get; }
    public ICommand SelectCourseCommand { get; }
    public ICommand SelectOnboardMenuRouteCommand { get; }
    public ICommand SelectPassengerCommand { get; }
    public ICommand ResetInventoryCommand { get; }
    public ICommand SkipServiceCommand { get; }
    public ICommand ToggleServiceHoldCommand { get; }
    public ICommand ToggleTimingEditorCommand { get; }
    public ICommand AdvanceServiceTimingCommand { get; }
    public ICommand DelayServiceTimingCommand { get; }
    public ICommand ResetServiceTimingCommand { get; }

    public string SelectedTab
    {
        get => _selectedTab;
        set
        {
            if (!SetProperty(ref _selectedTab, value)) return;
            OnPropertyChanged(nameof(IsMenuVisible));
            OnPropertyChanged(nameof(IsProgressVisible));
            OnPropertyChanged(nameof(IsInventoryVisible));
            OnPropertyChanged(nameof(IsPreferencesVisible));
            OnPropertyChanged(nameof(IsSpecialMealsVisible));
            OnPropertyChanged(nameof(PageHeading));
            OnPropertyChanged(nameof(PageDescription));
        }
    }

    public bool IsMenuVisible => SelectedTab == "Menu";
    public bool IsProgressVisible => SelectedTab == "Progress";
    public bool IsInventoryVisible => SelectedTab == "Inventory";
    public bool IsPreferencesVisible => SelectedTab == "Preferences";
    public bool IsSpecialMealsVisible => SelectedTab == "Special";
    public string PageHeading => SelectedTab switch
    {
        "Progress" => "Service Progress",
        "Inventory" => "Inventory",
        "Preferences" => "Passenger Preferences",
        "Special" => "Special Meals",
        _ => "Catering & Service"
    };
    public string PageDescription => SelectedTab switch
    {
        "Progress" => "Real-time overview of all onboard services and cabin activity.",
        "Inventory" => "Manage onboard catering stock and monitor usage in real time.",
        "Preferences" => "View and manage passenger meal selections and preferences.",
        "Special" => "Manage special dietary requirements and meal requests.",
        _ when IsShortHaul => "British Airways High Life Café, tailored to this short-haul flight.",
        _ => "British Airways onboard dining, tailored to your flight."
    };

    public string SelectedCabin
    {
        get => _selectedCabin;
        private set
        {
            if (!SetProperty(ref _selectedCabin, value)) return;
            OnPropertyChanged(nameof(MenuExperienceTitle));
            OnPropertyChanged(nameof(PassengerOrdersHeading));
        }
    }
    public string SelectedCourse { get => _selectedCourse; private set => SetProperty(ref _selectedCourse, value); }
    public CateringPassengerPreferenceViewModel? SelectedPassengerPreference
    {
        get => _selectedPassengerPreference;
        private set
        {
            if (SetProperty(ref _selectedPassengerPreference, value)) OnPropertyChanged(nameof(HasSelectedPassenger));
        }
    }

    public bool HasSelectedPassenger => SelectedPassengerPreference is not null;
    public bool IsShortHaul => _selection.AircraftFamily == CateringAircraftFamily.ShortHaul;
    public string MenuExperienceTitle => IsShortHaul ? "High Life Café" : $"{SelectedCabin} – À la carte Dining";
    public string MenuExperienceSubtitle => IsShortHaul
        ? "Buy-on-board food and drinks for Euro Traveller, with Club Europe service shown alongside."
        : "Dine Anytime - a more personal dining experience at any point during your flight.";
    public string MenuHeroTitle => IsShortHaul ? "High Life Café onboard." : "Exceptional dining at 35,000 feet.";
    public string MenuHeroDescription => IsShortHaul
        ? "Browse the live cafe stock and realistic prices. Revenue is recorded only when a Euro Traveller passenger completes a purchase."
        : "A selection of seasonal dishes, inspired by the best of British produce. Dine whenever you choose, with a menu designed by world-class chefs.";
    public string MenuBrandLabel => IsShortHaul ? "HIGH LIFE CAFE" : "BRITISH AIRWAYS";
    public string ServiceNoteOne => IsShortHaul
        ? "ⓘ  High Life Café purchases are available to Euro Traveller passengers."
        : "ⓘ  First passengers can dine at any time during the flight.";
    public string ServiceNoteTwo => IsShortHaul
        ? "ⓘ  Club Europe meals and the Euro Traveller water and snack remain complimentary."
        : "ⓘ  Crew take individual orders and monitor dietary requirements.";
    public string ServiceNoteThree => IsShortHaul
        ? "ⓘ  Only paid High Life Café items are included in cabin revenue."
        : "ⓘ  A full selection of wines, champagnes and beverages is available.";
    public string ServiceNoteFour => IsShortHaul
        ? "ⓘ  Stock and passenger spend update as the café service progresses."
        : "ⓘ  Special dietary requirements are catered for.";
    public bool HasSimBriefFlight => _passengers.HasSimBriefFlight;
    public bool IsCateringLoaded => _passengers.HasPassengerManifest;
    public string FlightNumber => string.IsNullOrWhiteSpace(_passengers.ImportedFlightNumber) ? "NO OFP" : _passengers.ImportedFlightNumber;
    public string RouteLabel => string.IsNullOrWhiteSpace(_passengers.ImportedOrigin) ? "Import SimBrief to select a route" : $"{ShortAirport(_passengers.ImportedOrigin)}  →  {ShortAirport(_passengers.ImportedDestination)}";
    public string AircraftLabel => FormatAircraftName(_passengers.ImportedAircraftIcao, _passengers.SelectedCabinLayoutProfile.Name);
    public string DepartureLabel => _passengers.ImportedScheduledDepartureLocal?.ToString("dd MMM · HH:mm") ?? "Not supplied";
    public string DurationLabel => FormatDuration(CreateFlightContext().Duration);
    public string ProfileLabel => _selection.DisplayName;
    public string ServiceBandLabel => $"{_selection.ServiceBand} · {BritishAirwaysCateringProfileSelector.FormatMealPeriod(_selection.MealPeriod)}";
    public string PackLabel => $"{_pack.DisplayName} · v{_pack.Version}";
    public string CateringCycleLabel => HasSimBriefFlight
        ? $"{PackEffectiveMonth()} · {FormatRouteRegion(_selection.RouteRegion, ShortAirport(_passengers.ImportedDestination))}"
        : "Awaiting SimBrief flight data";
    public string CateringOverviewTitle => HasSimBriefFlight ? "Menus loaded for this flight" : "Import SimBrief to load catering";
    public string CateringOverviewDescription => HasSimBriefFlight
        ? $"{_pack.DisplayName} applied"
        : "Route, aircraft and departure time are required";
    public string CateringOverviewSymbol => HasSimBriefFlight ? "✓" : "!";
    public string CateringOverviewColor => HasSimBriefFlight ? "#4ED488" : "#E59A39";
    public string InventoryStatus { get => _inventoryStatus; private set => SetProperty(ref _inventoryStatus, value); }
    public string ServiceStatus { get => _serviceStatus; private set => SetProperty(ref _serviceStatus, value); }
    public string CurrentServiceTitle
    {
        get => _currentServiceTitle;
        private set
        {
            if (SetProperty(ref _currentServiceTitle, value)) OnPropertyChanged(nameof(CurrentActivityTitle));
        }
    }
    public string NextServiceTitle
    {
        get => _nextServiceTitle;
        private set
        {
            if (!SetProperty(ref _nextServiceTitle, value)) return;
            OnPropertyChanged(nameof(NextServiceDisplayTitle));
            OnPropertyChanged(nameof(NextServiceEtaLabel));
        }
    }
    public double ServiceProgress { get => _serviceProgress; private set { if (SetProperty(ref _serviceProgress, Math.Clamp(value, 0d, 100d))) OnPropertyChanged(nameof(ServiceProgressLabel)); } }
    public string ServiceProgressLabel => $"{ServiceProgress:0}%";
    public bool AutoProgress { get => _autoProgress; set => SetProperty(ref _autoProgress, value); }
    public string HoldButtonLabel => _serviceManuallyHeld ? "Resume service" : "Hold service";
    public bool IsTimingEditorVisible { get => _isTimingEditorVisible; private set => SetProperty(ref _isTimingEditorVisible, value); }
    public int ServiceTimingOffsetMinutes => _serviceTimingOffsetMinutes;
    public string ServiceTimingOffsetLabel => _serviceTimingOffsetMinutes == 0
        ? "Flight schedule"
        : $"{Math.Abs(_serviceTimingOffsetMinutes)} min {(_serviceTimingOffsetMinutes < 0 ? "earlier" : "later")}";
    public double TimelineProgress { get => _timelineProgress; private set => SetProperty(ref _timelineProgress, Math.Clamp(value, 0d, 100d)); }
    public int TotalUnits => Inventory.Sum(item => item.Quantity);
    public int LoadedUnits => Inventory.Sum(item => item.TargetQuantity);
    public int UsedUnits => Inventory.Sum(item => item.Used);
    public double InventoryUsagePercent => LoadedUnits == 0 ? 0d : UsedUnits * 100d / LoadedUnits;
    public string InventoryUsageLabel => $"{InventoryUsagePercent:0}% used";
    public int LowStockCount => Inventory.Count(item => item.IsLowStock);
    public int OutOfStockCount => Inventory.Count(item => item.Quantity == 0);
    public string TotalPassengerSpend => $"£{_passengers.TotalOnboardSpendGbp:F2}";
    public bool IsOnboardShortHaulMenuSelected => _selectedOnboardMenuRoute == "ShortHaul";
    public bool IsOnboardLongHaulMenuSelected => _selectedOnboardMenuRoute == "LongHaul";
    public string MealServiceStatus => $"{_passengers.ActiveMealServiceCount} passengers eating or receiving meals";
    public string DrinkServiceStatus => $"{_passengers.ActiveDrinkServiceCount} passengers in drinks service";
    public string LavatoryQueueStatus => $"{_passengers.LavatoryQueueCount} waiting for a lavatory";
    public int PassengerCount => _passengers.PassengerManifest.Count;
    public int CabinCrewCount => _passengers.CabinCrewMarkers.Count;
    public string ConnectionLabel => _passengers.Status.ConnectionLabel;
    public string CateringLoadLabel => IsCateringLoaded ? "Catering: Loaded" : "Catering: Awaiting load";
    public string SimBriefFooterLabel => HasSimBriefFlight ? "SimBrief Loaded" : "SimBrief Not Loaded";
    public string PassengerOrdersHeading => IsShortHaul && SelectedCabin == "Euro Traveller"
        ? "High Life Café Orders"
        : $"Passenger Orders ({SelectedCabin})";
    public string CurrentActivityTitle => DisplayServiceTitle(CurrentServiceTitle);
    public string CurrentActivitySubtitle => PassengerPreferences.Count == 0
        ? "Cabin service is staged"
        : ServiceProgress < 1d
            ? "Catering loaded and ready"
            : $"Serving {ActiveServiceCabin?.Name ?? SelectedCabin}";
    public string CurrentActivityDetail => PassengerPreferences.Count == 0
        ? "Waiting for the passenger manifest"
        : ServiceProgress < 1d
            ? "Waiting for cruise and cabin clearance"
            : $"Row {Math.Max(1, (int)Math.Ceiling((ActiveServiceCabin?.PassengerCount ?? PassengerPreferences.Count) * CurrentActivityProgress / 100d))} of {ActiveServiceCabin?.PassengerCount ?? PassengerPreferences.Count}";
    public double CurrentActivityProgress => ActiveServiceCabin?.ServiceProgress ?? ServiceProgress;
    public string CurrentActivityProgressLabel => $"{CurrentActivityProgress:0}%";
    public string NextServiceDisplayTitle => DisplayServiceTitle(NextServiceTitle);
    public string NextServiceEtaLabel => BuildNextServiceEtaLabel();
    private CateringCabinViewModel? ActiveServiceCabin => Cabins.FirstOrDefault(cabin => cabin.Name is "Club World" or "Club Europe") ?? Cabins.FirstOrDefault();
    public int TotalSpecialMeals => SpecialMeals.Count;
    public int VegetarianMeals => SpecialMeals.Count(item => item.SpecialMeal == "Vegetarian");
    public int VeganMeals => SpecialMeals.Count(item => item.SpecialMeal == "Vegan");
    public int GlutenFreeMeals => SpecialMeals.Count(item => item.SpecialMeal == "Gluten free");
    public int HalalMeals => SpecialMeals.Count(item => item.SpecialMeal == "Halal");
    public int OtherSpecialMeals => TotalSpecialMeals - VegetarianMeals - VeganMeals - GlutenFreeMeals - HalalMeals;

    public void Dispose()
    {
        _timer.Stop();
        _timer.Tick -= HandleTimerTick;
        _passengers.PropertyChanged -= HandlePassengersPropertyChanged;
        _passengers.PassengerManifest.CollectionChanged -= HandleManifestCollectionChanged;
        GC.SuppressFinalize(this);
    }

    private void HandleTimerTick(object? sender, EventArgs e)
    {
        RebuildFlightCatering(force: false);
        ProcessPurchases();
        UpdateServiceProgress();
        RefreshSummaryProperties();
    }

    private void HandlePassengersPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PassengerFlowViewModel.HasSimBriefFlight) or nameof(PassengerFlowViewModel.ImportedFlightNumber) or
            nameof(PassengerFlowViewModel.ImportedOrigin) or nameof(PassengerFlowViewModel.ImportedDestination) or
            nameof(PassengerFlowViewModel.ImportedAircraftIcao) or nameof(PassengerFlowViewModel.ImportedScheduledDepartureLocal) or
            nameof(PassengerFlowViewModel.ImportedScheduledArrivalLocal) or nameof(PassengerFlowViewModel.SelectedCabinLayoutProfile))
            RebuildFlightCatering(force: false);
        else if (e.PropertyName is nameof(PassengerFlowViewModel.LiveFlightPhase) or nameof(PassengerFlowViewModel.SeatbeltSignOn) or
                 nameof(PassengerFlowViewModel.AircraftMovementLabel))
        {
            UpdateServiceProgress();
            RefreshSummaryProperties();
        }
    }

    private void HandleManifestCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => RebuildFlightCatering(force: true);

    private CateringFlightContext CreateFlightContext()
    {
        var departure = _passengers.ImportedScheduledDepartureLocal ?? new DateTimeOffset(DateTime.Today.AddHours(14));
        var arrival = _passengers.ImportedScheduledArrivalLocal;
        var duration = arrival is { } arrivalTime && arrivalTime > departure ? arrivalTime - departure : _passengers.IsNarrowBodyCabinLayout ? TimeSpan.FromHours(2) : TimeSpan.FromHours(8.5);
        return new CateringFlightContext(_passengers.ImportedFlightNumber, ShortAirport(_passengers.ImportedOrigin), ShortAirport(_passengers.ImportedDestination),
            _passengers.ImportedAircraftIcao, departure, duration, _passengers.IsNarrowBodyCabinLayout);
    }

    private void RebuildFlightCatering(bool force)
    {
        var context = CreateFlightContext();
        var key = $"{context.FlightNumber}|{context.Origin}|{context.Destination}|{context.AircraftIcao}|{context.ScheduledDeparture:O}|{context.Duration}|{context.IsNarrowBody}|{_passengers.PassengerManifest.Count}";
        if (!force && string.Equals(key, _flightKey, StringComparison.Ordinal)) return;
        _flightKey = key;
        _pack = CateringMenuPackLoader.Load(_settingsDirectory, context.ScheduledDeparture);
        _selection = BritishAirwaysCateringProfileSelector.Select(context);
        _servedPassengerIds.Clear();
        _highLifeCafePurchaserIds.Clear();
        ServiceProgress = 0d;
        var definitions = _pack.Items.Where(MatchesSelection).ToArray();
        BuildCabins(definitions);
        BuildInventory(definitions);
        RebuildOnboardMenuCards();
        BuildPassengerPreferences(definitions);
        BuildServicePhases();
        var preferredCabin = _selection.AircraftFamily == CateringAircraftFamily.ShortHaul
            ? "Euro Traveller"
            : Cabins.FirstOrDefault()?.Name;
        SelectCabin(Cabins.Any(cabin => cabin.Name == SelectedCabin)
            ? SelectedCabin
            : Cabins.FirstOrDefault(cabin => cabin.Name == preferredCabin)?.Name ?? Cabins.FirstOrDefault()?.Name);
        InventoryStatus = _passengers.HasPassengerManifest ? $"CATERING LOADED · {LoadedUnits} units · {TotalSpecialMeals} special meals" : "AWAITING PASSENGER LOAD";
        ServiceStatus = _passengers.HasSimBriefFlight ? $"AUTO PROFILE · {_selection.ServiceBand}" : "Preview profile · import SimBrief for automatic selection";
        AddLog($"Loaded {_pack.DisplayName} for {ProfileLabel}");
        UpdateServiceProgress();
        RefreshSummaryProperties();
    }

    private bool MatchesSelection(CateringMenuDefinition item)
    {
        if (!string.Equals(item.Family, _selection.AircraftFamily.ToString(), StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.Equals(item.MealPeriod, "All", StringComparison.OrdinalIgnoreCase) && !string.Equals(item.MealPeriod, _selection.MealPeriod.ToString(), StringComparison.OrdinalIgnoreCase)) return false;
        return string.Equals(item.Region, "All", StringComparison.OrdinalIgnoreCase) || string.Equals(item.Region, _selection.RouteRegion.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private void BuildCabins(IReadOnlyCollection<CateringMenuDefinition> definitions)
    {
        Cabins.Clear();
        var names = _selection.AircraftFamily == CateringAircraftFamily.ShortHaul
            ? new[] { "Club Europe", "Euro Traveller" }
            : new[] { "First", "Club World", "World Traveller Plus", "World Traveller" };
        foreach (var name in names)
        {
            var count = PassengerCountForCabin(name);
            if (count > 0 || definitions.Any(item => item.Cabin == name))
                Cabins.Add(new CateringCabinViewModel(name, count, SeatCapacityForCabin(name)));
        }
    }

    private void BuildInventory(IEnumerable<CateringMenuDefinition> definitions)
    {
        Inventory.Clear();
        foreach (var item in definitions.DistinctBy(item => item.Id))
        {
            var cabinPassengers = item.Cabin == "All" ? _passengers.PassengerManifest.Count : Math.Max(1, PassengerCountForCabin(item.Cabin));
            var target = Math.Max(item.Complimentary ? 2 : 4, (int)Math.Ceiling(cabinPassengers * item.LoadFactorPercent / 100d));
            Inventory.Add(new CateringInventoryItemViewModel(item, target));
        }
    }

    private void BuildPassengerPreferences(IReadOnlyCollection<CateringMenuDefinition> definitions)
    {
        PassengerPreferences.Clear();
        SpecialMeals.Clear();
        foreach (var passenger in _passengers.PassengerManifest.OrderBy(item => CabinSort(item.CabinClassName)).ThenBy(item => item.SeatX))
        {
            var cabin = CateringCabinName(passenger.CabinClassName);
            var choices = _selection.AircraftFamily == CateringAircraftFamily.ShortHaul && cabin == "Euro Traveller"
                ? definitions.Where(item => item.Cabin == cabin &&
                    item.Course == "High Life Café" && !item.Complimentary).ToArray()
                : definitions.Where(item => item.Cabin == cabin && item.Course is "Mains" or "Main service").ToArray();
            if (choices.Length == 0) choices = definitions.Where(item => item.Cabin == cabin).ToArray();
            var choice = choices.Length == 0 ? null : choices[Math.Abs(passenger.PassengerId * 17) % choices.Length];
            var specialMeal = SpecialMealFor(passenger.PassengerId);
            var preference = new CateringPassengerPreferenceViewModel(passenger.PassengerId, passenger.SeatNumber, passenger.FullName, cabin,
                choice?.Id ?? string.Empty, choice?.Name ?? "Crew selection", specialMeal, cabin == "First" ? "Dine Anytime" : "Scheduled service");
            PassengerPreferences.Add(preference);
            if (specialMeal != "—") SpecialMeals.Add(preference);
        }
    }

    private void BuildServicePhases()
    {
        ServicePhases.Clear();
        var departure = _passengers.ImportedScheduledDepartureLocal;
        var arrival = _passengers.ImportedScheduledArrivalLocal;
        var phases = new (string Name, string Icon, double Threshold, string Time)[]
        {
            ("Boarding Complete", "▣", 0d, FormatTimelineTime(departure?.AddMinutes(-18))),
            ("Pushback", "◉", 8d, FormatTimelineTime(departure?.AddMinutes(-5))),
            ("Take Off", "✈", 16d, FormatTimelineTime(departure?.AddMinutes(12))),
            ("Climb", "↗", 24d, string.Empty),
            ("Cruise", "✈", 32d, string.Empty),
            ("Meal Service", "🍴", 43d, string.Empty),
            ("Beverage Service", "☕", 57d, string.Empty),
            ("Crew Rest", "☾", 68d, string.Empty),
            ("Second Service", "♨", 79d, string.Empty),
            ("Descent", "✈", 90d, string.Empty),
            ("Arrival", "⌄", 100d, FormatTimelineTime(arrival))
        };
        for (var index = 0; index < phases.Length; index++)
        {
            var phase = phases[index];
            ServicePhases.Add(new CateringServicePhaseViewModel(
                phase.Name,
                phase.Icon,
                phase.Threshold,
                phase.Time,
                index,
                phases.Length));
        }
        UpdateServicePhaseStates();
    }

    private void SelectCabin(string? cabin)
    {
        if (string.IsNullOrWhiteSpace(cabin)) return;
        SelectedCabin = cabin;
        OnPropertyChanged(nameof(PassengerOrdersHeading));
        foreach (var candidate in Cabins) candidate.IsSelected = candidate.Name == cabin;
        VisibleMenuItems.Clear();
        CourseOptions.Clear();
        var availableCourses = Inventory
            .Where(item => item.Cabin == cabin || item.Cabin == "All")
            .Select(item => item.Course)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var courseOrder = _selection.AircraftFamily == CateringAircraftFamily.ShortHaul
            ? new[] { "High Life Café", "Complimentary", "Main service", "Afternoon tea", "Drinks" }
            : new[] { "Amuse-bouche", "Starters", "Soup", "Mains", "Desserts", "Cheese & extras" };
        foreach (var course in courseOrder)
            CourseOptions.Add(new CateringCourseOptionViewModel(course, availableCourses.Contains(course)));
        SelectedCourse = CourseOptions.Any(option => option.Name == SelectedCourse && option.IsAvailable)
            ? SelectedCourse
            : CourseOptions.FirstOrDefault(option => option.IsAvailable)?.Name ?? CourseOptions[0].Name;
        foreach (var option in CourseOptions) option.IsSelected = option.Name == SelectedCourse;
        RefreshVisibleMenuItems();
        VisibleInventory.Clear();
        foreach (var item in Inventory.Where(item => item.Cabin == cabin || item.Cabin == "All")) VisibleInventory.Add(item);
        VisiblePassengerPreferences.Clear();
        foreach (var item in PassengerPreferences.Where(item => item.Cabin == cabin)) VisiblePassengerPreferences.Add(item);
        SelectedPassengerPreference = VisiblePassengerPreferences.FirstOrDefault();
    }

    private void SelectCourse(string? course)
    {
        if (string.IsNullOrWhiteSpace(course)) return;
        var option = CourseOptions.FirstOrDefault(candidate => candidate.Name == course);
        if (option is null || !option.IsAvailable) return;
        SelectedCourse = course;
        foreach (var candidate in CourseOptions) candidate.IsSelected = candidate.Name == course;
        RefreshVisibleMenuItems();
    }

    private void SelectOnboardMenuRoute(string? route)
    {
        var normalized = string.Equals(route, "LongHaul", StringComparison.OrdinalIgnoreCase)
            ? "LongHaul"
            : "ShortHaul";
        if (string.Equals(_selectedOnboardMenuRoute, normalized, StringComparison.Ordinal)) return;
        _selectedOnboardMenuRoute = normalized;
        OnPropertyChanged(nameof(IsOnboardShortHaulMenuSelected));
        OnPropertyChanged(nameof(IsOnboardLongHaulMenuSelected));
        RebuildOnboardMenuCards();
    }

    private void RebuildOnboardMenuCards()
    {
        OnboardMenuCards.Clear();
        var imageSlots = new[]
        {
            new System.Windows.Rect(0.1810d, 0.2471d, 0.1940d, 0.1826d),
            new System.Windows.Rect(0.3854d, 0.2471d, 0.1927d, 0.1826d),
            new System.Windows.Rect(0.5885d, 0.2471d, 0.1947d, 0.1826d),
            new System.Windows.Rect(0.7943d, 0.2471d, 0.1934d, 0.1826d),
            new System.Windows.Rect(0.1810d, 0.5508d, 0.1940d, 0.1826d),
            new System.Windows.Rect(0.3854d, 0.5508d, 0.1927d, 0.1826d),
            new System.Windows.Rect(0.5885d, 0.5508d, 0.1947d, 0.1826d),
            new System.Windows.Rect(0.7943d, 0.5508d, 0.1934d, 0.1826d)
        };

        var specifications = IsOnboardShortHaulMenuSelected
            ? new (string Id, string Name, string Description, string Price, bool Vegetarian)[]
            {
                ("cafe-sandwich", "Chicken and bacon sandwich", "A British classic with tender chicken, smoked bacon and a creamy mayo on a soft malted roll.", "£5.50", false),
                ("cafe-veg", "Mozzarella and tomato focaccia", "Mozzarella, ripe tomato and basil pesto on a soft focaccia.", "£5.25", true),
                ("cafe-snack", "Savoury snack box", "Crisps, nuts and a selection of savoury bites.", "£4.25", false),
                ("ce-tea", "Afternoon tea", "A selection of sandwiches, scone and a sweet treat.", "Included", false),
                ("ce-drinks", "Club Europe bar", "Champagne, wine, spirits, soft drinks and hot drinks.", "From £2.75", false),
                ("cafe-soft", "Soft drink", "Coca-Cola, Diet Coke, Sprite, lemonade or tonic.", "£2.75", false),
                ("cafe-wine", "Wine miniature", "Red, white or sparkling wine (187ml).", "£6.50", false),
                ("et-water", "Water and light snack", "Complimentary onboard refreshment.", "Included", false)
            }
            : new (string Id, string Name, string Description, string Price, bool Vegetarian)[]
            {
                ("first-beetroot", "Beetroot rose", "Whipped Tunworth cheese and beetroot.", "Included", true),
                ("first-salmon-blini", "Smoked salmon blini", "Crème fraîche, chive oil and lemon.", "Included", false),
                ("first-beef", "Fillet of British beef", "Dauphinoise potato, greens and red-wine jus.", "Included", false),
                ("first-chicken", "Roast chicken crown", "Fondant potato and seasonal vegetables.", "Included", false),
                ("first-dessert", "Seasonal dessert collection", "Dessert, fruit and chocolate selection.", "Included", false),
                ("club-beef", "Braised British beef", "Potato gratin, vegetables and peppercorn sauce.", "Included", false),
                ("wtp-chicken", "Roast chicken", "Potato gratin, seasonal greens and gravy.", "Included", false),
                ("wt-chicken", "Chicken in herb gravy", "Potatoes and vegetables.", "Included", false)
            };

        for (var index = 0; index < specifications.Length; index++)
        {
            var specification = specifications[index];
            var liveItem = Inventory.FirstOrDefault(item => item.Id == specification.Id);
            OnboardMenuCards.Add(new OnboardMenuCardViewModel(
                specification.Id,
                specification.Name,
                specification.Description,
                specification.Price,
                specification.Price == "Included" ? string.Empty : liveItem?.StockStatus ?? "Available",
                liveItem?.StockColor ?? "#39E986",
                specification.Vegetarian,
                imageSlots[index]));
        }
    }

    private void RefreshVisibleMenuItems()
    {
        VisibleMenuItems.Clear();
        foreach (var item in Inventory.Where(item => (item.Cabin == SelectedCabin || item.Cabin == "All") &&
            item.Course == SelectedCourse).Take(3)) VisibleMenuItems.Add(item);
    }

    private void UpdateServiceProgress()
    {
        if (!_passengers.HasPassengerManifest)
        {
            CurrentServiceTitle = "Waiting for passenger load";
            NextServiceTitle = "Import SimBrief or enter a manual load";
            ServiceStatus = "AWAITING PASSENGER LOAD";
            UpdateCabinProgress();
            UpdateServicePhaseStates();
            return;
        }
        var phase = _passengers.LiveFlightPhase;
        var isCruise = phase.Contains("Cruise", StringComparison.OrdinalIgnoreCase);
        var isDescent = phase.Contains("Descent", StringComparison.OrdinalIgnoreCase) || phase.Contains("Approach", StringComparison.OrdinalIgnoreCase);
        var isArrival = phase.Contains("Arrival", StringComparison.OrdinalIgnoreCase) || phase.Contains("Landed", StringComparison.OrdinalIgnoreCase);
        var safetyHold = _passengers.SeatbeltSignOn && (isCruise || isDescent);
        if (isArrival) ServiceProgress = 100d;
        else if (isDescent) ServiceProgress = Math.Max(ServiceProgress, 86d);
        else if (isCruise && AutoProgress && !_serviceManuallyHeld && !safetyHold) ServiceProgress += 0.45d;
        var serviceIndex = Math.Clamp((int)Math.Floor(ServiceProgress / (100d / Math.Max(1, _selection.ServiceSequence.Count))), 0, _selection.ServiceSequence.Count - 1);
        CurrentServiceTitle = isCruise || isDescent || isArrival ? _selection.ServiceSequence[serviceIndex] : "Catering loaded · waiting for cruise";
        NextServiceTitle = !isCruise && !isDescent && !isArrival
            ? _selection.ServiceSequence[0]
            : serviceIndex + 1 < _selection.ServiceSequence.Count
                ? _selection.ServiceSequence[serviceIndex + 1]
                : "Arrival and stock reconciliation";
        ServiceStatus = _serviceManuallyHeld ? "SERVICE HELD BY USER" : safetyHold ? "SERVICE PAUSED · SEAT-BELT SIGN ON" : isCruise ? "LIVE CABIN SERVICE" : $"STAGED · {phase.ToUpperInvariant()}";
        UpdatePassengerOrderStatus(safetyHold || _serviceManuallyHeld);
        UpdateCabinProgress();
        UpdateServicePhaseStates();
    }

    private void UpdatePassengerOrderStatus(bool held)
    {
        if (held) return;
        var servedCount = (int)Math.Floor(PassengerPreferences.Count * ServiceProgress / 100d);
        for (var index = 0; index < PassengerPreferences.Count; index++)
        {
            var passenger = PassengerPreferences[index];
            var newStatus = index < servedCount ? "Served" : index == servedCount && ServiceProgress > 0d ? "Being served" : "Pending";
            if (newStatus == "Served")
            {
                var inventoryItem = Inventory.FirstOrDefault(item => item.Id == passenger.MenuItemId);
                if (inventoryItem is not null && inventoryItem.Complimentary && _servedPassengerIds.Add(passenger.PassengerId))
                {
                    inventoryItem.Consume();
                }

                if (inventoryItem is not null &&
                    IsShortHaul &&
                    passenger.Cabin == "Euro Traveller" &&
                    string.Equals(CurrentServiceTitle, "High Life Café", StringComparison.OrdinalIgnoreCase) &&
                    !inventoryItem.Complimentary &&
                    inventoryItem.Service == "Buy on board" &&
                    inventoryItem.Course == "High Life Café" &&
                    inventoryItem.Quantity > 0 &&
                    _highLifeCafePurchaserIds.Add(passenger.PassengerId) &&
                    _passengers.TryRecordHighLifeCafePurchase(
                        passenger.PassengerId,
                        inventoryItem.Id,
                        inventoryItem.Name,
                        inventoryItem.PriceGbp))
                {
                    AddLog($"Seat {passenger.Seat} ordered {inventoryItem.Name} from High Life Café");
                }
            }
            passenger.Status = newStatus;
        }
        _passengers.ApplyCateringServiceProgress(ServiceProgress, ServiceProgress >= 46d && ServiceProgress < 68d, ServiceProgress > 0d && ServiceProgress < 86d);
        RefreshSummaryProperties();
    }

    private void UpdateServicePhaseStates()
    {
        TimelineProgress = CalculateTimelineProgress();
        var currentIndex = 0;
        for (var index = 0; index < ServicePhases.Count; index++)
        {
            if (TimelineProgress + 0.01d >= ServicePhases[index].Threshold) currentIndex = index;
        }

        for (var index = 0; index < ServicePhases.Count; index++)
            ServicePhases[index].Update(index < currentIndex ? "Complete" : index == currentIndex ? "Active" : "Waiting");
    }

    private void UpdateCabinProgress()
    {
        for (var index = 0; index < Cabins.Count; index++)
        {
            var offset = index switch { 0 => 12d, 1 => 5d, 2 => -13d, _ => -22d };
            Cabins[index].UpdateService(ServiceProgress < 1d ? 0d : Math.Clamp(ServiceProgress + offset, 0d, 100d),
                ServiceProgress < 1d ? "Waiting for service" : ServiceProgress < 68d ? "Main meal service" : "Beverage / second service");
        }
        OnPropertyChanged(nameof(CurrentActivitySubtitle));
        OnPropertyChanged(nameof(CurrentActivityDetail));
        OnPropertyChanged(nameof(CurrentActivityTitle));
        OnPropertyChanged(nameof(CurrentActivityProgress));
        OnPropertyChanged(nameof(CurrentActivityProgressLabel));
        OnPropertyChanged(nameof(NextServiceDisplayTitle));
        OnPropertyChanged(nameof(NextServiceEtaLabel));
    }

    private void ProcessPurchases()
    {
        var inventoryChanged = false;
        foreach (var purchase in _passengers.DrainRecentCateringPurchases())
        {
            var item = Inventory.FirstOrDefault(candidate => candidate.Id == purchase.ItemId &&
                !candidate.Complimentary &&
                candidate.Course == "High Life Café" &&
                candidate.Service == "Buy on board");
            if (item is not null)
            {
                item.Consume();
                inventoryChanged = true;
            }
            AddLog($"Seat {purchase.SeatNumber} purchased {purchase.ItemName} · £{purchase.PriceGbp:F2}");
        }
        if (inventoryChanged) RebuildOnboardMenuCards();
    }

    private void ResetInventory()
    {
        foreach (var item in Inventory) item.RestockToTarget();
        _servedPassengerIds.Clear();
        _highLifeCafePurchaserIds.Clear();
        foreach (var passenger in PassengerPreferences) passenger.Status = "Pending";
        InventoryStatus = "FULL CATERING UPLIFT COMPLETE";
        AddLog("Catering inventory reset and reconciled");
        RebuildOnboardMenuCards();
        RefreshSummaryProperties();
    }

    private void SkipService()
    {
        ServiceProgress = Math.Min(100d, ServiceProgress + 100d / Math.Max(1, _selection.ServiceSequence.Count));
        _serviceManuallyHeld = false;
        OnPropertyChanged(nameof(HoldButtonLabel));
        UpdateServiceProgress();
        AddLog($"Service advanced to {ServiceProgressLabel}");
    }

    private void ToggleServiceHold()
    {
        _serviceManuallyHeld = !_serviceManuallyHeld;
        OnPropertyChanged(nameof(HoldButtonLabel));
        UpdateServiceProgress();
        AddLog(_serviceManuallyHeld ? "Cabin service held" : "Cabin service resumed");
    }

    private void AdjustServiceTiming(int minutes)
    {
        _serviceTimingOffsetMinutes = Math.Clamp(_serviceTimingOffsetMinutes + minutes, -120, 120);
        OnPropertyChanged(nameof(ServiceTimingOffsetMinutes));
        OnPropertyChanged(nameof(ServiceTimingOffsetLabel));
        OnPropertyChanged(nameof(NextServiceEtaLabel));
        AddLog($"Service timing set to {ServiceTimingOffsetLabel.ToLowerInvariant()}");
    }

    private void ResetServiceTiming()
    {
        _serviceTimingOffsetMinutes = 0;
        OnPropertyChanged(nameof(ServiceTimingOffsetMinutes));
        OnPropertyChanged(nameof(ServiceTimingOffsetLabel));
        OnPropertyChanged(nameof(NextServiceEtaLabel));
        AddLog("Service timing returned to the flight schedule");
    }

    private double CalculateTimelineProgress()
    {
        var phase = _passengers.LiveFlightPhase ?? string.Empty;
        if (phase.Contains("Arrival", StringComparison.OrdinalIgnoreCase) || phase.Contains("Landed", StringComparison.OrdinalIgnoreCase)) return 100d;
        if (phase.Contains("Descent", StringComparison.OrdinalIgnoreCase) || phase.Contains("Approach", StringComparison.OrdinalIgnoreCase)) return Math.Max(90d, 90d + ServiceProgress * 0.08d);
        if (phase.Contains("Cruise", StringComparison.OrdinalIgnoreCase)) return 32d + ServiceProgress * 0.56d;
        if (phase.Contains("Climb", StringComparison.OrdinalIgnoreCase)) return 24d;
        if (phase.Contains("Take", StringComparison.OrdinalIgnoreCase) || phase.Contains("Airborne", StringComparison.OrdinalIgnoreCase)) return 16d;
        if (phase.Contains("Taxi", StringComparison.OrdinalIgnoreCase)) return 12d;
        if (phase.Contains("Push", StringComparison.OrdinalIgnoreCase) || _passengers.AircraftMovementLabel.Contains("PUSHBACK", StringComparison.OrdinalIgnoreCase)) return 8d;
        return 0d;
    }

    private string BuildNextServiceEtaLabel()
    {
        if (!_passengers.HasPassengerManifest) return "Waiting for passenger manifest";
        if (_serviceManuallyHeld) return "Resume service when the cabin is ready";
        var livePhase = _passengers.LiveFlightPhase ?? string.Empty;
        if (_passengers.SeatbeltSignOn &&
            (livePhase.Contains("Cruise", StringComparison.OrdinalIgnoreCase) ||
             livePhase.Contains("Descent", StringComparison.OrdinalIgnoreCase) ||
             livePhase.Contains("Approach", StringComparison.OrdinalIgnoreCase)))
            return "Paused while the seat-belt sign is on";
        if (!_passengers.HasSimBriefFlight) return "Ready when cabin conditions allow";

        var departure = _passengers.ImportedScheduledDepartureLocal ?? DateTimeOffset.Now;
        var duration = CreateFlightContext().Duration;
        var serviceIndex = Math.Clamp((int)Math.Floor(ServiceProgress / (100d / Math.Max(1, _selection.ServiceSequence.Count))), 0, _selection.ServiceSequence.Count - 1);
        var nextFraction = Math.Clamp((serviceIndex + 2d) / (_selection.ServiceSequence.Count + 2d), 0.12d, 0.88d);
        var estimatedStart = departure + TimeSpan.FromTicks((long)(duration.Ticks * nextFraction)) + TimeSpan.FromMinutes(_serviceTimingOffsetMinutes);
        var remaining = estimatedStart - DateTimeOffset.Now;
        if (remaining <= TimeSpan.Zero) return "Ready when cabin conditions allow";
        if (remaining.TotalHours >= 1d) return $"Estimated start in {(int)remaining.TotalHours}h {remaining.Minutes:00}m";
        return $"Estimated start in {Math.Max(1, remaining.Minutes)} min";
    }

    private void RefreshSummaryProperties()
    {
        foreach (var property in new[] { nameof(HasSimBriefFlight), nameof(IsCateringLoaded), nameof(IsShortHaul), nameof(PageDescription), nameof(MenuExperienceTitle), nameof(MenuExperienceSubtitle), nameof(MenuHeroTitle), nameof(MenuHeroDescription), nameof(MenuBrandLabel), nameof(ServiceNoteOne), nameof(ServiceNoteTwo), nameof(ServiceNoteThree), nameof(ServiceNoteFour), nameof(PassengerOrdersHeading), nameof(FlightNumber), nameof(RouteLabel), nameof(AircraftLabel), nameof(DepartureLabel), nameof(DurationLabel), nameof(ProfileLabel), nameof(ServiceBandLabel), nameof(PackLabel), nameof(CateringCycleLabel), nameof(CateringOverviewTitle), nameof(CateringOverviewDescription), nameof(CateringOverviewSymbol), nameof(CateringOverviewColor), nameof(TotalUnits), nameof(LoadedUnits), nameof(UsedUnits), nameof(InventoryUsagePercent), nameof(InventoryUsageLabel), nameof(LowStockCount), nameof(OutOfStockCount), nameof(TotalPassengerSpend), nameof(MealServiceStatus), nameof(DrinkServiceStatus), nameof(LavatoryQueueStatus), nameof(TotalSpecialMeals), nameof(VegetarianMeals), nameof(VeganMeals), nameof(GlutenFreeMeals), nameof(HalalMeals), nameof(OtherSpecialMeals), nameof(PassengerCount), nameof(CabinCrewCount), nameof(ConnectionLabel), nameof(CateringLoadLabel), nameof(SimBriefFooterLabel), nameof(CurrentActivityTitle), nameof(CurrentActivitySubtitle), nameof(CurrentActivityDetail), nameof(CurrentActivityProgress), nameof(CurrentActivityProgressLabel), nameof(NextServiceDisplayTitle), nameof(NextServiceEtaLabel) }) OnPropertyChanged(property);
    }

    private int PassengerCountForCabin(string cabin) => _passengers.PassengerManifest.Count(passenger => CateringCabinName(passenger.CabinClassName) == cabin);
    private int SeatCapacityForCabin(string cabin) => cabin switch
    {
        "First" => _passengers.CabinCapacityFor(PassengerCabinClass.First),
        "Club World" or "Club Europe" => _passengers.CabinCapacityFor(PassengerCabinClass.Business),
        "World Traveller Plus" => _passengers.CabinCapacityFor(PassengerCabinClass.PremiumEconomy),
        _ => _passengers.CabinCapacityFor(PassengerCabinClass.Economy)
    };
    private string CateringCabinName(string passengerCabin) => _selection.AircraftFamily == CateringAircraftFamily.ShortHaul ? passengerCabin == "Business" ? "Club Europe" : "Euro Traveller" : passengerCabin switch { "Business" => "Club World", "Premium Economy" => "World Traveller Plus", "Economy" => "World Traveller", _ => passengerCabin };
    private static int CabinSort(string cabin) => cabin switch { "First" => 0, "Business" => 1, "Premium Economy" => 2, _ => 3 };
    private static string SpecialMealFor(int passengerId) => passengerId % 11 != 0 ? "—" : (passengerId % 5) switch { 0 => "Vegetarian", 1 => "Vegan", 2 => "Gluten free", 3 => "Halal", _ => "Allergy meal" };
    private static string ShortAirport(string airport) { var value = airport.Trim().ToUpperInvariant(); return value switch { "EGLL" => "LHR", "EGKK" => "LGW", "KJFK" => "JFK", "KLAX" => "LAX", "KSFO" => "SFO", _ when value.Length == 4 => value[1..], _ => value }; }
    private static string FormatDuration(TimeSpan duration) => $"{(int)duration.TotalHours}h {duration.Minutes:00}m";
    private static string FormatTimelineTime(DateTimeOffset? time) => time?.ToString("HH:mm", CultureInfo.InvariantCulture) ?? string.Empty;
    private static string DisplayServiceTitle(string value) => value switch
    {
        "Pre-departure drinks" => "Pre-departure Drinks",
        "Main departure meal" => "Main Meal Service",
        "Mid-flight snacks and drinks" or "Mid-flight refreshments" => "Mid-flight Refreshments",
        "Pre-arrival light meal" or "Second service" => "Second Service",
        "Arrival preparation" => "Arrival Preparation",
        "Arrival and stock reconciliation" => "Arrival",
        "Catering loaded · waiting for cruise" => "Cabin Service Ready",
        "Waiting for passenger load" => "Service Plan Pending",
        _ => value
    };
    private string PackEffectiveMonth() => DateTime.TryParseExact(
        _pack.EffectiveFrom,
        "yyyy-MM-dd",
        CultureInfo.InvariantCulture,
        DateTimeStyles.None,
        out var effectiveFrom) ? effectiveFrom.ToString("MMM yyyy", CultureInfo.InvariantCulture) : "Current season";
    private static string FormatRouteRegion(CateringRouteRegion region, string destination) => region switch
    {
        CateringRouteRegion.NorthAmerica when destination is "LAX" or "SFO" or "SEA" or "SAN" or "LAS" => "US West Coast",
        CateringRouteRegion.NorthAmerica => "North America",
        CateringRouteRegion.LatinAmerica => "Latin America",
        CateringRouteRegion.AsiaPacific => "Asia Pacific",
        CateringRouteRegion.AfricaMiddleEast => "Africa & Middle East",
        CateringRouteRegion.DomesticEurope => "UK & Europe",
        _ => "Worldwide"
    };
    private static string FormatAircraftName(string icao, string fallback) => icao.Trim().ToUpperInvariant() switch
    {
        "B77W" => "Boeing 777-300ER",
        "B772" or "B77L" => "Boeing 777-200ER",
        "A319" => "Airbus A319",
        "A320" => "Airbus A320-200",
        "A20N" => "Airbus A320neo",
        "A321" => "Airbus A321",
        "A21N" => "Airbus A321neo",
        "A359" => "Airbus A350-1000",
        "B788" => "Boeing 787-8",
        "B789" => "Boeing 787-9",
        "B78X" => "Boeing 787-10",
        "E190" => "Embraer 190",
        _ => fallback.Replace("British Airways ", string.Empty, StringComparison.OrdinalIgnoreCase)
    };
    private void AddLog(string message) { ActivityLog.Insert(0, $"{DateTime.Now:HH:mm:ss}  {message}"); while (ActivityLog.Count > 10) ActivityLog.RemoveAt(ActivityLog.Count - 1); }
}

public sealed class CateringCabinViewModel : ObservableObject
{
    private bool _isSelected;
    private double _serviceProgress;
    private string _serviceLabel = "Waiting for service";
    public CateringCabinViewModel(string name, int passengerCount, int seatCapacity) { Name = name; PassengerCount = passengerCount; SeatCapacity = seatCapacity; }
    public string Name { get; }
    public int PassengerCount { get; }
    public int SeatCapacity { get; }
    public string PassengerLabel => $"{SeatCapacity} seats";
    public string ServicePassengerLabel => $"{PassengerCount} / {SeatCapacity} seats";
    public string AccentColor => Name switch
    {
        "First" => "#A34BB7",
        "Club World" or "Club Europe" => "#247BD1",
        "World Traveller Plus" => "#60B9F4",
        _ => "#6BBEF4"
    };
    public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }
    public double ServiceProgress { get => _serviceProgress; private set { if (SetProperty(ref _serviceProgress, value)) OnPropertyChanged(nameof(ServiceProgressLabel)); } }
    public string ServiceProgressLabel => $"{ServiceProgress:0}%";
    public string ServiceLabel { get => _serviceLabel; private set => SetProperty(ref _serviceLabel, value); }
    public System.Windows.Rect ImageViewbox => Name switch
    {
        "First" => new System.Windows.Rect(0d, 0d, 0.5d, 0.5d),
        "Club World" or "Club Europe" => new System.Windows.Rect(0.5d, 0d, 0.5d, 0.5d),
        "World Traveller Plus" => new System.Windows.Rect(0d, 0.5d, 0.5d, 0.5d),
        _ => new System.Windows.Rect(0.5d, 0.5d, 0.5d, 0.5d)
    };
    public void UpdateService(double progress, string label) { ServiceProgress = progress; ServiceLabel = label; }
}

public sealed class CateringCourseOptionViewModel : ObservableObject
{
    private bool _isSelected;
    public CateringCourseOptionViewModel(string name, bool isAvailable) { Name = name; IsAvailable = isAvailable; }
    public string Name { get; }
    public string DisplayName => Name switch
    {
        "Complimentary" => "Included",
        "Main service" => "Club Europe",
        "Afternoon tea" => "Tea",
        _ => Name
    };
    public bool IsAvailable { get; }
    public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }
}

public sealed class OnboardMenuCardViewModel
{
    public OnboardMenuCardViewModel(
        string id,
        string name,
        string description,
        string priceLabel,
        string availabilityLabel,
        string availabilityColor,
        bool isVegetarian,
        System.Windows.Rect imageViewbox)
    {
        Id = id;
        Name = name;
        Description = description;
        PriceLabel = priceLabel;
        AvailabilityLabel = availabilityLabel;
        AvailabilityColor = availabilityColor;
        IsVegetarian = isVegetarian;
        ImageViewbox = imageViewbox;
    }

    public string Id { get; }
    public string Name { get; }
    public string Description { get; }
    public string PriceLabel { get; }
    public string AvailabilityLabel { get; }
    public string AvailabilityColor { get; }
    public bool IsVegetarian { get; }
    public System.Windows.Rect ImageViewbox { get; }
}

public sealed class CateringInventoryItemViewModel : ObservableObject
{
    private int _quantity;
    public CateringInventoryItemViewModel(CateringMenuDefinition item, int quantity) { Id = item.Id; Name = item.Name; Cabin = item.Cabin; Course = item.Course; Service = item.Service; Description = item.Description; Dietary = item.Dietary; PriceGbp = item.PriceGbp; Complimentary = item.Complimentary; TargetQuantity = quantity; _quantity = quantity; }
    public string Id { get; }
    public string Name { get; }
    public string Cabin { get; }
    public string Course { get; }
    public string Service { get; }
    public string Description { get; }
    public string Dietary { get; }
    public decimal PriceGbp { get; }
    public bool Complimentary { get; }
    public string PriceLabel => Complimentary ? "Included" : $"£{PriceGbp:F2}";
    public int TargetQuantity { get; }
    public int Quantity { get => _quantity; private set { if (SetProperty(ref _quantity, Math.Clamp(value, 0, TargetQuantity))) NotifyStockChanged(); } }
    public int Used => TargetQuantity - Quantity;
    public bool IsLowStock => Quantity <= Math.Max(2, TargetQuantity / 4);
    public string StockLabel => $"{Quantity} / {TargetQuantity}";
    public string StockStatus => Quantity == 0 ? "Out of stock" : IsLowStock ? "Low" : "Available";
    public string StockColor => Quantity == 0 ? "#FF6373" : IsLowStock ? "#FFB55F" : "#58E68A";
    public double UsagePercent => TargetQuantity == 0 ? 0d : Used * 100d / TargetQuantity;
    public System.Windows.Rect ThumbnailViewbox => Id switch
    {
        "first-beetroot" => new System.Windows.Rect(0d, 0d, 1d / 3d, 1d),
        "first-salmon-blini" => new System.Windows.Rect(1d / 3d, 0d, 1d / 3d, 1d),
        "first-seasonal-amuse" => new System.Windows.Rect(2d / 3d, 0d, 1d / 3d, 1d),
        _ => ((uint)Id.GetHashCode(StringComparison.Ordinal) % 3u) switch
        {
            0 => new System.Windows.Rect(0d, 0d, 1d / 3d, 1d),
            1 => new System.Windows.Rect(1d / 3d, 0d, 1d / 3d, 1d),
            _ => new System.Windows.Rect(2d / 3d, 0d, 1d / 3d, 1d)
        }
    };
    public void Consume(int amount = 1) => Quantity -= Math.Max(1, amount);
    public void RestockToTarget() => Quantity = TargetQuantity;
    private void NotifyStockChanged() { OnPropertyChanged(nameof(Used)); OnPropertyChanged(nameof(IsLowStock)); OnPropertyChanged(nameof(StockLabel)); OnPropertyChanged(nameof(StockStatus)); OnPropertyChanged(nameof(StockColor)); OnPropertyChanged(nameof(UsagePercent)); }
}

public sealed class CateringPassengerPreferenceViewModel : ObservableObject
{
    private string _status = "Pending";
    public CateringPassengerPreferenceViewModel(int passengerId, string seat, string name, string cabin, string menuItemId, string mealSelection, string specialMeal, string serviceStyle) { PassengerId = passengerId; Seat = seat; Name = name; Cabin = cabin; MenuItemId = menuItemId; MealSelection = mealSelection; SpecialMeal = specialMeal; ServiceStyle = serviceStyle; }
    public int PassengerId { get; }
    public string Seat { get; }
    public string Name { get; }
    public string Cabin { get; }
    public string MenuItemId { get; }
    public string MealSelection { get; }
    public string SpecialMeal { get; }
    public string ServiceStyle { get; }
    public string Status { get => _status; set { if (SetProperty(ref _status, value)) { OnPropertyChanged(nameof(StatusColor)); OnPropertyChanged(nameof(StatusSymbol)); } } }
    public string StatusColor => Status switch { "Served" => "#58E68A", "Being served" => "#FFB55F", _ => "#8DA0B8" };
    public string StatusSymbol => Status switch { "Served" => "●", "Being served" => "●", _ => "○" };
}

public sealed class CateringServicePhaseViewModel : ObservableObject
{
    private string _status = "Waiting";
    public CateringServicePhaseViewModel(string name, string icon, double threshold, string timeLabel, int index, int count)
    {
        Name = name;
        Icon = icon;
        Threshold = threshold;
        TimeLabel = timeLabel;
        IsFirst = index == 0;
        IsLast = index == count - 1;
    }
    public string Name { get; }
    public string Icon { get; }
    public double Threshold { get; }
    public string TimeLabel { get; }
    public bool IsFirst { get; }
    public bool IsLast { get; }
    public string Status
    {
        get => _status;
        private set
        {
            if (!SetProperty(ref _status, value)) return;
            OnPropertyChanged(nameof(Color));
            OnPropertyChanged(nameof(NodeFill));
            OnPropertyChanged(nameof(NodeStroke));
            OnPropertyChanged(nameof(ConnectorColor));
            OnPropertyChanged(nameof(IconSize));
            OnPropertyChanged(nameof(StatusDetail));
        }
    }
    public string Color => Status switch { "Complete" => "#58E68A", "Active" => "#55AEFF", _ => "#61758E" };
    public string NodeFill => Status switch { "Complete" => "#58E68A", "Active" => "#3A9DF4", _ => "#06233F" };
    public string NodeStroke => Status switch { "Complete" => "#58E68A", "Active" => "#60E49B", _ => "#6993B8" };
    public string ConnectorColor => Status == "Complete" ? "#45DB91" : "#7CA4C6";
    public double IconSize => Status == "Active" ? 25d : 21d;
    public string StatusDetail => string.IsNullOrWhiteSpace(TimeLabel) ? Status : TimeLabel;
    public void Update(string status) => Status = status;
}
