using System.Windows;
using System.Windows.Input;
using FreeFlight.CabinControl.App.Infrastructure;
using FreeFlight.CabinControl.Core.Configuration;
using FreeFlight.CabinControl.Core.Passengers;
using FreeFlight.CabinControl.Core.Persistence;

namespace FreeFlight.CabinControl.App.ViewModels;

public sealed class SettingsViewModel : PageViewModel
{
    private readonly AppSettings _settings;
    private readonly ISettingsStore _settingsStore;
    private string _selectedSection = "General";
    private string _saveStatus = "No unsaved changes";
    private CabinLayoutProfileOption _selectedCabinLayoutProfile;

    public SettingsViewModel(AppSettings settings, ISettingsStore settingsStore, SharedStatusViewModel status)
        : base("Settings", "Application, aircraft, airline, and user preferences")
    {
        _settings = settings;
        _settingsStore = settingsStore;
        _selectedCabinLayoutProfile = CabinLayoutProfiles.FirstOrDefault(profile =>
            string.Equals(profile.Id, settings.PassengerCabinLayoutId, StringComparison.OrdinalIgnoreCase)) ??
            CabinLayoutProfiles[0];
        _settings.PassengerCabinLayoutId = _selectedCabinLayoutProfile.Id;
        Status = status;
        SaveCommand = new AsyncRelayCommand(SaveAsync, ShowSaveError);
        RestoreDefaultsCommand = new RelayCommand(_ => RestoreDefaults());
        SelectSectionCommand = new RelayCommand(SelectSection);
    }

    public SharedStatusViewModel Status { get; }

    public ICommand SaveCommand { get; }

    public ICommand RestoreDefaultsCommand { get; }

    public ICommand SelectSectionCommand { get; }

    public IReadOnlyList<int> UiScales { get; } = [90, 100, 110, 125, 150];

    public IReadOnlyList<string> Themes { get; } = ["FreeFlight Dark"];

    public IReadOnlyList<CabinLayoutProfileOption> CabinLayoutProfiles => CabinLayoutProfileCatalog.All;

    public string Version => "v0.1.0-dev";

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
        SelectedCabinLayoutProfile = CabinLayoutProfiles.Single(profile =>
            profile.Id == defaults.PassengerCabinLayoutId);
        SaveStatus = "Defaults restored; choose Save Changes to keep them";
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
            "pack://application:,,,/FreeFlight.CabinControl;component/Assets/BA_777_200ER_SeatMap.png",
            "pack://application:,,,/FreeFlight.CabinControl;component/Assets/BA_777_200ER_SeatMap_Horizontal.png",
            "Manual selection · future aircraft match: Boeing 777-200ER",
            420d,
            true,
            "OPERATIONAL · 280 MAPPED SEAT POSITIONS · NOSE LEFT"),
        new(
            "british-airways.777-300",
            PassengerCabinLayout.BritishAirways777300,
            "British Airways 777-300",
            "Operational airline layout",
            "British Airways 777-300 boarding simulation with mapped seats, two-door routing, and live passenger movement.",
            "pack://application:,,,/FreeFlight.CabinControl;component/Assets/BA_777_300_SeatMap.png",
            "pack://application:,,,/FreeFlight.CabinControl;component/Assets/BA_777_300_SeatMap_Horizontal.png",
            "Manual selection · future aircraft match: Boeing 777-300",
            420d,
            true,
            "OPERATIONAL · 266 MAPPED SEAT POSITIONS · NOSE LEFT")
    ];

    public static CabinLayoutProfileOption Resolve(string? id) =>
        All.FirstOrDefault(profile => string.Equals(profile.Id, id, StringComparison.OrdinalIgnoreCase)) ?? All[0];
}
