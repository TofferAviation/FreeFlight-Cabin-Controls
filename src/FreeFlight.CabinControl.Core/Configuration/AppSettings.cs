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

    public bool XPlaneAutoConnect { get; set; } = true;

    public string XPlaneExecutablePath { get; set; } = string.Empty;

    public int XPlaneWebApiPort { get; set; } = 8086;

    public bool SyncXPlaneDoors { get; set; } = true;

    public bool Msfs2024AutoConnect { get; set; } = true;

    public string PreferredSimulator { get; set; } = "Auto";

    public bool AutomaticallyCheckForUpdates { get; set; } = true;

    public string UpdateChannel { get; set; } = "Stable";

    public int PassengerPreviewBookedCount { get; set; } = 219;

    public double PassengerPreviewSpeed { get; set; } = 2d;

    public string PassengerCabinLayoutId { get; set; } = "flightfactor.777v2";

    public string SimBriefPilotId { get; set; } = string.Empty;

    public bool SimBriefAutoSync { get; set; }

    public string GateFlightNumber { get; set; } = "BA117";

    public string GateOriginIata { get; set; } = "LHR";

    public string GateDestinationIata { get; set; } = "JFK";

    public string GateNumber { get; set; } = "B42";

    public string ArrivalGateNumber { get; set; } = "D4";

    public bool AutomaticGateAssignment { get; set; } = true;

    public string ScheduledDepartureLocal { get; set; } = "18:30";

    public int TurnaroundMinutes { get; set; } = 60;

    public bool AutomaticGateTiming { get; set; } = true;

    public int BoardingStartMinutesBeforeDeparture { get; set; } = 45;

    public int FinalBoardingMinutesBeforeDeparture { get; set; } = 5;

    public int GateCloseMinutesBeforeDeparture { get; set; } = 2;

    public bool ManualGateOverride { get; set; }

    public string PassengerNameRegionMix { get; set; } = "Global Mix (Default)";

    public int PassengerGenerationSeed { get; set; } = 112233;

    public string BoardingGroupOrder { get; set; } = "Groups by Cabin (1 → 8)";

    public bool SpecialAssistanceBoardsFirst { get; set; } = true;

    public bool PreventBoardingAfterGateClose { get; set; } = true;

    public string BoardingPassPrinter { get; set; } = "Zebra ZD620 (Preview)";

    public string BagTagPrinter { get; set; } = "Zebra ZD420 (Preview)";

    public bool SoundAlerts { get; set; } = true;

    public string BoardingCallChime { get; set; } = "British Airways";

    public bool AutoArchiveCompletedFlights { get; set; } = true;

    public int ArchiveCompletedFlightsAfterDays { get; set; } = 30;
}
