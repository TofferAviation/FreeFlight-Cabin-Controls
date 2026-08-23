using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using FreeFlight.CabinControl.App.Infrastructure;
using FreeFlight.CabinControl.App.Services;
using FreeFlight.CabinControl.Core.Configuration;
using FreeFlight.CabinControl.Core.Persistence;

namespace FreeFlight.CabinControl.App.ViewModels;

public sealed class AudioViewModel : PageViewModel
{
    private readonly AppSettings _settings;
    private readonly ISettingsStore _settingsStore;
    private readonly IAudioOutputDeviceService _audioOutputDeviceService;
    private bool _isRefreshingOutputDevices;
    private AudioOutputDevice? _selectedOutputDevice;
    private string _outputDeviceStatus = "Detecting Windows playback devices...";
    private string _saveStatus = "Changes are stored locally";

    public AudioViewModel(
        AppSettings settings,
        ISettingsStore settingsStore,
        SharedStatusViewModel status,
        IAudioOutputDeviceService? audioOutputDeviceService = null)
        : base("Audio Control", "Cabin soundscape and announcement management")
    {
        _settings = settings;
        _settingsStore = settingsStore;
        _audioOutputDeviceService = audioOutputDeviceService ?? new AudioOutputDeviceService();
        Status = status;
        SaveCommand = new AsyncRelayCommand(SaveAsync, ShowSaveError);
        PreviewCommand = new RelayCommand(_ => ShowPreviewUnavailable());
        RefreshOutputDevicesCommand = new RelayCommand(_ => RefreshOutputDevices());
        RefreshOutputDevices();
    }

    public SharedStatusViewModel Status { get; }

    public ICommand SaveCommand { get; }

    public ICommand PreviewCommand { get; }

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

    public string NowPlaying => "No audio playing";

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
            _settings.MasterVolume = value;
            OnPropertyChanged();
        }
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
        get => _settings.BoardingMusicEnabled;
        set
        {
            _settings.BoardingMusicEnabled = value;
            OnPropertyChanged();
        }
    }

    public int BoardingMusicVolume
    {
        get => _settings.BoardingMusicVolume;
        set
        {
            _settings.BoardingMusicVolume = value;
            OnPropertyChanged();
        }
    }

    public int SafetyDemonstrationVolume
    {
        get => _settings.SafetyDemonstrationVolume;
        set
        {
            _settings.SafetyDemonstrationVolume = value;
            OnPropertyChanged();
        }
    }

    public bool SafetyDemonstrationEnabled
    {
        get => _settings.SafetyDemonstrationEnabled;
        set
        {
            _settings.SafetyDemonstrationEnabled = value;
            OnPropertyChanged();
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
