using System.Text.RegularExpressions;

namespace FreeFlight.CabinControl.Core.Content;

public sealed partial class AirlinePackValidator
{
    private static readonly HashSet<string> AllowedAssetExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".aac", ".flac", ".jpg", ".jpeg", ".json", ".m4a", ".mkv", ".mov",
        ".mp3", ".mp4", ".ogg", ".png", ".svg", ".wav", ".webm"
    };

    public PackValidationResult Validate(AirlinePackManifest manifest, string packDirectory)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(packDirectory);

        var errors = new List<string>();

        if (manifest.SchemaVersion != AirlinePackManifest.CurrentSchemaVersion)
        {
            errors.Add($"Unsupported schema version {manifest.SchemaVersion}.");
        }

        if (!PackIdPattern().IsMatch(manifest.Id))
        {
            errors.Add("Pack id must be 3-64 lowercase letters, numbers, dots, or hyphens.");
        }

        Require(manifest.Version, "Pack version", errors);
        Require(manifest.DisplayName, "Display name", errors);
        Require(manifest.Author, "Author", errors);
        Require(manifest.Licence, "Pack licence declaration", errors);

        string packRoot;
        try
        {
            packRoot = Path.GetFullPath(packDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            errors.Add("Pack directory is invalid.");
            return new PackValidationResult(errors);
        }

        foreach (var asset in manifest.Assets)
        {
            ValidateAsset(asset, packRoot, errors);
        }

        return new PackValidationResult(errors);
    }

    private static void ValidateAsset(AirlinePackAsset asset, string packRoot, ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(asset.Path))
        {
            errors.Add("Every asset requires a relative path.");
            return;
        }

        if (Path.IsPathRooted(asset.Path))
        {
            errors.Add($"Asset path must be relative: {asset.Path}");
            return;
        }

        var resolvedPath = Path.GetFullPath(Path.Combine(packRoot, asset.Path));
        if (!resolvedPath.StartsWith(packRoot, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"Asset path escapes the pack directory: {asset.Path}");
        }

        var extension = Path.GetExtension(asset.Path);
        if (!AllowedAssetExtensions.Contains(extension))
        {
            errors.Add($"Asset type is not allowed: {asset.Path}");
        }

        if (string.IsNullOrWhiteSpace(asset.Licence))
        {
            errors.Add($"Asset is missing a licence declaration: {asset.Path}");
        }
    }

    private static void Require(string value, string label, ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add($"{label} is required.");
        }
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9.-]{1,62}[a-z0-9]$", RegexOptions.CultureInvariant)]
    private static partial Regex PackIdPattern();
}

public sealed record PackValidationResult(IReadOnlyList<string> Errors)
{
    public bool IsValid => Errors.Count == 0;
}
