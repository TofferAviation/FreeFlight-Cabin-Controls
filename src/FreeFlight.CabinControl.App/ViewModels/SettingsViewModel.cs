using System.Windows;
using System.Windows.Input;
using FreeFlight.CabinControl.App.Infrastructure;
using FreeFlight.CabinControl.Core.Configuration;
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

    public IReadOnlyList<CabinLayoutProfileOption> CabinLayoutProfiles { get; } =
    [
        new(
            "flightfactor.777v2",
            "FlightFactor 777 v2 cabin",
            "Operational preview",
            "The coded FreeFlight schematic used for live boarding, deboarding, seat markers, and door routing.",
            "pack://application:,,,/FreeFlight.CabinControl;component/Assets/Ff777CabinLayout.png",
            "Manual selection · future adapter ID: FlightFactor 777 v2",
            470d),
        new(
            "british-airways.777-200er",
            "British Airways 777-200ER",
            "Airline seat-map reference",
            "The supplied British Airways 777-200ER layout, stored as private airline-pack content for aircraft-profile matching.",
            "pack://application:,,,/FreeFlight.CabinControl;component/Assets/BA_777_200ER_SeatMap.png",
            "Manual selection · future aircraft match: Boeing 777-200ER",
            420d),
        new(
            "british-airways.777-300",
            "British Airways 777-300",
            "Airline seat-map reference",
            "The supplied British Airways 777-300 layout, stored as private airline-pack content for aircraft-profile matching.",
            "pack://application:,,,/FreeFlight.CabinControl;component/Assets/BA_777_300_SeatMap.png",
            "Manual selection · future aircraft match: Boeing 777-300",
            420d)
    ];

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
    string Name,
    string ProfileType,
    string Description,
    string PreviewUri,
    string MatchHint,
    double PreviewWidth)
{
    public override string ToString() => Name;
}
