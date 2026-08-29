namespace FreeFlight.CabinControl.Core.Operations;

public sealed record PassengerNoShowForecast(
    int RatePercent,
    int ForecastPassengerCount,
    string ProfileLabel);

public static class PassengerNoShowForecastService
{
    private static readonly HashSet<string> JapanAirports =
    [
        "HND", "NRT", "KIX", "NGO", "FUK", "CTS", "OKA"
    ];

    private static readonly HashSet<string> IndiaAirports =
    [
        "DEL", "BOM", "BLR", "MAA", "HYD", "CCU", "COK", "GOI", "ATQ", "AMD", "PNQ", "TRV"
    ];

    public static PassengerNoShowForecast Calculate(
        string? flightNumber,
        string? originIata,
        string? destinationIata,
        int bookedPassengers)
    {
        var flight = Normalize(flightNumber);
        var origin = Normalize(originIata);
        var destination = Normalize(destinationIata);
        var isBritishAirways = flight.StartsWith("BAW", StringComparison.Ordinal) ||
                               flight.StartsWith("BA", StringComparison.Ordinal);

        int ratePercent;
        string profileLabel;
        if (isBritishAirways && IndiaAirports.Contains(origin))
        {
            ratePercent = 40;
            profileLabel = "Historic BA India-origin extreme";
        }
        else if (isBritishAirways &&
                 (JapanAirports.Contains(origin) || JapanAirports.Contains(destination)))
        {
            ratePercent = 2;
            profileLabel = "Historic BA Japan profile";
        }
        else if (isBritishAirways)
        {
            ratePercent = 9;
            profileLabel = "Historic BA systemwide average";
        }
        else
        {
            ratePercent = CalculateIndustryBaseline(flight, origin, destination);
            profileLabel = "Industry route baseline";
        }

        var forecastCount = Math.Clamp(
            (int)Math.Round(bookedPassengers * (ratePercent / 100d), MidpointRounding.AwayFromZero),
            0,
            Math.Max(0, bookedPassengers));
        return new PassengerNoShowForecast(ratePercent, forecastCount, profileLabel);
    }

    private static int CalculateIndustryBaseline(string flight, string origin, string destination)
    {
        var routeKey = $"{flight}|{origin}|{destination}";
        var stableSeed = routeKey.Aggregate(17, (current, character) => unchecked((current * 31) + character));
        return 2 + (int)(Math.Abs((long)stableSeed) % 9L);
    }

    private static string Normalize(string? value) => (value ?? string.Empty).Trim().ToUpperInvariant();
}
