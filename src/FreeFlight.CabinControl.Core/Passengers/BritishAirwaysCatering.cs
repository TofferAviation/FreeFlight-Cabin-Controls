namespace FreeFlight.CabinControl.Core.Passengers;

public enum CateringAircraftFamily
{
    ShortHaul,
    LongHaul
}

public enum CateringMealPeriod
{
    Breakfast,
    Lunch,
    AfternoonTea,
    Dinner
}

public enum CateringRouteRegion
{
    DomesticEurope,
    NorthAmerica,
    LatinAmerica,
    AfricaMiddleEast,
    AsiaPacific,
    GeneralLongHaul
}

public sealed record CateringFlightContext(
    string FlightNumber,
    string Origin,
    string Destination,
    string AircraftIcao,
    DateTimeOffset ScheduledDeparture,
    TimeSpan Duration,
    bool IsNarrowBody);

public sealed record CateringProfileSelection(
    CateringAircraftFamily AircraftFamily,
    CateringMealPeriod MealPeriod,
    CateringRouteRegion RouteRegion,
    string ServiceBand,
    string ProfileId,
    string DisplayName,
    IReadOnlyList<string> ServiceSequence);

public static class BritishAirwaysCateringProfileSelector
{
    private static readonly HashSet<string> NorthAmerica =
    [
        "ATL", "AUS", "BOS", "BWI", "DEN", "DFW", "EWR", "IAD", "IAH", "JFK", "LAS", "LAX",
        "MCO", "MIA", "MSY", "ORD", "PHL", "PHX", "SAN", "SEA", "SFO", "TPA", "YUL", "YVR", "YYC", "YYZ"
    ];

    private static readonly HashSet<string> LatinAmerica =
    [
        "BGI", "BNA", "CUN", "EZE", "GCM", "GRU", "KIN", "LIM", "MEX", "NAS", "POS", "PUJ", "SCL", "UVF"
    ];

    private static readonly HashSet<string> AfricaMiddleEast =
    [
        "ABV", "ACC", "AUH", "BAH", "CAI", "CPT", "DOH", "DXB", "JED", "JNB", "LOS", "MRU", "NBO", "RUH", "SEZ"
    ];

    private static readonly HashSet<string> AsiaPacific =
    [
        "BKK", "BLR", "BOM", "DEL", "HKG", "HND", "HYD", "KUL", "MAA", "MLE", "NRT", "PER", "SIN", "SYD"
    ];

    public static CateringProfileSelection Select(CateringFlightContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var family = context.IsNarrowBody || IsShortHaulAircraft(context.AircraftIcao)
            ? CateringAircraftFamily.ShortHaul
            : CateringAircraftFamily.LongHaul;
        var mealPeriod = SelectMealPeriod(context.ScheduledDeparture.TimeOfDay);
        var region = family == CateringAircraftFamily.ShortHaul
            ? CateringRouteRegion.DomesticEurope
            : SelectRegion(context.Destination);
        var minutes = Math.Max(1d, context.Duration.TotalMinutes);

        if (family == CateringAircraftFamily.ShortHaul)
        {
            var band = minutes switch
            {
                <= 90d => "Band 1 · express",
                <= 150d => "Band 2 · short-haul",
                <= 240d => "Band 3 · extended short-haul",
                _ => "Band 4 · longest short-haul"
            };
            return new CateringProfileSelection(
                family,
                mealPeriod,
                region,
                band,
                $"ba-shorthaul-{mealPeriod.ToString().ToLowerInvariant()}",
                $"Club Europe {FormatMealPeriod(mealPeriod)} · Euro Traveller Café",
                minutes < 105d
                    ? ["Club Europe service", "Euro Traveller complimentary snack", "High Life Café"]
                    : ["Club Europe meal", "Drinks service", "Euro Traveller complimentary service", "High Life Café"]);
        }

        var longHaulBand = minutes switch
        {
            <= 420d => "Long-haul · shorter sector",
            <= 600d => "Long-haul · standard sector",
            _ => "Long-haul · extended sector"
        };
        var sequence = minutes > 600d
            ? new[] { "Pre-departure drinks", "Main departure meal", "Mid-flight snacks and drinks", "Second service", "Arrival preparation" }
            : new[] { "Pre-departure drinks", "Main departure meal", "Mid-flight refreshments", "Pre-arrival light meal", "Arrival preparation" };
        return new CateringProfileSelection(
            family,
            mealPeriod,
            region,
            longHaulBand,
            $"ba-longhaul-{region.ToString().ToLowerInvariant()}-{mealPeriod.ToString().ToLowerInvariant()}",
            $"British Airways long-haul · {FormatRegion(region)}",
            sequence);
    }

    private static CateringMealPeriod SelectMealPeriod(TimeSpan departureTime) => departureTime.Hours switch
    {
        < 10 => CateringMealPeriod.Breakfast,
        < 14 => CateringMealPeriod.Lunch,
        < 17 => CateringMealPeriod.AfternoonTea,
        _ => CateringMealPeriod.Dinner
    };

    private static CateringRouteRegion SelectRegion(string destination)
    {
        var code = NormalizeAirport(destination);
        if (NorthAmerica.Contains(code)) return CateringRouteRegion.NorthAmerica;
        if (LatinAmerica.Contains(code)) return CateringRouteRegion.LatinAmerica;
        if (AfricaMiddleEast.Contains(code)) return CateringRouteRegion.AfricaMiddleEast;
        if (AsiaPacific.Contains(code)) return CateringRouteRegion.AsiaPacific;
        return CateringRouteRegion.GeneralLongHaul;
    }

    private static bool IsShortHaulAircraft(string aircraft) => aircraft.Trim().ToUpperInvariant() switch
    {
        "A319" or "A19N" or "A320" or "A20N" or "A321" or "A21N" or "E190" => true,
        _ => false
    };

    private static string NormalizeAirport(string airport)
    {
        var value = airport.Trim().ToUpperInvariant();
        return value switch
        {
            "EGLL" => "LHR", "EGKK" => "LGW", "KJFK" => "JFK", "KLAX" => "LAX", "KSFO" => "SFO",
            "KBOS" => "BOS", "KORD" => "ORD", "KMIA" => "MIA", "OMDB" => "DXB", "VHHH" => "HKG",
            "WSSS" => "SIN", "RJTT" => "HND", "FACT" => "CPT", "FAOR" => "JNB",
            _ when value.Length == 4 => value[1..],
            _ => value
        };
    }

    public static string FormatMealPeriod(CateringMealPeriod period) => period switch
    {
        CateringMealPeriod.AfternoonTea => "afternoon tea",
        _ => period.ToString().ToLowerInvariant()
    };

    public static string FormatRegion(CateringRouteRegion region) => region switch
    {
        CateringRouteRegion.DomesticEurope => "UK & Europe",
        CateringRouteRegion.NorthAmerica => "North America",
        CateringRouteRegion.LatinAmerica => "Latin America & Caribbean",
        CateringRouteRegion.AfricaMiddleEast => "Africa & Middle East",
        CateringRouteRegion.AsiaPacific => "Asia Pacific",
        _ => "Worldwide"
    };
}
