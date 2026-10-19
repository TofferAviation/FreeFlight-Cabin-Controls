namespace FreeFlight.CabinControl.Core.Configuration;

/// <summary>
/// A local, simulation-only record of a completed virtual arrival. It is
/// deliberately not a representation of a government document or border record.
/// </summary>
public sealed class VirtualPassportStampSettings
{
    public string SourcePirepId { get; set; } = string.Empty;

    public string FlightNumber { get; set; } = string.Empty;

    public string AirportCode { get; set; } = string.Empty;

    public string AirportName { get; set; } = string.Empty;

    public string City { get; set; } = string.Empty;

    public string Country { get; set; } = string.Empty;

    public DateTimeOffset ArrivedAtLocal { get; set; }

    public string StampTone { get; set; } = "#2F6B7C";

    // A few virtual impressions look naturally handled rather than freshly
    // printed. The values are stored so a stamp never changes after issue.
    public bool HasWeatheredInk { get; set; }

    public double InkOpacity { get; set; } = 1d;

    public double RotationDegrees { get; set; }
}
