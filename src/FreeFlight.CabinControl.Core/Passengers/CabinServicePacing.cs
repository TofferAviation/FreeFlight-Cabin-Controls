namespace FreeFlight.CabinControl.Core.Passengers;

/// <summary>
/// Describes the deliberately slower, aircraft-aware pace for the visible
/// cabin-service simulation.  This is a presentation/operations model rather
/// than a claim about an airline's published service standard.
/// </summary>
public sealed record CabinServicePacing(
    string Label,
    TimeSpan MainServiceDuration,
    TimeSpan MidFlightServiceDuration,
    TimeSpan PreArrivalServiceDuration,
    TimeSpan PreArrivalLead,
    int ConcurrentServicePositions)
{
    public string Summary => $"{Label} · main service about {Math.Ceiling(MainServiceDuration.TotalMinutes):0} min";

    public static CabinServicePacing Select(CateringFlightContext context, int passengerCount, int cabinCrewCount)
    {
        var aircraft = context.AircraftIcao?.Trim().ToUpperInvariant() ?? string.Empty;
        var isA380 = aircraft.Contains("A380", StringComparison.Ordinal) || aircraft.Contains("A388", StringComparison.Ordinal);
        var isShortHaul = context.IsNarrowBody || context.AircraftIcao is "A319" or "A19N" or "A320" or "A20N" or "A321" or "A21N" or "E190";
        var crew = Math.Max(1, cabinCrewCount);
        var passengersPerCrew = Math.Max(1d, passengerCount / (double)crew);

        if (isA380)
        {
            // Two decks, several galleys and a large economy cabin need a
            // noticeably longer service window even with a full crew.
            var mainMinutes = Math.Clamp(passengersPerCrew * 1.7d, 30d, 48d);
            return new CabinServicePacing(
                "A380 multi-deck service",
                TimeSpan.FromMinutes(mainMinutes),
                TimeSpan.FromMinutes(Math.Clamp(mainMinutes * 0.62d, 18d, 30d)),
                TimeSpan.FromMinutes(Math.Clamp(mainMinutes * 0.78d, 24d, 38d)),
                TimeSpan.FromMinutes(100),
                Math.Clamp((int)Math.Ceiling(crew * 0.65d), 8, 16));
        }

        if (isShortHaul)
        {
            var mainMinutes = Math.Clamp(passengersPerCrew * 0.85d, 9d, 18d);
            return new CabinServicePacing(
                "Short-haul cabin service",
                TimeSpan.FromMinutes(mainMinutes),
                TimeSpan.FromMinutes(Math.Clamp(mainMinutes * 0.55d, 6d, 11d)),
                TimeSpan.FromMinutes(Math.Clamp(mainMinutes * 0.7d, 8d, 14d)),
                TimeSpan.FromMinutes(35),
                Math.Clamp((int)Math.Ceiling(crew * 0.75d), 2, 6));
        }

        var widebodyMainMinutes = Math.Clamp(passengersPerCrew * 1.5d, 24d, 42d);
        return new CabinServicePacing(
            "Long-haul cabin service",
            TimeSpan.FromMinutes(widebodyMainMinutes),
            TimeSpan.FromMinutes(Math.Clamp(widebodyMainMinutes * 0.58d, 14d, 25d)),
            TimeSpan.FromMinutes(Math.Clamp(widebodyMainMinutes * 0.72d, 19d, 32d)),
            TimeSpan.FromMinutes(80),
            Math.Clamp((int)Math.Ceiling(crew * 0.7d), 4, 12));
    }
}
