using FreeFlight.CabinControl.App.Infrastructure;

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

    public string Icao { get; } = icao;

    public string Type { get; } = type;

    public string SoundPack { get; } = soundPack;

    public bool IsInstalled { get; } = isInstalled;

    public string PackAvailability => IsInstalled ? "Installed" : "Pack planned";

    public bool IsActive
    {
        get => _isActive;
        set => SetProperty(ref _isActive, value);
    }
}
