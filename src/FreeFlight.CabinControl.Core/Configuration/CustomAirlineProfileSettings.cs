namespace FreeFlight.CabinControl.Core.Configuration;

public sealed class CustomAirlineProfileSettings
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Icao { get; set; } = string.Empty;

    public string SoundPackName { get; set; } = "Custom cabin pack";
}
