using System.IO;

namespace FreeFlight.CabinControl.App.Services;

public static class AirlineLogoCatalog
{
    private static readonly HashSet<string> BundledIcaoCodes =
        new(StringComparer.OrdinalIgnoreCase) { "BAW", "NOZ" };

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

        return BundledIcaoCodes.Contains(normalizedIcao)
            ? $"pack://application:,,,/FreeFlight.CabinControl;component/Assets/AirlineLogos/{normalizedIcao}.png"
            : null;
    }
}
