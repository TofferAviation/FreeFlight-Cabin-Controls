using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using FreeFlight.CabinControl.App.Infrastructure;
using FreeFlight.CabinControl.App.Services;
using FreeFlight.CabinControl.Core.Configuration;
using FreeFlight.CabinControl.Core.Persistence;

namespace FreeFlight.CabinControl.App.ViewModels;

public sealed class AudioViewModel : PageViewModel, IDisposable
{
    private readonly AppSettings _settings;
    private readonly ISettingsStore _settingsStore;
    private readonly IAudioOutputDeviceService _audioOutputDeviceService;
    private readonly CabinControlPanelViewModel? _cabinPanel;
    private readonly DispatcherTimer _vuMeterTimer;
    private bool _isRefreshingOutputDevices;
    private AudioOutputDevice? _selectedOutputDevice;
    private string _outputDeviceStatus = "Detecting Windows playback devices...";
    private string _saveStatus = "Changes are stored locally";
    private double _leftMeterLevel;
    private double _rightMeterLevel;
    private double _meterPhase;

    public AudioViewModel(
        AppSettings settings,
        ISettingsStore settingsStore,
        SharedStatusViewModel status,
        IAudioOutputDeviceService? audioOutputDeviceService = null,
        CabinControlPanelViewModel? cabinPanel = null)
        : base("Audio Control", "Cabin soundscape and announcement management")
    {
        _settings = settings;
        _settingsStore = settingsStore;
        _audioOutputDeviceService = audioOutputDeviceService ?? new AudioOutputDeviceService();
        _cabinPanel = cabinPanel;
        Status = status;
        SaveCommand = new AsyncRelayCommand(SaveAsync, ShowSaveError);
        PreviewCommand = new RelayCommand(_ => ShowPreviewUnavailable());
        SafetyDemonstrationCommand = new RelayCommand(_ => ToggleSafetyDemonstration());
        BoardingMusicCommand = new RelayCommand(_ => ToggleBoardingMusic());
        NowPlayingCommand = new RelayCommand(_ => ToggleNowPlaying());
        RefreshOutputDevicesCommand = new RelayCommand(_ => RefreshOutputDevices());
        if (_cabinPanel is not null)
        {
            _cabinPanel.PropertyChanged += HandleCabinPanelPropertyChanged;
        }

        _vuMeterTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(80)
        };
        _vuMeterTimer.Tick += HandleVuMeterTick;
        _vuMeterTimer.Start();

        RefreshOutputDevices();
    }

    public SharedStatusViewModel Status { get; }

    public ICommand SaveCommand { get; }

    public ICommand PreviewCommand { get; }

    public ICommand SafetyDemonstrationCommand { get; }

    public ICommand BoardingMusicCommand { get; }

    public ICommand NowPlayingCommand { get; }

    public ICommand RefreshOutputDevicesCommand { get; }

    public ObservableCollection<AudioOutputDevice> OutputDevices { get; } = [];

    public IReadOnlyList<string> AudioProfiles { get; } =
        ["Balanced Cabin", "Quiet Cruise", "Immersive Cabin"];

    public AudioOutputDevice? SelectedOutputDevice
    {
        get => _selectedOutputDevice;
        set
        {
            if (!SetProperty(ref _selectedOutputDevice, value) || value is null)
            {
                return;
            }

            _settings.AudioOutputDeviceId = value.Id;
            _settings.AudioOutputDeviceName = value.Name;
            if (!_isRefreshingOutputDevices)
            {
                SaveStatus = "Output selection changed; save the audio profile to keep it";
            }
        }
    }

    public string OutputDeviceStatus
    {
        get => _outputDeviceStatus;
        private set => SetProperty(ref _outputDeviceStatus, value);
    }

    public string NowPlaying => IsSafetyDemonstrationInProgress
        ? _cabinPanel?.SafetyVideoTitle ?? "Safety demonstration"
        : IsBoardingMusicInProgress
            ? $"Boarding Music — Program {_cabinPanel?.SelectedBoardingProgram}"
            : "No audio playing";

    public string NowPlayingDescription
    {
        get
        {
            if (IsSafetyDemonstrationInProgress)
            {
                return SafetyDemonstrationEnabled
                    ? $"Local MP4 audio at {SafetyDemonstrationVolume}%"
                    : "Local MP4 continues with audio muted";
            }

            if (IsBoardingMusicInProgress)
            {
                return BoardingMusicEnabled
                    ? $"Program {_cabinPanel?.SelectedBoardingProgram} at {BoardingMusicVolume}% — use Cabin Panel for a specific program"
                    : $"Program {_cabinPanel?.SelectedBoardingProgram} continues with audio muted";
            }

            return _cabinPanel?.HasLocalSafetyVideo == true
                ? "Safety demonstration and four boarding programs ready"
                : "Four boarding programs ready; local BA safety demonstration is not installed";
        }
    }

    public bool IsSafetyDemonstrationInProgress => _cabinPanel?.IsSafetyVideoInProgress == true;

    public bool IsBoardingMusicInProgress => _cabinPanel?.IsBoardingMusicPlaying == true;

    public bool IsAnyAudioPlaying => IsSafetyDemonstrationInProgress || IsBoardingMusicInProgress;

    public string NowPlayingActionGlyph => IsAnyAudioPlaying ? "\uE71A" : "\uE768";

    public string NowPlayingActionLabel => IsSafetyDemonstrationInProgress
        ? "Stop safety demonstration"
        : IsBoardingMusicInProgress
            ? "Stop boarding music"
            : "Start safety demonstration";

    public string SafetyDemonstrationActionGlyph => IsSafetyDemonstrationInProgress ? "\uE71A" : "\uE768";

    public string SafetyDemonstrationActionLabel => IsSafetyDemonstrationInProgress
        ? "Stop safety demonstration"
        : "Start safety demonstration";

    public string BoardingMusicActionGlyph => IsBoardingMusicInProgress ? "\uE71A" : "\uE768";

    public string BoardingMusicActionLabel => IsBoardingMusicInProgress
        ? "Stop boarding music"
        : "Play a random boarding program";

    public string SaveStatus
    {
        get => _saveStatus;
        private set => SetProperty(ref _saveStatus, value);
    }

    public int MasterVolume
    {
        get => _settings.MasterVolume;
        set
        {
            var clamped = Math.Clamp(value, 0, 100);
            if (_settings.MasterVolume == clamped)
            {
                return;
            }

            _settings.MasterVolume = clamped;
            OnPropertyChanged();
            _cabinPanel?.RefreshMasterAudioOutput();
            if (clamped == 0)
            {
                LeftMeterLevel = 0d;
                RightMeterLevel = 0d;
            }
        }
    }

    public double LeftMeterLevel
    {
        get => _leftMeterLevel;
        private set => SetProperty(ref _leftMeterLevel, Math.Clamp(value, 0d, 100d));
    }

    public double RightMeterLevel
    {
        get => _rightMeterLevel;
        private set => SetProperty(ref _rightMeterLevel, Math.Clamp(value, 0d, 100d));
    }

    public bool PassengerAmbienceEnabled
    {
        get => _settings.PassengerAmbienceEnabled;
        set
        {
            _settings.PassengerAmbienceEnabled = value;
            OnPropertyChanged();
        }
    }

    public int PassengerAmbienceVolume
    {
        get => _settings.PassengerAmbienceVolume;
        set
        {
            _settings.PassengerAmbienceVolume = value;
            OnPropertyChanged();
        }
    }

    public bool AutomaticAnnouncementsEnabled
    {
        get => _settings.AutomaticAnnouncementsEnabled;
        set
        {
            _settings.AutomaticAnnouncementsEnabled = value;
            OnPropertyChanged();
        }
    }

    public int CrewAnnouncementsVolume
    {
        get => _settings.CrewAnnouncementsVolume;
        set
        {
            _settings.CrewAnnouncementsVolume = value;
            OnPropertyChanged();
        }
    }

    public int CabinSoundsVolume
    {
        get => _settings.CabinSoundsVolume;
        set
        {
            _settings.CabinSoundsVolume = value;
            OnPropertyChanged();
        }
    }

    public bool CabinSoundsEnabled
    {
        get => _settings.CabinSoundsEnabled;
        set
        {
            _settings.CabinSoundsEnabled = value;
            OnPropertyChanged();
        }
    }

    public bool BoardingMusicEnabled
    {
        get => _cabinPanel?.BoardingMusicEnabled ?? _settings.BoardingMusicEnabled;
        set
        {
            if (_cabinPanel is not null)
            {
                _cabinPanel.SetBoardingMusicEnabled(value);
            }
            else
            {
                _settings.BoardingMusicEnabled = value;
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(NowPlayingDescription));
        }
    }

    public int BoardingMusicVolume
    {
        get => _settings.BoardingMusicVolume;
        set
        {
            if (_cabinPanel is not null)
            {
                _cabinPanel.SetBoardingMusicVolume(value);
            }
            else
            {
                _settings.BoardingMusicVolume = Math.Clamp(value, 0, 100);
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(NowPlayingDescription));
        }
    }

    public int SafetyDemonstrationVolume
    {
        get => _settings.SafetyDemonstrationVolume;
        set
        {
            _settings.SafetyDemonstrationVolume = Math.Clamp(value, 0, 100);
            OnPropertyChanged();
            OnPropertyChanged(nameof(NowPlayingDescription));
            _cabinPanel?.RefreshSafetyVideoAudioOutput();
        }
    }

    public bool SafetyDemonstrationEnabled
    {
        get => _settings.SafetyDemonstrationEnabled;
        set
        {
            _settings.SafetyDemonstrationEnabled = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(NowPlayingDescription));
            _cabinPanel?.RefreshSafetyVideoAudioOutput();
        }
    }

    public bool AircraftEventsEnabled
    {
        get => _settings.AircraftEventsEnabled;
        set
        {
            _settings.AircraftEventsEnabled = value;
            OnPropertyChanged();
        }
    }

    public int AircraftEventsVolume
    {
        get => _settings.AircraftEventsVolume;
        set
        {
            _settings.AircraftEventsVolume = value;
            OnPropertyChanged();
        }
    }

    public string AudioProfile
    {
        get => _settings.AudioProfile;
        set
        {
            _settings.AudioProfile = value;
            OnPropertyChanged();
        }
    }

    public void Dispose()
    {
        _vuMeterTimer.Stop();
        _vuMeterTimer.Tick -= HandleVuMeterTick;
        if (_cabinPanel is not null)
        {
            _cabinPanel.PropertyChanged -= HandleCabinPanelPropertyChanged;
        }

        GC.SuppressFinalize(this);
    }

    public void AdvanceVuMeters()
    {
        var effectiveOutput = IsSafetyDemonstrationInProgress
            ? _cabinPanel?.SafetyVideoVolume ?? 0d
            : IsBoardingMusicInProgress
                ? _cabinPanel?.BoardingMusicOutputVolume ?? 0d
                : 0d;

        if (!IsAnyAudioPlaying || effectiveOutput <= 0d)
        {
            LeftMeterLevel = Math.Max(0d, LeftMeterLevel - 12d);
            RightMeterLevel = Math.Max(0d, RightMeterLevel - 14d);
            return;
        }

        _meterPhase += 0.61d;
        var leftEnvelope = 0.62d + (0.25d * Math.Abs(Math.Sin(_meterPhase))) +
                           (0.10d * Math.Abs(Math.Sin(_meterPhase * 2.31d)));
        var rightEnvelope = 0.58d + (0.27d * Math.Abs(Math.Sin(_meterPhase + 0.83d))) +
                            (0.11d * Math.Abs(Math.Sin((_meterPhase * 1.87d) + 0.25d)));
        var leftTarget = effectiveOutput * 100d * leftEnvelope;
        var rightTarget = effectiveOutput * 100d * rightEnvelope;
        LeftMeterLevel += (leftTarget - LeftMeterLevel) * 0.68d;
        RightMeterLevel += (rightTarget - RightMeterLevel) * 0.68d;
    }

    private void ToggleSafetyDemonstration()
    {
        if (_cabinPanel is null)
        {
            ShowPreviewUnavailable();
            return;
        }

        if (_cabinPanel.IsSafetyVideoInProgress)
        {
            _cabinPanel.StopSafetyVideoCommand.Execute(null);
        }
        else
        {
            if (_cabinPanel.IsBoardingMusicPlaying)
            {
                _cabinPanel.StopBoardingMusic();
            }

            _cabinPanel.StartSafetyVideoCommand.Execute(null);
        }
    }

    private void HandleVuMeterTick(object? sender, EventArgs e) => AdvanceVuMeters();

    private void ToggleBoardingMusic()
    {
        if (_cabinPanel is null)
        {
            ShowPreviewUnavailable();
            return;
        }

        if (!_cabinPanel.IsBoardingMusicPlaying && _cabinPanel.IsSafetyVideoInProgress)
        {
            _cabinPanel.StopSafetyVideoCommand.Execute(null);
        }

        _cabinPanel.ToggleRandomBoardingMusic();
    }

    private void ToggleNowPlaying()
    {
        if (IsSafetyDemonstrationInProgress)
        {
            ToggleSafetyDemonstration();
        }
        else if (IsBoardingMusicInProgress)
        {
            ToggleBoardingMusic();
        }
        else
        {
            ToggleSafetyDemonstration();
        }
    }

    private void HandleCabinPanelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        var safetyChanged = e.PropertyName is nameof(CabinControlPanelViewModel.IsSafetyVideoInProgress) or
            nameof(CabinControlPanelViewModel.SafetyVideoPreviewStatus) or
            nameof(CabinControlPanelViewModel.HasLocalSafetyVideo);
        var boardingMusicChanged = e.PropertyName is nameof(CabinControlPanelViewModel.IsBoardingMusicPlaying) or
            nameof(CabinControlPanelViewModel.SelectedBoardingProgram) or
            nameof(CabinControlPanelViewModel.BoardingMusicEnabled) or
            nameof(CabinControlPanelViewModel.BoardingMusicLevel) or
            nameof(CabinControlPanelViewModel.BoardingMusicOutputVolume) or
            nameof(CabinControlPanelViewModel.BoardingMusicPreviewStatus);
        if (!safetyChanged && !boardingMusicChanged)
        {
            return;
        }

        if (safetyChanged)
        {
            OnPropertyChanged(nameof(IsSafetyDemonstrationInProgress));
            OnPropertyChanged(nameof(SafetyDemonstrationActionGlyph));
            OnPropertyChanged(nameof(SafetyDemonstrationActionLabel));
        }

        if (boardingMusicChanged)
        {
            OnPropertyChanged(nameof(IsBoardingMusicInProgress));
            OnPropertyChanged(nameof(BoardingMusicEnabled));
            OnPropertyChanged(nameof(BoardingMusicVolume));
            OnPropertyChanged(nameof(BoardingMusicActionGlyph));
            OnPropertyChanged(nameof(BoardingMusicActionLabel));
        }

        OnPropertyChanged(nameof(NowPlaying));
        OnPropertyChanged(nameof(NowPlayingDescription));
        OnPropertyChanged(nameof(IsAnyAudioPlaying));
        OnPropertyChanged(nameof(NowPlayingActionGlyph));
        OnPropertyChanged(nameof(NowPlayingActionLabel));
    }

    private async Task SaveAsync()
    {
        await _settingsStore.SaveAsync(_settings);
        SaveStatus = $"Audio profile saved at {DateTime.Now:t}";
    }

    private void RefreshOutputDevices()
    {
        _isRefreshingOutputDevices = true;
        OutputDevices.Clear();
        var systemDefault = new AudioOutputDevice(string.Empty, "System default (follows Windows)", false);
        OutputDevices.Add(systemDefault);

        try
        {
            var devices = _audioOutputDeviceService.GetActiveOutputDevices();
            foreach (var device in devices)
            {
                OutputDevices.Add(device);
            }

            SelectedOutputDevice = OutputDevices.FirstOrDefault(device =>
                string.Equals(device.Id, _settings.AudioOutputDeviceId, StringComparison.OrdinalIgnoreCase))
                ?? systemDefault;
            OutputDeviceStatus = devices.Count == 0
                ? "No fixed endpoints found; following the Windows default"
                : $"{devices.Count} active Windows playback device{(devices.Count == 1 ? string.Empty : "s")}";
        }
        catch (Exception exception) when (exception is COMException or InvalidCastException)
        {
            SelectedOutputDevice = systemDefault;
            OutputDeviceStatus = "Following the Windows default; fixed endpoints unavailable";
        }
        finally
        {
            _isRefreshingOutputDevices = false;
        }
    }

    private static void ShowPreviewUnavailable() => MessageBox.Show(
        "Audio preview will become available when a licensed content pack or user-owned test file is installed.",
        "No preview media installed",
        MessageBoxButton.OK,
        MessageBoxImage.Information);

    private void ShowSaveError(Exception exception)
    {
        SaveStatus = "Could not save audio profile";
        MessageBox.Show(exception.Message, "Save failed", MessageBoxButton.OK, MessageBoxImage.Error);
    }
}
