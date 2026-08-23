namespace FreeFlight.CabinControl.Core.Configuration;

public sealed class AppSettings
{
    public const string DefaultAirlinePackId = "freeflight.generic";

    public string UserDisplayName { get; set; } = "FreeFlight User";

    public bool LaunchWithWindows { get; set; }

    public bool StartMinimized { get; set; }

    public bool MinimizeToTray { get; set; } = true;

    public bool StartCabinImmersionAutomatically { get; set; } = true;

    public bool CabinImmersionEnabled { get; set; } = true;

    public bool AutomaticAnnouncementsEnabled { get; set; } = true;

    public bool PassengerAmbienceEnabled { get; set; } = true;

    public bool CrewEventsEnabled { get; set; } = true;

    public bool CabinSoundsEnabled { get; set; } = true;

    public bool BoardingMusicEnabled { get; set; } = true;

    public bool SafetyDemonstrationEnabled { get; set; } = true;

    public bool AircraftEventsEnabled { get; set; } = true;

    public bool SeatbackDisplaysEnabled { get; set; } = true;

    public int MasterVolume { get; set; } = 78;

    public int PassengerAmbienceVolume { get; set; } = 72;

    public int CrewAnnouncementsVolume { get; set; } = 80;

    public int CabinSoundsVolume { get; set; } = 65;

    public int BoardingMusicVolume { get; set; } = 60;

    public int SafetyDemonstrationVolume { get; set; } = 75;

    public int AircraftEventsVolume { get; set; } = 70;

    public string AudioProfile { get; set; } = "Balanced Cabin";

    public string CabinLightingMode { get; set; } = "Cruise";

    public double CabinTargetTemperatureC { get; set; } = 22;

    public string AudioOutputDeviceId { get; set; } = string.Empty;

    public string AudioOutputDeviceName { get; set; } = "System default";

    public string ActiveAirlinePackId { get; set; } = DefaultAirlinePackId;

    public string ActiveAirlineId { get; set; } = "freeflight.virtual";

    public List<CustomAirlineProfileSettings> CustomAirlineProfiles { get; set; } = [];

    public string Theme { get; set; } = "FreeFlight Dark";

    public string AccentColor { get; set; } = "#1476FF";

    public int UiScalePercent { get; set; } = 100;

    public string PerformanceMode { get; set; } = "Balanced";
}
