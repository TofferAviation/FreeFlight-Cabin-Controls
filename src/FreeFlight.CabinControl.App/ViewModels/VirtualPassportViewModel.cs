using System.Collections.ObjectModel;
using System.Windows.Media;
using FreeFlight.CabinControl.App.Infrastructure;
using FreeFlight.CabinControl.App.Services;
using FreeFlight.CabinControl.Core.Configuration;
using FreeFlight.CabinControl.Core.Persistence;

namespace FreeFlight.CabinControl.App.ViewModels;

public sealed class VirtualPassportViewModel : PageViewModel
{
    private static readonly string[] StampTones = ["#2D7185", "#6B4C88", "#8A5B2D", "#397354", "#9B3D55"];
    private readonly AppSettings _settings;
    private readonly ISettingsStore _settingsStore;
    private string _statusMessage = "Arrival stamps appear after a confirmed ACARS PIREP.";

    public VirtualPassportViewModel(AppSettings settings, ISettingsStore settingsStore)
        : base("Virtual Passport", "A local record of confirmed simulation arrivals")
    {
        _settings = settings;
        _settingsStore = settingsStore;
        Entries = new ObservableCollection<VirtualPassportEntry>(
            settings.VirtualPassportStamps
                .OrderByDescending(stamp => stamp.ArrivedAtLocal)
                .Select(VirtualPassportEntry.FromSettings));
    }

    public ObservableCollection<VirtualPassportEntry> Entries { get; }

    public bool HasEntries => Entries.Count > 0;

    public int CountryCount => Entries
        .Select(entry => entry.Country)
        .Where(country => country != "Destination country")
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Count();

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public async Task RecordArrivalAsync(FleetPirepDto pirep, DateTimeOffset completedAtUtc)
    {
        var stamp = CreateStamp(pirep, completedAtUtc);
        if (stamp is null)
        {
            return;
        }

        try
        {
            await _settingsStore.SaveAsync(_settings);
            StatusMessage = $"{stamp.AirportCode} arrival added to your virtual passport.";
        }
        catch (Exception exception)
        {
            // A PIREP is already confirmed remotely; a local keepsake failure must never change it.
            _settings.VirtualPassportStamps.Remove(stamp);
            Entries.RemoveAt(0);
            OnPropertyChanged(nameof(HasEntries));
            OnPropertyChanged(nameof(CountryCount));
            StatusMessage = $"Your PIREP is complete, but Ember could not save its local passport stamp: {exception.Message}";
        }
    }

    public async Task SyncHistoryAsync(IEnumerable<FleetPirepDto> pireps)
    {
        var added = new List<VirtualPassportStampSettings>();
        foreach (var pirep in pireps)
        {
            var completedAt = DateTimeOffset.TryParse(pirep.CompletedAt, out var parsed) ? parsed : DateTimeOffset.UtcNow;
            var stamp = CreateStamp(pirep, completedAt);
            if (stamp is not null) added.Add(stamp);
        }

        if (added.Count == 0) return;
        try
        {
            await _settingsStore.SaveAsync(_settings);
            StatusMessage = $"Added {added.Count} existing BAV flight stamp{(added.Count == 1 ? string.Empty : "s")} to your virtual passport.";
        }
        catch (Exception exception)
        {
            foreach (var stamp in added)
            {
                _settings.VirtualPassportStamps.Remove(stamp);
            }
            Entries.Clear();
            foreach (var remaining in _settings.VirtualPassportStamps.OrderByDescending(stamp => stamp.ArrivedAtLocal))
            {
                Entries.Add(VirtualPassportEntry.FromSettings(remaining));
            }
            OnPropertyChanged(nameof(HasEntries));
            OnPropertyChanged(nameof(CountryCount));
            StatusMessage = $"Ember could not save your existing passport stamps: {exception.Message}";
        }
    }

    private VirtualPassportStampSettings? CreateStamp(FleetPirepDto pirep, DateTimeOffset completedAtUtc)
    {
        if (string.IsNullOrWhiteSpace(pirep.Id) ||
            _settings.VirtualPassportStamps.Any(stamp => string.Equals(stamp.SourcePirepId, pirep.Id, StringComparison.Ordinal)))
        {
            return null;
        }

        var airport = VirtualPassportAirportCatalog.Resolve(pirep.To);
        var arrivedAt = ToAirportLocalTime(completedAtUtc, airport.WindowsTimeZoneId);
        var impressionSeed = GetStableSeed(pirep.Id);
        var hasWeatheredInk = impressionSeed % 5 is 0 or 1;
        var stamp = new VirtualPassportStampSettings
        {
            SourcePirepId = pirep.Id,
            FlightNumber = pirep.FlightNumber?.Trim().ToUpperInvariant() ?? "Flight",
            AirportCode = airport.Iata,
            AirportName = airport.AirportName,
            City = airport.City,
            Country = airport.Country,
            ArrivedAtLocal = arrivedAt,
            StampTone = StampTones[Math.Abs(StringComparer.OrdinalIgnoreCase.GetHashCode(airport.Country)) % StampTones.Length],
            HasWeatheredInk = hasWeatheredInk,
            InkOpacity = hasWeatheredInk ? 0.68d + (impressionSeed % 17) / 100d : 0.96d,
            RotationDegrees = hasWeatheredInk ? -2.5d + (impressionSeed % 51) / 10d : -0.8d + (impressionSeed % 17) / 20d
        };
        _settings.VirtualPassportStamps.Add(stamp);
        Entries.Insert(0, VirtualPassportEntry.FromSettings(stamp));
        OnPropertyChanged(nameof(HasEntries));
        OnPropertyChanged(nameof(CountryCount));
        return stamp;
    }

    private static DateTimeOffset ToAirportLocalTime(DateTimeOffset value, string timeZoneId)
    {
        try
        {
            return TimeZoneInfo.ConvertTime(value, TimeZoneInfo.FindSystemTimeZoneById(timeZoneId));
        }
        catch (TimeZoneNotFoundException)
        {
            return value.ToLocalTime();
        }
        catch (InvalidTimeZoneException)
        {
            return value.ToLocalTime();
        }
    }

    private static int GetStableSeed(string value)
    {
        var hash = 17;
        foreach (var character in value)
        {
            hash = unchecked(hash * 31 + char.ToUpperInvariant(character));
        }

        return hash & int.MaxValue;
    }
}

public sealed class VirtualPassportEntry
{
    private VirtualPassportEntry(VirtualPassportStampSettings stamp)
    {
        FlightNumber = stamp.FlightNumber;
        AirportCode = stamp.AirportCode;
        AirportName = stamp.AirportName;
        City = stamp.City;
        Country = stamp.Country;
        StampTone = stamp.StampTone;
        ArrivedAtLocal = stamp.ArrivedAtLocal;
        HasWeatheredInk = stamp.HasWeatheredInk;
        InkOpacity = stamp.InkOpacity is > 0d and <= 1d ? stamp.InkOpacity : 1d;
        RotationDegrees = stamp.RotationDegrees;
    }

    public string FlightNumber { get; }
    public string AirportCode { get; }
    public string AirportName { get; }
    public string City { get; }
    public string Country { get; }
    public string StampTone { get; }
    public bool HasWeatheredInk { get; }
    public double InkOpacity { get; }
    public double RotationDegrees { get; }
    public double WearOpacity => HasWeatheredInk ? 0.42d : 0d;
    public string CardBackground => HasWeatheredInk ? "#EEE5D4" : "#F4EFE4";
    public DateTimeOffset ArrivedAtLocal { get; }
    public string ArrivalDate => ArrivedAtLocal.ToString("dd MMM yyyy").ToUpperInvariant();
    public string ArrivalTime => ArrivedAtLocal.ToString("HH:mm");
    public string LocationLabel => $"{City}, {Country}";

    public static VirtualPassportEntry FromSettings(VirtualPassportStampSettings stamp) => new(stamp);
}
