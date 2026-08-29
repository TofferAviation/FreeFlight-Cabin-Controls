using FreeFlight.CabinControl.App.Infrastructure;
using FreeFlight.CabinControl.App.Services;

namespace FreeFlight.CabinControl.App.ViewModels;

public sealed class AirlineProfileViewModel(
    string id,
    string name,
    string icao,
    string type,
    string soundPack,
    bool isInstalled) : ObservableObject
{
    private bool _isActive;

    public string Id { get; } = id;

    public string Name { get; } = name;

    public string Icao { get; } = string.IsNullOrWhiteSpace(icao) ? "—" : icao;

    public string Type { get; } = type;

    public string SoundPack { get; } = soundPack;

    public string? LogoSource { get; } = AirlineLogoCatalog.Resolve(icao);

    public bool HasLogo => LogoSource is not null;

    public bool IsInstalled { get; } = isInstalled;

    public string PackAvailability => IsInstalled ? "Installed" : "Pack planned";

    public bool IsActive
    {
        get => _isActive;
        set => SetProperty(ref _isActive, value);
    }
}
