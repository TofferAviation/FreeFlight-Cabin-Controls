namespace FreeFlight.CabinControl.Core.Catering;

public enum CateringItemCategory
{
    Meal,
    Snack,
    SoftDrink,
    HotDrink,
    AlcoholicDrink,
    Retail,
    Other
}

public enum CateringLoadState
{
    NotLoaded,
    Loading,
    Loaded,
    Replenishing,
    Depleted
}

public sealed class CateringInventoryItem
{
    public CateringInventoryItem(
        string sku,
        string name,
        CateringItemCategory category,
        int targetQuantity,
        decimal unitPrice,
        bool complimentary = false)
    {
        if (string.IsNullOrWhiteSpace(sku)) throw new ArgumentException("SKU is required.", nameof(sku));
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Name is required.", nameof(name));
        if (targetQuantity < 0) throw new ArgumentOutOfRangeException(nameof(targetQuantity));
        if (unitPrice < 0m) throw new ArgumentOutOfRangeException(nameof(unitPrice));

        Sku = sku.Trim();
        Name = name.Trim();
        Category = category;
        TargetQuantity = targetQuantity;
        UnitPrice = unitPrice;
        Complimentary = complimentary;
    }

    public string Sku { get; }

    public string Name { get; }

    public CateringItemCategory Category { get; }

    public int TargetQuantity { get; private set; }

    public int LoadedQuantity { get; internal set; }

    public decimal UnitPrice { get; }

    public bool Complimentary { get; }

    public CateringLoadState State { get; internal set; } = CateringLoadState.NotLoaded;

    public int RemainingQuantity => LoadedQuantity;

    public double LoadProgress => TargetQuantity == 0
        ? 1d
        : Math.Clamp(LoadedQuantity / (double)TargetQuantity, 0d, 1d);

    internal void SetTargetQuantity(int targetQuantity)
    {
        TargetQuantity = Math.Max(0, targetQuantity);
        LoadedQuantity = Math.Min(LoadedQuantity, TargetQuantity);
    }
}

public sealed record CateringInventoryChange(
    DateTimeOffset Timestamp,
    string Sku,
    int PreviousQuantity,
    int NewQuantity,
    CateringLoadState State,
    string Reason);

public sealed class CateringInventorySimulator
{
    private readonly Dictionary<string, CateringInventoryItem> _items;
    private readonly Random _random;
    private double _secondsUntilNextLoadStep;

    public CateringInventorySimulator(IEnumerable<CateringInventoryItem> items, int randomSeed = 777500)
    {
        ArgumentNullException.ThrowIfNull(items);
        var materialized = items.ToArray();
        if (materialized.Select(item => item.Sku).Distinct(StringComparer.OrdinalIgnoreCase).Count() != materialized.Length)
        {
            throw new ArgumentException("Catering SKUs must be unique.", nameof(items));
        }

        _items = materialized.ToDictionary(item => item.Sku, StringComparer.OrdinalIgnoreCase);
        _random = new Random(randomSeed);
        _secondsUntilNextLoadStep = NextDelay();
    }

    public IReadOnlyCollection<CateringInventoryItem> Items => _items.Values;

    public event Action<CateringInventoryChange>? InventoryChanged;

    public void BeginLoading()
    {
        foreach (var item in _items.Values)
        {
            item.LoadedQuantity = 0;
            item.State = item.TargetQuantity == 0 ? CateringLoadState.Loaded : CateringLoadState.Loading;
        }

        _secondsUntilNextLoadStep = 0d;
    }

    public void BeginRefill(IReadOnlyDictionary<string, int>? targetOverrides = null)
    {
        foreach (var item in _items.Values)
        {
            if (targetOverrides is not null && targetOverrides.TryGetValue(item.Sku, out var target))
            {
                item.SetTargetQuantity(target);
            }

            item.State = item.LoadedQuantity >= item.TargetQuantity
                ? CateringLoadState.Loaded
                : CateringLoadState.Replenishing;
        }

        _secondsUntilNextLoadStep = 0d;
    }

    public void Tick(TimeSpan elapsed, DateTimeOffset? timestamp = null)
    {
        var seconds = Math.Clamp(elapsed.TotalSeconds, 0d, 30d);
        if (seconds <= 0d)
        {
            return;
        }

        _secondsUntilNextLoadStep -= seconds;
        while (_secondsUntilNextLoadStep <= 0d)
        {
            if (!ApplyRandomLoadStep(timestamp ?? DateTimeOffset.UtcNow))
            {
                _secondsUntilNextLoadStep = NextDelay();
                return;
            }

            _secondsUntilNextLoadStep += NextDelay();
        }
    }

    public bool TryConsume(string sku, int quantity, DateTimeOffset? timestamp = null)
    {
        if (quantity <= 0 || !_items.TryGetValue(sku, out var item) || item.LoadedQuantity < quantity)
        {
            return false;
        }

        var previous = item.LoadedQuantity;
        item.LoadedQuantity -= quantity;
        item.State = item.LoadedQuantity == 0 ? CateringLoadState.Depleted : CateringLoadState.Loaded;
        InventoryChanged?.Invoke(new CateringInventoryChange(
            timestamp ?? DateTimeOffset.UtcNow,
            item.Sku,
            previous,
            item.LoadedQuantity,
            item.State,
            "Passenger service"));
        return true;
    }

    private bool ApplyRandomLoadStep(DateTimeOffset timestamp)
    {
        var candidates = _items.Values
            .Where(item => item.State is CateringLoadState.Loading or CateringLoadState.Replenishing)
            .Where(item => item.LoadedQuantity < item.TargetQuantity)
            .ToArray();
        if (candidates.Length == 0)
        {
            foreach (var item in _items.Values.Where(item => item.LoadedQuantity >= item.TargetQuantity))
            {
                item.State = CateringLoadState.Loaded;
            }

            return false;
        }

        var item = candidates[_random.Next(candidates.Length)];
        var remaining = item.TargetQuantity - item.LoadedQuantity;
        var maximumStep = Math.Max(1, Math.Min(remaining, Math.Max(2, item.TargetQuantity / 12)));
        var step = _random.Next(1, maximumStep + 1);
        var previous = item.LoadedQuantity;
        item.LoadedQuantity = Math.Min(item.TargetQuantity, item.LoadedQuantity + step);
        if (item.LoadedQuantity >= item.TargetQuantity)
        {
            item.State = CateringLoadState.Loaded;
        }

        InventoryChanged?.Invoke(new CateringInventoryChange(
            timestamp,
            item.Sku,
            previous,
            item.LoadedQuantity,
            item.State,
            item.State == CateringLoadState.Loaded ? "Loading complete" : "Catering vehicle loading"));
        return true;
    }

    private double NextDelay() => 0.35d + (_random.NextDouble() * 1.4d);
}

public sealed record PassengerPurchase(
    DateTimeOffset Timestamp,
    int PassengerId,
    string SeatNumber,
    string Sku,
    string Description,
    int Quantity,
    decimal UnitPrice,
    decimal Total);

public sealed class PassengerSpendLedger
{
    private readonly List<PassengerPurchase> _purchases = [];

    public IReadOnlyList<PassengerPurchase> Purchases => _purchases;

    public PassengerPurchase RecordPurchase(
        int passengerId,
        string seatNumber,
        CateringInventoryItem item,
        int quantity = 1,
        DateTimeOffset? timestamp = null)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (passengerId <= 0) throw new ArgumentOutOfRangeException(nameof(passengerId));
        if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity));

        var unitPrice = item.Complimentary ? 0m : item.UnitPrice;
        var purchase = new PassengerPurchase(
            timestamp ?? DateTimeOffset.UtcNow,
            passengerId,
            seatNumber ?? string.Empty,
            item.Sku,
            item.Name,
            quantity,
            unitPrice,
            unitPrice * quantity);
        _purchases.Add(purchase);
        return purchase;
    }

    public decimal GetPassengerTotal(int passengerId) => _purchases
        .Where(item => item.PassengerId == passengerId)
        .Sum(item => item.Total);

    public IReadOnlyList<PassengerPurchase> GetPassengerPurchases(int passengerId) => _purchases
        .Where(item => item.PassengerId == passengerId)
        .OrderBy(item => item.Timestamp)
        .ToArray();

    public decimal FlightRevenue => _purchases.Sum(item => item.Total);

    public void Clear() => _purchases.Clear();
}
