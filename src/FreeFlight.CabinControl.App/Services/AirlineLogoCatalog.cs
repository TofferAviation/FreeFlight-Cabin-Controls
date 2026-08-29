using System.IO;
using System.Reflection;
using System.Text.Json;

namespace FreeFlight.CabinControl.App.Services;

public static class AirlineLogoCatalog
{
    private const string BundledCodesResource = "FreeFlight.CabinControl.Operators.BundledLogoCodes.json";

    private static readonly Lazy<HashSet<string>> BundledIcaoCodes = new(LoadBundledCodes);

    public static string? Resolve(string icao)
    {
        var normalizedIcao = icao.Trim().ToUpperInvariant();
        if (normalizedIcao.Length is < 2 or > 4)
        {
            return null;
        }

        var userLogoDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FreeFlight",
            "CabinControl",
            "airline-logos");
        foreach (var extension in new[] { ".png", ".jpg", ".jpeg" })
        {
            var localPath = Path.Combine(userLogoDirectory, $"{normalizedIcao}{extension}");
            if (File.Exists(localPath))
            {
                return new Uri(localPath).AbsoluteUri;
            }
        }

        return BundledIcaoCodes.Value.Contains(normalizedIcao)
            ? $"pack://application:,,,/FreeFlight.CabinControl;component/Assets/AirlineLogos/{normalizedIcao}.png"
            : null;
    }

    private static HashSet<string> LoadBundledCodes()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(BundledCodesResource)
            ?? throw new InvalidOperationException("The bundled airline-logo index is missing.");
        return JsonSerializer.Deserialize<string[]>(stream)?.ToHashSet(StringComparer.OrdinalIgnoreCase)
            ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }
}
