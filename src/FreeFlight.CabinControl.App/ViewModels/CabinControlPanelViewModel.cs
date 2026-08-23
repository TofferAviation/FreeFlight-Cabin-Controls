using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using FreeFlight.CabinControl.App.Infrastructure;
using FreeFlight.CabinControl.Core.Configuration;
using FreeFlight.CabinControl.Core.Persistence;

namespace FreeFlight.CabinControl.App.ViewModels;

public sealed class CabinControlPanelViewModel : PageViewModel
{
    private readonly AppSettings _settings;
    private readonly ISettingsStore _settingsStore;
    private string _selectedPanel = "CSCP Main Menu";
    private string _previousPanel = "CSCP Main Menu";
    private string _lastAction = "Panel ready — aircraft bridge offline";
    private string _saveStatus = "Changes are stored locally";
    private int _queueDepth;
    private string _selectedLightingArea = "All Cabin";
    private bool _passengerCallsEnabled = true;
    private bool _lavatoryCallsEnabled = true;
    private bool _attendantChimeEnabled = true;
    private int _activeCalls;
    private string _selectedTemperatureZone = "Zone 1";
    private int _zone1TemperatureC = 23;
    private int _zone2TemperatureC = 23;
    private int _zone3TemperatureC = 22;
    private int _paVolumeLevel = 5;
    private bool _ambientNoiseSensorEnabled = true;
    private bool _automaticPaVolumeEnabled = true;
    private int _displayBrightness = 70;
    private string _displayMode = "Day";
    private bool _displayPowerOn = true;
    private int _boardingMusicLevel;
    private int _selectedBoardingProgram = 1;
    private bool _isBoardingMusicPlaying;
    private Uri? _boardingMusicLocalSource;
    private string _boardingMusicPreviewStatus = "Program 1 recording is not installed";
    private string _safetyVideoPreviewStatus = "Local BA_Safety_Video.mp4 is not installed";
    private bool _isSafetyVideoInProgress;
    private bool _hasLocalSafetyVideo;
    private bool _isUsingLocalSafetyVideo;
    private Uri? _safetyVideoLocalSource;

    public CabinControlPanelViewModel(
        AppSettings settings,
        ISettingsStore settingsStore,
        SharedStatusViewModel status,
        string? safetyVideoLocalFilePath = null,
        string? boardingMusicDirectory = null)
        : base("Cabin Area Control Panel", "FlightFactor 777 v2 cabin systems and media control")
    {
        _settings = settings;
        _settingsStore = settingsStore;
        _boardingMusicLevel = Math.Clamp((int)Math.Round(settings.BoardingMusicVolume / 10d), 1, 10);
        BoardingMusicDirectory = boardingMusicDirectory ?? Path.Combine(
            AppContext.BaseDirectory, "content-packs", "british-airways", "audio", "boarding");
        SafetyVideoLocalFilePath = safetyVideoLocalFilePath ?? Path.Combine(
            AppContext.BaseDirectory, "content-packs", "british-airways", "media", "BA_Safety_Video.mp4");
        _hasLocalSafetyVideo = File.Exists(SafetyVideoLocalFilePath);
        _safetyVideoPreviewStatus = _hasLocalSafetyVideo
            ? "Built-in British Airways safety video ready"
            : "Local BA_Safety_Video.mp4 is not installed";
        SelectFirstInstalledBoardingProgram();
        RefreshSelectedBoardingProgram();
        Status = status;

        SelectPanelCommand = new RelayCommand(SelectPanel);
        MainMenuCommand = new RelayCommand(_ => NavigateToMainMenu());
        PreviousMenuCommand = new RelayCommand(_ => NavigateToPreviousMenu());
        ExecuteActionCommand = new RelayCommand(ExecuteAction);
        QueueCommand = new RelayCommand(QueueEvent);
        StartSafetyVideoCommand = new RelayCommand(_ => StartSafetyVideo());
        StopSafetyVideoCommand = new RelayCommand(_ => StopSafetyVideo());
        ClearQueueCommand = new RelayCommand(_ => ClearQueue());
        SaveCommand = new AsyncRelayCommand(SaveAsync, ShowSaveError);
    }

    public SharedStatusViewModel Status { get; }

    public ICommand SelectPanelCommand { get; }

    public ICommand MainMenuCommand { get; }

    public ICommand PreviousMenuCommand { get; }

    public ICommand ExecuteActionCommand { get; }

    public ICommand QueueCommand { get; }

    public ICommand StartSafetyVideoCommand { get; }

    public ICommand StopSafetyVideoCommand { get; }

    public ICommand ClearQueueCommand { get; }

    public ICommand SaveCommand { get; }

    public ObservableCollection<string> ActivityQueue { get; } = [];

    public string SafetyVideoTitle => "British Airways Safety Video 2024";

    public double SafetyVideoVolume => _settings.SafetyDemonstrationEnabled
        ? Math.Clamp(_settings.SafetyDemonstrationVolume / 100d, 0d, 1d)
        : 0d;

    public string BoardingMusicDirectory { get; }

    public string SelectedBoardingProgramTitle => GetBoardingProgram(SelectedBoardingProgram).Title;

    public string SelectedBoardingProgramCredit => GetBoardingProgram(SelectedBoardingProgram).Credit;

    public string SelectedBoardingProgramFilePath => Path.Combine(
        BoardingMusicDirectory,
        GetBoardingProgram(SelectedBoardingProgram).FileName);

    public Uri? BoardingMusicLocalSource
    {
        get => _boardingMusicLocalSource;
        private set => SetProperty(ref _boardingMusicLocalSource, value);
    }

    public bool HasSelectedBoardingMusic => BoardingMusicLocalSource is not null;

    public bool IsBoardingMusicPlaying
    {
        get => _isBoardingMusicPlaying;
        private set
        {
            if (SetProperty(ref _isBoardingMusicPlaying, value))
            {
                OnPropertyChanged(nameof(BoardingMusicStatus));
            }
        }
    }

    public string BoardingMusicPreviewStatus
    {
        get => _boardingMusicPreviewStatus;
        private set => SetProperty(ref _boardingMusicPreviewStatus, value);
    }

    public string SafetyVideoLocalFilePath { get; }

    public bool HasLocalSafetyVideo
    {
        get => _hasLocalSafetyVideo;
        private set => SetProperty(ref _hasLocalSafetyVideo, value);
    }

    public bool IsUsingLocalSafetyVideo
    {
        get => _isUsingLocalSafetyVideo;
        private set => SetProperty(ref _isUsingLocalSafetyVideo, value);
    }

    public Uri? SafetyVideoLocalSource
    {
        get => _safetyVideoLocalSource;
        private set => SetProperty(ref _safetyVideoLocalSource, value);
    }

    public bool IsSafetyVideoInProgress
    {
        get => _isSafetyVideoInProgress;
        private set => SetProperty(ref _isSafetyVideoInProgress, value);
    }

    public string SafetyVideoPreviewStatus
    {
        get => _safetyVideoPreviewStatus;
        private set => SetProperty(ref _safetyVideoPreviewStatus, value);
    }

    public string SelectedPanel
    {
        get => _selectedPanel;
        private set => SetProperty(ref _selectedPanel, value);
    }

    public string PreviousPanel
    {
        get => _previousPanel;
        private set => SetProperty(ref _previousPanel, value);
    }

    public string LastAction
    {
        get => _lastAction;
        private set => SetProperty(ref _lastAction, value);
    }

    public string SaveStatus
    {
        get => _saveStatus;
        private set => SetProperty(ref _saveStatus, value);
    }

    public int QueueDepth
    {
        get => _queueDepth;
        private set => SetProperty(ref _queueDepth, value);
    }

    public string SelectedLightingArea
    {
        get => _selectedLightingArea;
        private set => SetProperty(ref _selectedLightingArea, value);
    }

    public string CabinLightingMode
    {
        get => _settings.CabinLightingMode;
        private set
        {
            if (_settings.CabinLightingMode == value)
            {
                return;
            }

            _settings.CabinLightingMode = value;
            OnPropertyChanged();
            MarkChanged();
        }
    }

    public bool PassengerCallsEnabled
    {
        get => _passengerCallsEnabled;
        private set => SetProperty(ref _passengerCallsEnabled, value);
    }

    public bool LavatoryCallsEnabled
    {
        get => _lavatoryCallsEnabled;
        private set => SetProperty(ref _lavatoryCallsEnabled, value);
    }

    public bool AttendantChimeEnabled
    {
        get => _attendantChimeEnabled;
        private set
        {
            if (SetProperty(ref _attendantChimeEnabled, value))
            {
                OnPropertyChanged(nameof(AttendantChimeStatus));
            }
        }
    }

    public string AttendantChimeStatus => AttendantChimeEnabled ? "ENABLED" : "DISABLED";

    public int ActiveCalls
    {
        get => _activeCalls;
        private set => SetProperty(ref _activeCalls, value);
    }

    public string SelectedTemperatureZone
    {
        get => _selectedTemperatureZone;
        private set
        {
            if (SetProperty(ref _selectedTemperatureZone, value))
            {
                OnPropertyChanged(nameof(SelectedTemperatureC));
            }
        }
    }

    public int Zone1TemperatureC
    {
        get => _zone1TemperatureC;
        private set
        {
            if (SetProperty(ref _zone1TemperatureC, value) && SelectedTemperatureZone == "Zone 1")
            {
                OnPropertyChanged(nameof(SelectedTemperatureC));
            }
        }
    }

    public int Zone2TemperatureC
    {
        get => _zone2TemperatureC;
        private set
        {
            if (SetProperty(ref _zone2TemperatureC, value) && SelectedTemperatureZone == "Zone 2")
            {
                OnPropertyChanged(nameof(SelectedTemperatureC));
            }
        }
    }

    public int Zone3TemperatureC
    {
        get => _zone3TemperatureC;
        private set
        {
            if (SetProperty(ref _zone3TemperatureC, value) && SelectedTemperatureZone == "Zone 3")
            {
                OnPropertyChanged(nameof(SelectedTemperatureC));
            }
        }
    }

    public int SelectedTemperatureC => SelectedTemperatureZone switch
    {
        "Zone 2" => Zone2TemperatureC,
        "Zone 3" => Zone3TemperatureC,
        _ => Zone1TemperatureC
    };

    public int PaVolumeLevel
    {
        get => _paVolumeLevel;
        private set => SetProperty(ref _paVolumeLevel, value);
    }

    public bool AmbientNoiseSensorEnabled
    {
        get => _ambientNoiseSensorEnabled;
        private set
        {
            if (SetProperty(ref _ambientNoiseSensorEnabled, value))
            {
                OnPropertyChanged(nameof(AmbientNoiseSensorStatus));
            }
        }
    }

    public string AmbientNoiseSensorStatus => AmbientNoiseSensorEnabled ? "ON" : "OFF";

    public bool AutomaticPaVolumeEnabled
    {
        get => _automaticPaVolumeEnabled;
        private set
        {
            if (SetProperty(ref _automaticPaVolumeEnabled, value))
            {
                OnPropertyChanged(nameof(AutomaticPaVolumeStatus));
            }
        }
    }

    public string AutomaticPaVolumeStatus => AutomaticPaVolumeEnabled ? "AUTOMATIC VOLUME: ON" : "AUTOMATIC VOLUME: OFF";

    public int DisplayBrightness
    {
        get => _displayBrightness;
        private set => SetProperty(ref _displayBrightness, value);
    }

    public string DisplayMode
    {
        get => _displayMode;
        private set
        {
            if (SetProperty(ref _displayMode, value))
            {
                OnPropertyChanged(nameof(DisplayStatus));
            }
        }
    }

    public bool DisplayPowerOn
    {
        get => _displayPowerOn;
        private set
        {
            if (SetProperty(ref _displayPowerOn, value))
            {
                OnPropertyChanged(nameof(DisplayStatus));
            }
        }
    }

    public string DisplayStatus => DisplayPowerOn ? $"{DisplayMode.ToUpperInvariant()} MODE ACTIVE" : "DISPLAY OFF";

    public bool BoardingMusicEnabled
    {
        get => _settings.BoardingMusicEnabled;
        private set
        {
            if (_settings.BoardingMusicEnabled == value)
            {
                return;
            }

            _settings.BoardingMusicEnabled = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(BoardingMusicStatus));
            MarkChanged();
        }
    }

    public string BoardingMusicStatus => IsBoardingMusicPlaying ? "PLAYING" : "STOPPED";

    public int BoardingMusicLevel
    {
        get => _boardingMusicLevel;
        private set
        {
            if (!SetProperty(ref _boardingMusicLevel, value))
            {
                return;
            }

            _settings.BoardingMusicVolume = value * 10;
            MarkChanged();
        }
    }

    public int SelectedBoardingProgram
    {
        get => _selectedBoardingProgram;
        private set
        {
            if (!SetProperty(ref _selectedBoardingProgram, value))
            {
                return;
            }

            OnPropertyChanged(nameof(SelectedBoardingProgramTitle));
            OnPropertyChanged(nameof(SelectedBoardingProgramCredit));
            OnPropertyChanged(nameof(SelectedBoardingProgramFilePath));
            RefreshSelectedBoardingProgram();
        }
    }

    private void SelectPanel(object? parameter)
    {
        if (parameter is not string destination || string.IsNullOrWhiteSpace(destination) || destination == SelectedPanel)
        {
            return;
        }

        PreviousPanel = SelectedPanel;
        SelectedPanel = destination;
        LastAction = $"Opened {destination}";
    }

    private void NavigateToMainMenu()
    {
        if (SelectedPanel == "CSCP Main Menu")
        {
            return;
        }

        PreviousPanel = SelectedPanel;
        SelectedPanel = "CSCP Main Menu";
        LastAction = "Returned to main menu";
    }

    private void NavigateToPreviousMenu()
    {
        var destination = PreviousPanel;
        PreviousPanel = SelectedPanel;
        SelectedPanel = string.IsNullOrWhiteSpace(destination) ? "CSCP Main Menu" : destination;
        LastAction = $"Returned to {SelectedPanel}";
    }

    private void ExecuteAction(object? parameter)
    {
        if (parameter is not string action)
        {
            return;
        }

        switch (action)
        {
            case "Lighting:Cabin Lighting":
            case "Lighting:Entry Way Lights":
            case "Lighting:Reading Lights":
            case "Lighting:Galley Lights":
            case "Lighting:Lavatory Lights":
            case "Lighting:Work Lights":
                SelectedLightingArea = action["Lighting:".Length..];
                CabinLightingMode = SelectedLightingArea;
                LastAction = $"Selected {SelectedLightingArea}";
                break;
            case "Calls:Reset":
                ActiveCalls = 0;
                LastAction = "Passenger and lavatory calls reset";
                break;
            case "Chime:On":
                AttendantChimeEnabled = true;
                LastAction = "Attendant chime enabled";
                break;
            case "Chime:Off":
                AttendantChimeEnabled = false;
                LastAction = "Attendant chime disabled";
                break;
            case "Temp:Zone 1":
            case "Temp:Zone 2":
            case "Temp:Zone 3":
                SelectedTemperatureZone = action["Temp:".Length..];
                LastAction = $"Selected {SelectedTemperatureZone}";
                break;
            case "Temp:Increase":
                AdjustSelectedTemperature(1);
                break;
            case "Temp:Decrease":
                AdjustSelectedTemperature(-1);
                break;
            case "PA:VolumeUp":
                PaVolumeLevel = Math.Clamp(PaVolumeLevel + 1, 1, 10);
                LastAction = $"PA volume set to {PaVolumeLevel}";
                break;
            case "PA:VolumeDown":
                PaVolumeLevel = Math.Clamp(PaVolumeLevel - 1, 1, 10);
                LastAction = $"PA volume set to {PaVolumeLevel}";
                break;
            case "PA:SensorOn":
                AmbientNoiseSensorEnabled = true;
                LastAction = "Ambient noise sensor enabled";
                break;
            case "PA:SensorOff":
                AmbientNoiseSensorEnabled = false;
                LastAction = "Ambient noise sensor disabled";
                break;
            case "PA:AutomaticVolume":
                AutomaticPaVolumeEnabled = !AutomaticPaVolumeEnabled;
                LastAction = $"Automatic PA volume {(AutomaticPaVolumeEnabled ? "enabled" : "disabled")}";
                break;
            case "Display:BrightnessUp":
                DisplayBrightness = Math.Clamp(DisplayBrightness + 10, 0, 100);
                LastAction = $"Display brightness set to {DisplayBrightness}%";
                break;
            case "Display:BrightnessDown":
                DisplayBrightness = Math.Clamp(DisplayBrightness - 10, 0, 100);
                LastAction = $"Display brightness set to {DisplayBrightness}%";
                break;
            case "Display:Day":
            case "Display:Night":
                DisplayMode = action["Display:".Length..];
                DisplayPowerOn = true;
                LastAction = $"Display set to {DisplayMode.ToLowerInvariant()} mode";
                break;
            case "Display:Off":
                DisplayPowerOn = false;
                LastAction = "Display power command staged";
                break;
            case "Display:ScreenClean":
                LastAction = "Screen clean mode staged";
                break;
            case "Music:On":
                RefreshSelectedBoardingProgram();
                if (!HasSelectedBoardingMusic)
                {
                    IsBoardingMusicPlaying = false;
                    LastAction = $"Boarding music program {SelectedBoardingProgram} is not installed";
                    break;
                }

                BoardingMusicEnabled = true;
                IsBoardingMusicPlaying = true;
                BoardingMusicPreviewStatus = $"Playing Program {SelectedBoardingProgram}";
                LastAction = $"Boarding music program {SelectedBoardingProgram} started";
                break;
            case "Music:Off":
                IsBoardingMusicPlaying = false;
                BoardingMusicEnabled = false;
                BoardingMusicPreviewStatus = HasSelectedBoardingMusic
                    ? $"Program {SelectedBoardingProgram} ready"
                    : $"Program {SelectedBoardingProgram} recording is not installed";
                LastAction = "Boarding music stopped";
                break;
            case "Music:VolumeUp":
                BoardingMusicLevel = Math.Clamp(BoardingMusicLevel + 1, 1, 10);
                LastAction = $"Boarding music volume set to {BoardingMusicLevel}";
                break;
            case "Music:VolumeDown":
                BoardingMusicLevel = Math.Clamp(BoardingMusicLevel - 1, 1, 10);
                LastAction = $"Boarding music volume set to {BoardingMusicLevel}";
                break;
            case "Music:Program1":
            case "Music:Program2":
            case "Music:Program3":
            case "Music:Program4":
                SelectedBoardingProgram = action[^1] - '0';
                if (!HasSelectedBoardingMusic)
                {
                    IsBoardingMusicPlaying = false;
                }

                LastAction = HasSelectedBoardingMusic
                    ? $"Boarding music program {SelectedBoardingProgram} selected and ready"
                    : $"Boarding music program {SelectedBoardingProgram} selected; recording not installed";
                break;
            default:
                QueueEvent(action);
                break;
        }
    }

    private void AdjustSelectedTemperature(int delta)
    {
        var updated = Math.Clamp(SelectedTemperatureC + delta, 18, 30);
        switch (SelectedTemperatureZone)
        {
            case "Zone 2":
                Zone2TemperatureC = updated;
                break;
            case "Zone 3":
                Zone3TemperatureC = updated;
                break;
            default:
                Zone1TemperatureC = updated;
                break;
        }

        _settings.CabinTargetTemperatureC = updated;
        LastAction = $"{SelectedTemperatureZone} target set to {updated} °C";
        MarkChanged();
    }

    private void QueueEvent(object? parameter)
    {
        if (parameter is not string item || string.IsNullOrWhiteSpace(item))
        {
            return;
        }

        ActivityQueue.Insert(0, item);
        while (ActivityQueue.Count > 5)
        {
            ActivityQueue.RemoveAt(ActivityQueue.Count - 1);
        }

        QueueDepth++;
        LastAction = Status.IsConnected
            ? $"Queued for aircraft bridge: {item}"
            : $"Staged locally: {item}";
    }

    private void ClearQueue()
    {
        ActivityQueue.Clear();
        QueueDepth = 0;
        LastAction = "Media and command queue cleared";
    }

    private void StartSafetyVideo()
    {
        if (IsSafetyVideoInProgress)
        {
            return;
        }

        HasLocalSafetyVideo = File.Exists(SafetyVideoLocalFilePath);
        if (!HasLocalSafetyVideo)
        {
            SafetyVideoPreviewStatus = "Local BA_Safety_Video.mp4 is not installed";
            LastAction = "Safety video could not start because the local MP4 is missing";
            return;
        }

        QueueEvent("Safety demonstration video");
        SafetyVideoLocalSource = new Uri(SafetyVideoLocalFilePath, UriKind.Absolute);
        IsSafetyVideoInProgress = true;
        IsUsingLocalSafetyVideo = true;
        SafetyVideoPreviewStatus = "Announcement in progress — local British Airways MP4";
        LastAction = Status.IsConnected
            ? "Safety video started and queued for the aircraft bridge"
            : "Local safety video preview started; aircraft playback remains staged locally";
    }

    private void StopSafetyVideo()
    {
        if (!IsSafetyVideoInProgress)
        {
            return;
        }

        IsSafetyVideoInProgress = false;
        IsUsingLocalSafetyVideo = false;
        SafetyVideoLocalSource = null;
        SafetyVideoPreviewStatus = HasLocalSafetyVideo
            ? "Built-in British Airways safety video ready"
            : "Local BA_Safety_Video.mp4 is not installed";
        LastAction = "Stopped safety video test playback";
    }

    internal void ReportSafetyVideoPlaybackFailure(string? details)
    {
        IsSafetyVideoInProgress = false;
        IsUsingLocalSafetyVideo = false;
        SafetyVideoLocalSource = null;
        SafetyVideoPreviewStatus = "BA_Safety_Video.mp4 could not be played";
        LastAction = string.IsNullOrWhiteSpace(details)
            ? "Local safety video playback failed"
            : $"Local safety video playback failed: {details}";
    }

    internal void RefreshSafetyVideoAudioOutput() => OnPropertyChanged(nameof(SafetyVideoVolume));

    internal void ReportBoardingMusicPlaybackFailure(string? details)
    {
        IsBoardingMusicPlaying = false;
        BoardingMusicPreviewStatus = $"Program {SelectedBoardingProgram} could not be played";
        LastAction = string.IsNullOrWhiteSpace(details)
            ? "Local boarding music playback failed"
            : $"Local boarding music playback failed: {details}";
    }

    private void RefreshSelectedBoardingProgram()
    {
        var sourcePath = SelectedBoardingProgramFilePath;
        BoardingMusicLocalSource = File.Exists(sourcePath)
            ? new Uri(sourcePath, UriKind.Absolute)
            : null;
        OnPropertyChanged(nameof(HasSelectedBoardingMusic));
        BoardingMusicPreviewStatus = HasSelectedBoardingMusic
            ? $"Program {SelectedBoardingProgram} ready"
            : $"Program {SelectedBoardingProgram} recording is not installed";
    }

    private void SelectFirstInstalledBoardingProgram()
    {
        for (var program = 1; program <= 4; program++)
        {
            var candidate = Path.Combine(BoardingMusicDirectory, GetBoardingProgram(program).FileName);
            if (!File.Exists(candidate))
            {
                continue;
            }

            _selectedBoardingProgram = program;
            return;
        }
    }

    private static BoardingMusicProgram GetBoardingProgram(int program) => program switch
    {
        2 => new(
            "BRAHMS — SYMPHONY NO. 3, III. POCO ALLEGRETTO",
            "Musopen Symphony Orchestra — CC0 1.0",
            "BA_Boarding_Program_02_Brahms.mp3"),
        3 => new(
            "TCHAIKOVSKY — SERENADE FOR STRINGS, OP. 48: II. WALTZ",
            "Omega13a / MuseSounds rendition — CC BY 4.0",
            "BA_Boarding_Program_03_Tchaikovsky.mp3"),
        4 => new(
            "DELIBES — THE FLOWER DUET FROM LAKMÉ",
            "Philip Milman recording — CC BY 3.0",
            "BA_Boarding_Program_04_Flower_Duet.mp3"),
        _ => new(
            "DVOŘÁK — SERENADE FOR STRINGS, OP. 22: I. MODERATO",
            "Virtual Philharmonic Orchestra / Reinhold Behringer — CC BY-SA 4.0",
            "BA_Boarding_Program_01_Dvorak.mp3")
    };

    private sealed record BoardingMusicProgram(string Title, string Credit, string FileName);

    private void MarkChanged() => SaveStatus = "Unsaved changes";

    private async Task SaveAsync()
    {
        await _settingsStore.SaveAsync(_settings);
        SaveStatus = "Panel preferences saved";
    }

    private void ShowSaveError(Exception exception) =>
        SaveStatus = $"Could not save: {exception.Message}";
}
