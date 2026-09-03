using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Runtime.CompilerServices;
using FreeFlight.CabinControl.App.Infrastructure;
using FreeFlight.CabinControl.App.Services;
using FreeFlight.CabinControl.Core.Configuration;
using FreeFlight.CabinControl.Core.Integration;
using FreeFlight.CabinControl.Core.Passengers;
using FreeFlight.CabinControl.Core.Persistence;
using Microsoft.Win32;

namespace FreeFlight.CabinControl.App.ViewModels;

public sealed class SettingsViewModel : PageViewModel
{
    private readonly AppSettings _settings;
    private readonly ISettingsStore _settingsStore;
    private readonly ISimulatorBridge? _simulatorBridge;
    private readonly XPlanePluginInstaller _xPlanePluginInstaller;
    private string _selectedSection = "General";
    private string _saveStatus = "No unsaved changes";
    private string _boardingPassPrinterStatus = "Select an installed Windows queue from Gate Desk";
    private string _bagTagPrinterStatus = "Preview printer ready";
    private CabinLayoutProfileOption _selectedCabinLayoutProfile;
    private string _xPlanePluginStatus;

    public SettingsViewModel(
        AppSettings settings,
        ISettingsStore settingsStore,
        SharedStatusViewModel status,
        ISimulatorBridge? simulatorBridge = null,
        XPlanePluginInstaller? xPlanePluginInstaller = null)
        : base("Settings", "Application, aircraft, airline, and user preferences")
    {
        _settings = settings;
        _settingsStore = settingsStore;
        _simulatorBridge = simulatorBridge;
        _xPlanePluginInstaller = xPlanePluginInstaller ?? new XPlanePluginInstaller();
        _xPlanePluginStatus = _xPlanePluginInstaller.GetStatus(settings.XPlaneExecutablePath);
        _selectedCabinLayoutProfile = CabinLayoutProfiles.FirstOrDefault(profile =>
            string.Equals(profile.Id, settings.PassengerCabinLayoutId, StringComparison.OrdinalIgnoreCase)) ??
            CabinLayoutProfiles[0];
        _settings.PassengerCabinLayoutId = _selectedCabinLayoutProfile.Id;
        Status = status;
        SaveCommand = new AsyncRelayCommand(SaveAsync, ShowSaveError);
        RestoreDefaultsCommand = new RelayCommand(_ => RestoreDefaults());
        SelectSectionCommand = new RelayCommand(SelectSection);
        TestBoardingPassPrinterCommand = new RelayCommand(_ => BoardingPassPrinterStatus = "Use Print Pass in Gate Desk to send a real Windows print job");
        TestBagTagPrinterCommand = new RelayCommand(_ => BagTagPrinterStatus = $"Test tag queued at {DateTime.Now:HH:mm}");
        RandomizePassengerSeedCommand = new RelayCommand(_ => PassengerGenerationSeed = Random.Shared.Next(100000, 999999));
        ReconnectXPlaneCommand = new RelayCommand(_ => _simulatorBridge?.RequestReconnect());
        SelectXPlaneFolderCommand = new RelayCommand(_ => SelectXPlaneFolder());
        SelectXPlaneExecutableCommand = new RelayCommand(_ => SelectXPlaneExecutable());
        InstallXPlanePluginCommand = new RelayCommand(_ => InstallXPlanePlugin());
    }

    public SharedStatusViewModel Status { get; }

    public ICommand SaveCommand { get; }

    public ICommand RestoreDefaultsCommand { get; }

    public ICommand SelectSectionCommand { get; }

    public ICommand TestBoardingPassPrinterCommand { get; }

    public ICommand TestBagTagPrinterCommand { get; }

    public ICommand RandomizePassengerSeedCommand { get; }

    public ICommand ReconnectXPlaneCommand { get; }

    public ICommand SelectXPlaneFolderCommand { get; }

    public ICommand SelectXPlaneExecutableCommand { get; }

    public ICommand InstallXPlanePluginCommand { get; }

    public string XPlanePluginStatus
    {
        get => _xPlanePluginStatus;
        private set => SetProperty(ref _xPlanePluginStatus, value);
    }

    public bool CanInstallXPlanePlugin => _xPlanePluginInstaller.CanInstall(XPlaneExecutablePath);

    public IReadOnlyList<int> UiScales { get; } = [90, 100, 110, 125, 150];

    public IReadOnlyList<string> Themes { get; } = ["FreeFlight Dark"];

    public IReadOnlyList<int> BoardingStartOffsets { get; } = [60, 45, 30, 20];

    public IReadOnlyList<int> TurnaroundDurations { get; } = [45, 60, 75, 90];

    public IReadOnlyList<int> FinalBoardingOffsets { get; } = [10, 5, 3];

    public IReadOnlyList<int> GateCloseOffsets { get; } = [5, 3, 2, 1];

    public IReadOnlyList<string> NameRegionMixes { get; } = ["Global Mix (Default)", "Europe", "North America", "Asia Pacific"];

    public IReadOnlyList<string> BoardingGroupOrders { get; } = ["Groups by Cabin (1 → 8)", "Back to Front", "Outside In"];

    public IReadOnlyList<string> BoardingCallChimes { get; } = ["British Airways", "FreeFlight Standard", "Silent"];

    public IReadOnlyList<CabinLayoutProfileOption> CabinLayoutProfiles => CabinLayoutProfileCatalog.All;

    public string Version => $"v{typeof(SettingsViewModel).Assembly.GetName().Version?.ToString(3) ?? "0.0.0"}";

    public string XPlaneExecutablePath
    {
        get => _settings.XPlaneExecutablePath;
        set => SetSetting(value.Trim(), current => _settings.XPlaneExecutablePath = current);
    }

    public string XPlaneExecutableLabel => string.IsNullOrWhiteSpace(XPlaneExecutablePath)
        ? "X-Plane folder not selected"
        : XPlaneExecutablePath;

    public bool Msfs2024AutoConnect
    {
        get => _settings.Msfs2024AutoConnect;
        set
        {
            SetSetting(value, current => _settings.Msfs2024AutoConnect = current);
            _simulatorBridge?.RequestReconnect();
        }
    }

    public bool XPlaneAutoConnect
    {
        get => _settings.XPlaneAutoConnect;
        set
        {
            SetSetting(value, current => _settings.XPlaneAutoConnect = current);
            _simulatorBridge?.RequestReconnect();
        }
    }

    public int XPlaneWebApiPort
    {
        get => _settings.XPlaneWebApiPort;
        set
        {
            var sanitized = value is >= 1 and <= 65_535 ? value : 8086;
            SetSetting(sanitized, current => _settings.XPlaneWebApiPort = current);
        }
    }

    public bool SyncXPlaneDoors
    {
        get => _settings.SyncXPlaneDoors;
        set => SetSetting(value, current => _settings.SyncXPlaneDoors = current);
    }

    public bool SyncSimulatorSeatbeltSign
    {
        get => _settings.SyncSimulatorSeatbeltSign;
        set => SetSetting(value, current => _settings.SyncSimulatorSeatbeltSign = current);
    }

    public bool AutomaticallyCheckForUpdates
    {
        get => _settings.AutomaticallyCheckForUpdates;
        set => SetSetting(value, current => _settings.AutomaticallyCheckForUpdates = current);
    }

    private void SelectXPlaneExecutable()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select the X-Plane executable",
            Filter = "X-Plane executable|X-Plane.exe|Executable files|*.exe",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        XPlaneExecutablePath = dialog.FileName;
        OnPropertyChanged(nameof(XPlaneExecutableLabel));
        RefreshXPlanePluginStatus();
        _simulatorBridge?.RequestReconnect();
    }

    private void SelectXPlaneFolder()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select the X-Plane installation folder",
            Multiselect = false
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var executable = Path.Combine(dialog.FolderName, "X-Plane.exe");
        XPlaneExecutablePath = File.Exists(executable) ? executable : dialog.FolderName;
        OnPropertyChanged(nameof(XPlaneExecutableLabel));
        RefreshXPlanePluginStatus();
        _simulatorBridge?.RequestReconnect();
    }

    private void InstallXPlanePlugin()
    {
        try
        {
            XPlanePluginStatus = _xPlanePluginInstaller.Install(XPlaneExecutablePath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            XPlanePluginStatus = $"Plugin installation failed: {exception.Message}";
        }
        OnPropertyChanged(nameof(CanInstallXPlanePlugin));
    }

    private void RefreshXPlanePluginStatus()
    {
        XPlanePluginStatus = _xPlanePluginInstaller.GetStatus(XPlaneExecutablePath);
        OnPropertyChanged(nameof(CanInstallXPlanePlugin));
    }

    public string BoardingPassPrinterStatus
    {
        get => _boardingPassPrinterStatus;
        private set => SetProperty(ref _boardingPassPrinterStatus, value);
    }

    public string BagTagPrinterStatus
    {
        get => _bagTagPrinterStatus;
        private set => SetProperty(ref _bagTagPrinterStatus, value);
    }

    public string SelectedSection
    {
        get => _selectedSection;
        private set => SetProperty(ref _selectedSection, value);
    }

    public string SaveStatus
    {
        get => _saveStatus;
        private set => SetProperty(ref _saveStatus, value);
    }

    public string UserDisplayName
    {
        get => _settings.UserDisplayName;
        set
        {
            _settings.UserDisplayName = value;
            OnPropertyChanged();
            MarkDirty();
        }
    }

    public bool LaunchWithWindows
    {
        get => _settings.LaunchWithWindows;
        set
        {
            _settings.LaunchWithWindows = value;
            OnPropertyChanged();
            MarkDirty();
        }
    }

    public bool StartMinimized
    {
        get => _settings.StartMinimized;
        set
        {
            _settings.StartMinimized = value;
            OnPropertyChanged();
            MarkDirty();
        }
    }

    public bool MinimizeToTray
    {
        get => _settings.MinimizeToTray;
        set
        {
            _settings.MinimizeToTray = value;
            OnPropertyChanged();
            MarkDirty();
        }
    }

    public bool StartCabinImmersionAutomatically
    {
        get => _settings.StartCabinImmersionAutomatically;
        set
        {
            _settings.StartCabinImmersionAutomatically = value;
            OnPropertyChanged();
            MarkDirty();
        }
    }

    public string Theme
    {
        get => _settings.Theme;
        set
        {
            _settings.Theme = value;
            OnPropertyChanged();
            MarkDirty();
        }
    }

    public int UiScalePercent
    {
        get => _settings.UiScalePercent;
        set
        {
            _settings.UiScalePercent = value;
            OnPropertyChanged();
            MarkDirty();
        }
    }

    public string ActiveAirlinePackId => _settings.ActiveAirlinePackId;

    public string ActiveAirlinePackName => "FreeFlight Generic";

    public string SimBriefPilotId
    {
        get => _settings.SimBriefPilotId;
        set => SetSetting(value.Trim(), current => _settings.SimBriefPilotId = current);
    }

    public string GateFlightNumber
    {
        get => _settings.GateFlightNumber;
        set => SetSetting(value.Trim().ToUpperInvariant(), current => _settings.GateFlightNumber = current);
    }

    public string GateOriginIata
    {
        get => _settings.GateOriginIata;
        set => SetSetting(value.Trim().ToUpperInvariant(), current => _settings.GateOriginIata = current);
    }

    public string GateDestinationIata
    {
        get => _settings.GateDestinationIata;
        set => SetSetting(value.Trim().ToUpperInvariant(), current => _settings.GateDestinationIata = current);
    }

    public string GateNumber
    {
        get => _settings.GateNumber;
        set => SetSetting(value.Trim().ToUpperInvariant(), current => _settings.GateNumber = current);
    }

    public string ArrivalGateNumber
    {
        get => _settings.ArrivalGateNumber;
        set => SetSetting(value.Trim().ToUpperInvariant(), current => _settings.ArrivalGateNumber = current);
    }

    public bool AutomaticGateAssignment
    {
        get => _settings.AutomaticGateAssignment;
        set => SetSetting(value, current => _settings.AutomaticGateAssignment = current);
    }

    public string ScheduledDepartureLocal
    {
        get => _settings.ScheduledDepartureLocal;
        set => SetSetting(value.Trim(), current => _settings.ScheduledDepartureLocal = current);
    }

    public bool AutomaticGateTiming
    {
        get => _settings.AutomaticGateTiming;
        set => SetSetting(value, current => _settings.AutomaticGateTiming = current);
    }

    public int TurnaroundMinutes
    {
        get => _settings.TurnaroundMinutes;
        set => SetSetting(value, current => _settings.TurnaroundMinutes = current);
    }

    public int BoardingStartMinutesBeforeDeparture
    {
        get => _settings.BoardingStartMinutesBeforeDeparture;
        set => SetSetting(value, current => _settings.BoardingStartMinutesBeforeDeparture = current);
    }

    public int FinalBoardingMinutesBeforeDeparture
    {
        get => _settings.FinalBoardingMinutesBeforeDeparture;
        set => SetSetting(value, current => _settings.FinalBoardingMinutesBeforeDeparture = current);
    }

    public int GateCloseMinutesBeforeDeparture
    {
        get => _settings.GateCloseMinutesBeforeDeparture;
        set => SetSetting(value, current => _settings.GateCloseMinutesBeforeDeparture = current);
    }

    public bool ManualGateOverride
    {
        get => _settings.ManualGateOverride;
        set => SetSetting(value, current => _settings.ManualGateOverride = current);
    }

    public string PassengerNameRegionMix
    {
        get => _settings.PassengerNameRegionMix;
        set => SetSetting(value, current => _settings.PassengerNameRegionMix = current);
    }

    public int PassengerGenerationSeed
    {
        get => _settings.PassengerGenerationSeed;
        set => SetSetting(value, current => _settings.PassengerGenerationSeed = current);
    }

    public string BoardingGroupOrder
    {
        get => _settings.BoardingGroupOrder;
        set => SetSetting(value, current => _settings.BoardingGroupOrder = current);
    }

    public bool SpecialAssistanceBoardsFirst
    {
        get => _settings.SpecialAssistanceBoardsFirst;
        set => SetSetting(value, current => _settings.SpecialAssistanceBoardsFirst = current);
    }

    public bool PreventBoardingAfterGateClose
    {
        get => _settings.PreventBoardingAfterGateClose;
        set => SetSetting(value, current => _settings.PreventBoardingAfterGateClose = current);
    }

    public string BoardingPassPrinter
    {
        get => _settings.BoardingPassPrinter;
        set => SetSetting(value, current => _settings.BoardingPassPrinter = current);
    }

    public string BagTagPrinter
    {
        get => _settings.BagTagPrinter;
        set => SetSetting(value, current => _settings.BagTagPrinter = current);
    }

    public bool SoundAlerts
    {
        get => _settings.SoundAlerts;
        set => SetSetting(value, current => _settings.SoundAlerts = current);
    }

    public string BoardingCallChime
    {
        get => _settings.BoardingCallChime;
        set => SetSetting(value, current => _settings.BoardingCallChime = current);
    }

    public bool AutoArchiveCompletedFlights
    {
        get => _settings.AutoArchiveCompletedFlights;
        set => SetSetting(value, current => _settings.AutoArchiveCompletedFlights = current);
    }

    public int ArchiveCompletedFlightsAfterDays
    {
        get => _settings.ArchiveCompletedFlightsAfterDays;
        set => SetSetting(value, current => _settings.ArchiveCompletedFlightsAfterDays = current);
    }

    public CabinLayoutProfileOption SelectedCabinLayoutProfile
    {
        get => _selectedCabinLayoutProfile;
        set
        {
            if (value is null || !SetProperty(ref _selectedCabinLayoutProfile, value))
            {
                return;
            }

            _settings.PassengerCabinLayoutId = value.Id;
            MarkDirty();
        }
    }

    public void ApplyCabinLayoutSelection(string? profileId)
    {
        var profile = CabinLayoutProfileCatalog.Resolve(profileId);
        if (SetProperty(ref _selectedCabinLayoutProfile, profile, nameof(SelectedCabinLayoutProfile)))
        {
            _settings.PassengerCabinLayoutId = profile.Id;
        }
    }

    private void SelectSection(object? parameter)
    {
        if (parameter is string section)
        {
            SelectedSection = section;
        }
    }

    private async Task SaveAsync()
    {
        await _settingsStore.SaveAsync(_settings);
        SaveStatus = $"Saved at {DateTime.Now:t}";
    }

    private void RestoreDefaults()
    {
        var defaults = new AppSettings();
        UserDisplayName = defaults.UserDisplayName;
        LaunchWithWindows = defaults.LaunchWithWindows;
        StartMinimized = defaults.StartMinimized;
        MinimizeToTray = defaults.MinimizeToTray;
        StartCabinImmersionAutomatically = defaults.StartCabinImmersionAutomatically;
        Theme = defaults.Theme;
        UiScalePercent = defaults.UiScalePercent;
        XPlaneAutoConnect = defaults.XPlaneAutoConnect;
        XPlaneWebApiPort = defaults.XPlaneWebApiPort;
        SyncXPlaneDoors = defaults.SyncXPlaneDoors;
        SyncSimulatorSeatbeltSign = defaults.SyncSimulatorSeatbeltSign;
        XPlaneExecutablePath = defaults.XPlaneExecutablePath;
        OnPropertyChanged(nameof(XPlaneExecutableLabel));
        RefreshXPlanePluginStatus();
        Msfs2024AutoConnect = defaults.Msfs2024AutoConnect;
        AutomaticallyCheckForUpdates = defaults.AutomaticallyCheckForUpdates;
        SelectedCabinLayoutProfile = CabinLayoutProfiles.Single(profile =>
            profile.Id == defaults.PassengerCabinLayoutId);
        SimBriefPilotId = defaults.SimBriefPilotId;
        GateFlightNumber = defaults.GateFlightNumber;
        GateOriginIata = defaults.GateOriginIata;
        GateDestinationIata = defaults.GateDestinationIata;
        GateNumber = defaults.GateNumber;
        ArrivalGateNumber = defaults.ArrivalGateNumber;
        AutomaticGateAssignment = defaults.AutomaticGateAssignment;
        ScheduledDepartureLocal = defaults.ScheduledDepartureLocal;
        TurnaroundMinutes = defaults.TurnaroundMinutes;
        AutomaticGateTiming = defaults.AutomaticGateTiming;
        BoardingStartMinutesBeforeDeparture = defaults.BoardingStartMinutesBeforeDeparture;
        FinalBoardingMinutesBeforeDeparture = defaults.FinalBoardingMinutesBeforeDeparture;
        GateCloseMinutesBeforeDeparture = defaults.GateCloseMinutesBeforeDeparture;
        ManualGateOverride = defaults.ManualGateOverride;
        PassengerNameRegionMix = defaults.PassengerNameRegionMix;
        PassengerGenerationSeed = defaults.PassengerGenerationSeed;
        BoardingGroupOrder = defaults.BoardingGroupOrder;
        SpecialAssistanceBoardsFirst = defaults.SpecialAssistanceBoardsFirst;
        PreventBoardingAfterGateClose = defaults.PreventBoardingAfterGateClose;
        BoardingPassPrinter = defaults.BoardingPassPrinter;
        BagTagPrinter = defaults.BagTagPrinter;
        SoundAlerts = defaults.SoundAlerts;
        BoardingCallChime = defaults.BoardingCallChime;
        AutoArchiveCompletedFlights = defaults.AutoArchiveCompletedFlights;
        ArchiveCompletedFlightsAfterDays = defaults.ArchiveCompletedFlightsAfterDays;
        SaveStatus = "Defaults restored; choose Save Changes to keep them";
    }

    private void SetSetting<T>(T value, Action<T> apply, [CallerMemberName] string? propertyName = null)
    {
        apply(value);
        OnPropertyChanged(propertyName);
        MarkDirty();
    }

    private void MarkDirty() => SaveStatus = "Unsaved changes";

    private void ShowSaveError(Exception exception)
    {
        SaveStatus = "Could not save settings";
        MessageBox.Show(exception.Message, "Save failed", MessageBoxButton.OK, MessageBoxImage.Error);
    }
}

public sealed record CabinLayoutProfileOption(
    string Id,
    PassengerCabinLayout Layout,
    string Name,
    string ProfileType,
    string Description,
    string PreviewUri,
    string LivePreviewUri,
    string MatchHint,
    double PreviewWidth,
    bool IsOperational,
    string LivePreviewStatus)
{
    public bool UsesFallbackLivePreview => LivePreviewUri.Contains("Ff777CabinLayout", StringComparison.OrdinalIgnoreCase);

    public Rect LivePreviewViewbox => UsesFallbackLivePreview
        ? new Rect(0d, 62d, 1033d, 192d)
        : Layout switch
        {
            PassengerCabinLayout.BritishAirways777200Er => new Rect(0d, 0d, 1033d, 192d),
            PassengerCabinLayout.BritishAirways777300 => new Rect(0d, 0d, 1033d, 192d),
            _ => new Rect(0d, 62d, 1033d, 192d)
        };

    public Stretch LivePreviewStretch => UsesFallbackLivePreview
        ? Stretch.Fill
        : Stretch.Uniform;

    public override string ToString() => Name;
}

public static class CabinLayoutProfileCatalog
{
    public static IReadOnlyList<CabinLayoutProfileOption> All { get; } =
    [
        new(
            "flightfactor.777v2",
            PassengerCabinLayout.FlightFactor777V2,
            "FlightFactor 777 v2 cabin",
            "Operational preview",
            "The coded FreeFlight schematic used for live boarding, deboarding, seat markers, and door routing.",
            "pack://application:,,,/FreeFlight.CabinControl;component/Assets/Ff777CabinLayout.png",
            "pack://application:,,,/FreeFlight.CabinControl;component/Assets/Ff777CabinLayout.png",
            "Manual selection · future adapter ID: FlightFactor 777 v2",
            470d,
            true,
            "OPERATIONAL · 311 MAPPED SEAT POSITIONS"),
        new(
            "british-airways.777-200er",
            PassengerCabinLayout.BritishAirways777200Er,
            "British Airways 777-200ER",
            "Operational airline layout",
            "British Airways 777-200ER boarding simulation with mapped seats, two-door routing, and live passenger movement.",
            "pack://application:,,,/FreeFlight.CabinControl;component/Assets/CabinLayouts/BritishAirways777200Er.png",
            "pack://application:,,,/FreeFlight.CabinControl;component/Assets/CabinLayouts/BritishAirways777200Er.png",
            "Manual selection · future aircraft match: Boeing 777-200ER",
            420d,
            true,
            "OPERATIONAL · 272 MAPPED SEAT POSITIONS · NOSE LEFT"),
        new(
            "british-airways.777-300",
            PassengerCabinLayout.BritishAirways777300,
            "British Airways 777-300",
            "Operational airline layout",
            "British Airways 777-300 boarding simulation with mapped seats, two-door routing, and live passenger movement.",
            "pack://application:,,,/FreeFlight.CabinControl;component/Assets/CabinLayouts/BritishAirways777300.png",
            "pack://application:,,,/FreeFlight.CabinControl;component/Assets/CabinLayouts/BritishAirways777300.png",
            "Manual selection · future aircraft match: Boeing 777-300",
            420d,
            true,
            "OPERATIONAL · 256 MAPPED SEAT POSITIONS · NOSE LEFT")
    ];

    public static CabinLayoutProfileOption Resolve(string? id) =>
        All.FirstOrDefault(profile => string.Equals(profile.Id, id, StringComparison.OrdinalIgnoreCase)) ?? All[0];
}
