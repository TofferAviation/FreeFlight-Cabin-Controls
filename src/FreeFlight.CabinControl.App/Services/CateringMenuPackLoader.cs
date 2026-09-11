using System.Globalization;
using System.IO;
using System.Text.Json;

namespace FreeFlight.CabinControl.App.Services;

public sealed class CateringMenuPack
{
    public string Id { get; set; } = "british-airways-catering";
    public string Version { get; set; } = "2026.09";
    public string DisplayName { get; set; } = "British Airways seasonal menu pack";
    public string EffectiveFrom { get; set; } = "2026-01-01";
    public string EffectiveTo { get; set; } = "2027-12-31";
    public List<CateringMenuDefinition> Items { get; set; } = [];
}

public sealed class CateringMenuDefinition
{
    public string Id { get; set; } = string.Empty;
    public string Family { get; set; } = "LongHaul";
    public string Region { get; set; } = "All";
    public string MealPeriod { get; set; } = "All";
    public string Cabin { get; set; } = "World Traveller";
    public string Course { get; set; } = "Mains";
    public string Service { get; set; } = "Departure meal";
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Dietary { get; set; } = string.Empty;
    public decimal PriceGbp { get; set; }
    public bool Complimentary { get; set; } = true;
    public int LoadFactorPercent { get; set; } = 35;
}

public static class CateringMenuPackLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static CateringMenuPack Load(string? settingsDirectory, DateTimeOffset now)
    {
        var candidates = new List<CateringMenuPack>();
        LoadDirectory(Path.Combine(AppContext.BaseDirectory, "content-packs", "british-airways", "catering"), candidates);
        if (!string.IsNullOrWhiteSpace(settingsDirectory))
        {
            LoadDirectory(Path.Combine(settingsDirectory, "catering-packs"), candidates);
        }

        var active = candidates
            .Where(candidate => IsActive(candidate, now.Date))
            .OrderByDescending(candidate => ParseDate(candidate.EffectiveFrom))
            .ThenByDescending(candidate => candidate.Version, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        return active ?? candidates
            .OrderByDescending(candidate => ParseDate(candidate.EffectiveFrom))
            .ThenByDescending(candidate => candidate.Version, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault() ?? CreateFallback();
    }

    private static void LoadDirectory(string directory, ICollection<CateringMenuPack> destination)
    {
        if (!Directory.Exists(directory)) return;
        foreach (var path in Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly))
        {
            try
            {
                var pack = JsonSerializer.Deserialize<CateringMenuPack>(File.ReadAllText(path), JsonOptions);
                if (pack is { Items.Count: > 0 }) destination.Add(pack);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
            {
                // A damaged optional pack must never prevent the cabin application from starting.
            }
        }
    }

    private static bool IsActive(CateringMenuPack pack, DateTime date)
    {
        var from = ParseDate(pack.EffectiveFrom) ?? DateTime.MinValue;
        var to = ParseDate(pack.EffectiveTo) ?? DateTime.MaxValue;
        return date >= from && date <= to;
    }

    private static DateTime? ParseDate(string value) => DateTime.TryParseExact(
        value,
        "yyyy-MM-dd",
        CultureInfo.InvariantCulture,
        DateTimeStyles.AssumeLocal,
        out var parsed) ? parsed.Date : null;

    private static CateringMenuPack CreateFallback() => new()
    {
        DisplayName = "British Airways fallback catering pack",
        Items =
        [
            new CateringMenuDefinition { Id = "fallback-chicken", Family = "LongHaul", Cabin = "World Traveller", Name = "Roast chicken", Course = "Mains", Description = "Chicken with seasonal vegetables", LoadFactorPercent = 55 },
            new CateringMenuDefinition { Id = "fallback-pasta", Family = "LongHaul", Cabin = "World Traveller", Name = "Tomato basil pasta", Course = "Mains", Description = "Vegetarian pasta", Dietary = "Vegetarian", LoadFactorPercent = 45 },
            new CateringMenuDefinition { Id = "fallback-club", Family = "ShortHaul", Cabin = "Club Europe", Name = "Seasonal Club Europe meal", Course = "Main service", Description = "Meal selected for the departure time", LoadFactorPercent = 100 },
            new CateringMenuDefinition { Id = "fallback-cafe", Family = "ShortHaul", Cabin = "Euro Traveller", Name = "High Life Café snack", Course = "Café", Description = "Buy-on-board selection", PriceGbp = 4.50m, Complimentary = false, LoadFactorPercent = 35 }
        ]
    };
}
