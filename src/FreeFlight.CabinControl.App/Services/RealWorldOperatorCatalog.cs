using System.Reflection;
using System.Text.Json;

namespace FreeFlight.CabinControl.App.Services;

public sealed record RealWorldOperatorEntry(string Id, string Name, string Icao, string Source);

public static class RealWorldOperatorCatalog
{
    private const string ResourceName = "FreeFlight.CabinControl.Operators.RealWorld.json";

    private static readonly Lazy<IReadOnlyList<RealWorldOperatorEntry>> Entries = new(Load);

    public static IReadOnlyList<RealWorldOperatorEntry> All => Entries.Value;

    private static IReadOnlyList<RealWorldOperatorEntry> Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException("The bundled real-world operator catalog is missing.");
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.GetProperty("operators")
            .EnumerateArray()
            .Select((item, index) =>
            {
                var name = item.GetProperty("name").GetString()?.Trim() ?? string.Empty;
                var icao = item.GetProperty("icao").GetString()?.Trim().ToUpperInvariant() ?? string.Empty;
                var source = item.GetProperty("source").GetString()?.Trim() ?? string.Empty;
                var key = icao.Length > 0 ? icao.ToLowerInvariant() : $"row-{index + 1:000}";
                return new RealWorldOperatorEntry($"real.{key}", name, icao, source);
            })
            .Where(entry => entry.Name.Length > 0)
            .ToArray();
    }
}
