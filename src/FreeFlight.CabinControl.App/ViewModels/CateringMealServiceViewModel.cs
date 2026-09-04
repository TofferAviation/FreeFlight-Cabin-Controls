using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Threading;
using FreeFlight.CabinControl.App.Infrastructure;
using FreeFlight.CabinControl.Core.Catering;

namespace FreeFlight.CabinControl.App.ViewModels;

public sealed class CateringMealServiceViewModel : PageViewModel, IDisposable
{
    private readonly PassengerFlowViewModel _passengers;
    private readonly CateringInventorySimulator _inventory;
    private readonly PassengerSpendLedger _spendLedger = new();
    private readonly DispatcherTimer _timer;
    private readonly Random _random = new(5050);
    private int _purchasePulse;
    private string _inventoryStatus = "Catering loading";

    public CateringMealServiceViewModel(PassengerFlowViewModel passengers)
        : base("Catering & Meal Service", "Live catering inventory, onboard service and passenger purchases")
    {
        _passengers = passengers;
        var items = CreateInitialInventory();
        _inventory = new CateringInventorySimulator(items, 5050);
        _inventory.InventoryChanged += HandleInventoryChanged;
        MenuItems = items.Select(MenuItemViewModel.FromInventory).ToArray();
        StartLoadingCommand = new RelayCommand(_ => StartLoading());
        RefillCommand = new RelayCommand(_ => BeginRefill());
        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _timer.Tick += HandleTimerTick;
        _inventory.BeginLoading();
        RefreshInventoryRows();
        RefreshPassengerSpendRows();
        _timer.Start();
    }

    public ObservableCollection<CateringInventoryRowViewModel> InventoryRows { get; } = [];

    public ObservableCollection<PassengerSpendRowViewModel> PassengerSpendRows { get; } = [];

    public IReadOnlyList<MenuItemViewModel> MenuItems { get; }

    public ICommand StartLoadingCommand { get; }

    public ICommand RefillCommand { get; }

    public string InventoryStatus
    {
        get => _inventoryStatus;
        private set => SetProperty(ref _inventoryStatus, value);
    }

    public string CateringProgress
    {
        get
        {
            var target = _inventory.Items.Sum(item => item.TargetQuantity);
            var loaded = _inventory.Items.Sum(item => item.LoadedQuantity);
            return target <= 0 ? "100%" : $"{loaded * 100d / target:F0}%";
        }
    }

    public string FlightRevenue => $"£{_spendLedger.FlightRevenue:F2}";

    public string PurchaseSummary => _spendLedger.Purchases.Count == 0
        ? "No paid onboard purchases yet"
        : $"{_spendLedger.Purchases.Count} paid items · {_spendLedger.Purchases.Select(item => item.PassengerId).Distinct().Count()} passengers";

    public void Dispose()
    {
        _timer.Stop();
        _timer.Tick -= HandleTimerTick;
        _inventory.InventoryChanged -= HandleInventoryChanged;
        GC.SuppressFinalize(this);
    }

    private void HandleTimerTick(object? sender, EventArgs e)
    {
        _inventory.Tick(_timer.Interval);
        _purchasePulse++;
        if (_purchasePulse >= 18)
        {
            _purchasePulse = 0;
            TryCreatePassengerPurchase();
        }

        RefreshInventoryRows();
        RefreshPassengerSpendRows();
        RefreshStatus();
    }

    private void HandleInventoryChanged(CateringInventoryChange change)
    {
        OnPropertyChanged(nameof(CateringProgress));
    }

    private void StartLoading()
    {
        _spendLedger.Clear();
        _inventory.BeginLoading();
        InventoryStatus = "Catering loading started";
        RefreshInventoryRows();
        RefreshPassengerSpendRows();
    }

    private void BeginRefill()
    {
        _inventory.BeginRefill();
        InventoryStatus = "Catering replenishment in progress";
        RefreshInventoryRows();
    }

    private void RefreshStatus()
    {
        var loading = _inventory.Items.Count(item => item.State is CateringLoadState.Loading or CateringLoadState.Replenishing);
        var depleted = _inventory.Items.Count(item => item.State == CateringLoadState.Depleted);
        InventoryStatus = loading > 0
            ? $"Live loading · {loading} item groups still being loaded"
            : depleted > 0
                ? $"Service active · {depleted} item groups depleted"
                : "Catering loaded · inventory updating with service";
        OnPropertyChanged(nameof(CateringProgress));
        OnPropertyChanged(nameof(FlightRevenue));
        OnPropertyChanged(nameof(PurchaseSummary));
    }

    private void TryCreatePassengerPurchase()
    {
        if (_passengers.PassengerManifest.Count == 0 || _passengers.BoardedPassengerCount == 0)
        {
            return;
        }

        var chargeable = _inventory.Items
            .Where(item => !item.Complimentary && item.LoadedQuantity > 0)
            .ToArray();
        if (chargeable.Length == 0)
        {
            return;
        }

        var eligiblePassengers = _passengers.PassengerManifest
            .Where(passenger => !passenger.StatusLabel.Contains("Awaiting", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (eligiblePassengers.Length == 0)
        {
            return;
        }

        var passenger = eligiblePassengers[_random.Next(eligiblePassengers.Length)];
        var item = chargeable[_random.Next(chargeable.Length)];
        if (!_inventory.TryConsume(item.Sku, 1))
        {
            return;
        }

        _spendLedger.RecordPurchase(passenger.PassengerId, passenger.SeatNumber, item);
    }

    private void RefreshInventoryRows()
    {
        var existing = InventoryRows.ToDictionary(item => item.Sku, StringComparer.OrdinalIgnoreCase);
        foreach (var item in _inventory.Items.OrderBy(item => item.Category).ThenBy(item => item.Name))
        {
            if (existing.TryGetValue(item.Sku, out var row))
            {
                row.Update(item);
            }
            else
            {
                InventoryRows.Add(new CateringInventoryRowViewModel(item));
            }
        }
    }

    private void RefreshPassengerSpendRows()
    {
        var totals = _passengers.PassengerManifest
            .Select(passenger => new PassengerSpendRowViewModel(
                passenger.PassengerId,
                passenger.SeatNumber,
                passenger.FullName,
                _spendLedger.GetPassengerTotal(passenger.PassengerId),
                _spendLedger.GetPassengerPurchases(passenger.PassengerId).Count))
            .OrderByDescending(item => item.TotalSpend)
            .ThenBy(item => item.SeatNumber, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        PassengerSpendRows.Clear();
        foreach (var row in totals)
        {
            PassengerSpendRows.Add(row);
        }

        OnPropertyChanged(nameof(FlightRevenue));
        OnPropertyChanged(nameof(PurchaseSummary));
    }

    private static CateringInventoryItem[] CreateInitialInventory() =>
    [
        new("HL-CURRY", "Chicken Thai Green Curry", CateringItemCategory.Meal, 36, 8.00m),
        new("HL-HAM", "Ham Hock & Cheddar Toastie", CateringItemCategory.Meal, 42, 6.50m),
        new("HL-CHICKEN", "Chicken Salad Sandwich", CateringItemCategory.Meal, 44, 5.50m),
        new("HL-MOZZ", "Mozzarella, Tomato & Basil Roll", CateringItemCategory.Meal, 36, 5.50m),
        new("HL-RAMEN", "Itsu Chicken Ramen", CateringItemCategory.Meal, 28, 5.00m),
        new("HL-MUFFIN", "Berries & Cream Muffin", CateringItemCategory.Snack, 48, 3.00m),
        new("HL-LATTE", "Grind Caramel Latte", CateringItemCategory.HotDrink, 64, 3.75m),
        new("HL-COFFEE", "Grind Long Black Coffee", CateringItemCategory.HotDrink, 64, 3.50m),
        new("HL-TEA", "Birchall English Breakfast Tea", CateringItemCategory.HotDrink, 64, 3.20m),
        new("HL-MEAL", "Sandwich + snack + hot/cold drink deal", CateringItemCategory.Meal, 30, 9.95m),
        new("HL-MEAL-BAR", "Sandwich + snack + beer/wine/cider deal", CateringItemCategory.Meal, 24, 12.95m),
        new("INC-WATER", "Complimentary water", CateringItemCategory.SoftDrink, 220, 0m, true),
        new("INC-SNACK", "Complimentary snack", CateringItemCategory.Snack, 220, 0m, true),
        new("INC-MAIN", "Included main meal", CateringItemCategory.Meal, 180, 0m, true)
    ];
}

public sealed class CateringInventoryRowViewModel : ObservableObject
{
    private int _loaded;
    private int _target;
    private string _state = string.Empty;

    public CateringInventoryRowViewModel(CateringInventoryItem item)
    {
        Sku = item.Sku;
        Name = item.Name;
        Category = item.Category.ToString();
        Price = item.Complimentary ? "Included" : $"£{item.UnitPrice:F2}";
        Update(item);
    }

    public string Sku { get; }
    public string Name { get; }
    public string Category { get; }
    public string Price { get; }
    public int Loaded { get => _loaded; private set => SetProperty(ref _loaded, value); }
    public int Target { get => _target; private set => SetProperty(ref _target, value); }
    public string State { get => _state; private set => SetProperty(ref _state, value); }
    public string QuantityLabel => $"{Loaded} / {Target}";
    public double Progress => Target <= 0 ? 100d : Loaded * 100d / Target;

    public void Update(CateringInventoryItem item)
    {
        Loaded = item.LoadedQuantity;
        Target = item.TargetQuantity;
        State = item.State.ToString();
        OnPropertyChanged(nameof(QuantityLabel));
        OnPropertyChanged(nameof(Progress));
    }
}

public sealed record PassengerSpendRowViewModel(
    int PassengerId,
    string SeatNumber,
    string PassengerName,
    decimal TotalSpend,
    int PurchaseCount)
{
    public string TotalSpendLabel => $"£{TotalSpend:F2}";
    public string PurchaseCountLabel => PurchaseCount == 1 ? "1 item" : $"{PurchaseCount} items";
}

public sealed record MenuItemViewModel(
    string Sku,
    string Name,
    string Category,
    string PriceLabel,
    bool IsComplimentary)
{
    public static MenuItemViewModel FromInventory(CateringInventoryItem item) => new(
        item.Sku,
        item.Name,
        item.Category.ToString(),
        item.Complimentary ? "Included" : $"£{item.UnitPrice:F2}",
        item.Complimentary);
}

public sealed class MenuBoardViewModel : PageViewModel
{
    private int _pageIndex;

    public MenuBoardViewModel(CateringMealServiceViewModel catering)
        : base("Menu Board", "Onboard menu presentation for the active cabin service")
    {
        Pages =
        [
            new MenuBoardPageViewModel(
                "High Life Café",
                "BUY ON BOARD · EURO TRAVELLER",
                catering.MenuItems.Where(item => !item.IsComplimentary && item.Category is "Meal" or "Snack").ToArray()),
            new MenuBoardPageViewModel(
                "Drinks",
                "HIGH LIFE CAFÉ",
                catering.MenuItems.Where(item => !item.IsComplimentary && item.Category is "HotDrink" or "SoftDrink" or "AlcoholicDrink").ToArray()),
            new MenuBoardPageViewModel(
                "Included Service",
                "COMPLIMENTARY ONBOARD SERVICE",
                catering.MenuItems.Where(item => item.IsComplimentary).ToArray())
        ];
        PreviousPageCommand = new RelayCommand(_ => PreviousPage(), _ => PageIndex > 0);
        NextPageCommand = new RelayCommand(_ => NextPage(), _ => PageIndex < Pages.Count - 1);
    }

    public IReadOnlyList<MenuBoardPageViewModel> Pages { get; }

    public ICommand PreviousPageCommand { get; }

    public ICommand NextPageCommand { get; }

    public int PageIndex
    {
        get => _pageIndex;
        private set
        {
            if (!SetProperty(ref _pageIndex, value))
            {
                return;
            }

            OnPropertyChanged(nameof(CurrentPage));
            OnPropertyChanged(nameof(PageNumberLabel));
            (PreviousPageCommand as RelayCommand)?.NotifyCanExecuteChanged();
            (NextPageCommand as RelayCommand)?.NotifyCanExecuteChanged();
        }
    }

    public MenuBoardPageViewModel CurrentPage => Pages[PageIndex];

    public string PageNumberLabel => $"{PageIndex + 1} / {Pages.Count}";

    private void PreviousPage() => PageIndex = Math.Max(0, PageIndex - 1);

    private void NextPage() => PageIndex = Math.Min(Pages.Count - 1, PageIndex + 1);
}

public sealed record MenuBoardPageViewModel(
    string Title,
    string Eyebrow,
    IReadOnlyList<MenuItemViewModel> Items);
