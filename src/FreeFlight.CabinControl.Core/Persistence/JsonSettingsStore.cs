using System.Text.Json;
using FreeFlight.CabinControl.Core.Configuration;

namespace FreeFlight.CabinControl.Core.Persistence;

public interface ISettingsStore
{
    Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);
}

public sealed class JsonSettingsStore : ISettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly string _settingsPath;

    public JsonSettingsStore(string settingsPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsPath);
        _settingsPath = Path.GetFullPath(settingsPath);
    }

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_settingsPath))
        {
            return new AppSettings();
        }

        await using var stream = File.OpenRead(_settingsPath);
        var settings = await JsonSerializer.DeserializeAsync<AppSettings>(stream, SerializerOptions, cancellationToken)
            .ConfigureAwait(false) ?? new AppSettings();
        NormalizeBritishAirwaysFleetConnection(settings);
        return settings;
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        NormalizeBritishAirwaysFleetConnection(settings);

        var directory = Path.GetDirectoryName(_settingsPath)
            ?? throw new InvalidOperationException("The settings path has no parent directory.");
        Directory.CreateDirectory(directory);

        var temporaryPath = _settingsPath + ".tmp";
        await using (var stream = new FileStream(
                         temporaryPath,
                         FileMode.Create,
                         FileAccess.Write,
                         FileShare.None,
                         4096,
                         FileOptions.Asynchronous | FileOptions.WriteThrough))
        {
            await JsonSerializer.SerializeAsync(stream, settings, SerializerOptions, cancellationToken)
                .ConfigureAwait(false);
        }

        File.Move(temporaryPath, _settingsPath, true);
    }

    private static void NormalizeBritishAirwaysFleetConnection(AppSettings settings)
    {
        // Replace every historical local, preview, or custom endpoint with the
        // single public BAV API. A shared device key is no longer used by the
        // desktop client, so do not carry one forward into a new settings file.
        settings.FleetApiBaseUrl = AppSettings.BritishAirwaysVirtualWebsiteUrl;
        settings.FleetApiAccessKey = string.Empty;
    }
}
