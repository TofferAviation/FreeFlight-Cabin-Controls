using System.Text.Json.Serialization;

namespace FreeFlight.CabinControl.Core.Content;

public sealed class AirlinePackManifest
{
    public const int CurrentSchemaVersion = 1;

    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; init; }

    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; init; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; init; } = string.Empty;

    [JsonPropertyName("author")]
    public string Author { get; init; } = string.Empty;

    [JsonPropertyName("licence")]
    public string Licence { get; init; } = string.Empty;

    [JsonPropertyName("aircraftAdapters")]
    public IReadOnlyList<string> AircraftAdapters { get; init; } = [];

    [JsonPropertyName("branding")]
    public AirlineBranding Branding { get; init; } = new();

    [JsonPropertyName("assets")]
    public IReadOnlyList<AirlinePackAsset> Assets { get; init; } = [];
}

public sealed class AirlineBranding
{
    [JsonPropertyName("primaryColor")]
    public string PrimaryColor { get; init; } = "#1476FF";

    [JsonPropertyName("accentColor")]
    public string AccentColor { get; init; } = "#00BDF2";

    [JsonPropertyName("logo")]
    public string? Logo { get; init; }
}

public sealed class AirlinePackAsset
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;

    [JsonPropertyName("path")]
    public string Path { get; init; } = string.Empty;

    [JsonPropertyName("licence")]
    public string Licence { get; init; } = string.Empty;
}
