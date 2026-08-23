using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using FreeFlight.CabinControl.App.Infrastructure;
using FreeFlight.CabinControl.Core.Configuration;
using FreeFlight.CabinControl.Core.Persistence;

namespace FreeFlight.CabinControl.App.ViewModels;

public sealed class CabinControlPanelViewModel : PageViewModel
{
    private readonly AppSettings _settings;
    private readonly ISettingsStore _settingsStore;
    private string _selectedPanel = "Passenger Address";
    private string _lastAction = "Cabin panel ready in local preview mode";
    private string _saveStatus = "Panel preferences are stored locally";
    private int _queueDepth;

    public CabinControlPanelViewModel(
        AppSettings settings,
        ISettingsStore settingsStore,
        SharedStatusViewModel status)
        : base("Cabin Area Control Panel", "Boeing 777 cabin systems, media and service control")
    {
        _settings = settings;
        _settingsStore = settingsStore;
        Status = status;
        SelectPanelCommand = new RelayCommand(SelectPanel);
        QueueCommand = new RelayCommand(QueueEvent);
        ExecuteActionCommand = new RelayCommand(ExecuteAction);
        ClearQueueCommand = new RelayCommand(_ => ClearQueue());
        SaveCommand = new AsyncRelayCommand(SaveAsync, ShowSaveError);
    }

    public SharedStatusViewModel Status { get; }

    public ICommand SelectPanelCommand { get; }

    public ICommand QueueCommand { get; }

    public ICommand ExecuteActionCommand { get; }

    public ICommand ClearQueueCommand { get; }

    public ICommand SaveCommand { get; }

    public ObservableCollection<string> ActivityQueue { get; } = [];

    public IReadOnlyList<string> LightingModes { get; } = ["Boarding", "Bright", "Cruise", "Night", "Off"];

    public string SelectedPanel
    {
        get => _selectedPanel;
        private set => SetProperty(ref _selectedPanel, value);
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

    public string CabinLightingMode
    {
        get => _settings.CabinLightingMode;
        set
        {
            _settings.CabinLightingMode = value;
            OnPropertyChanged();
            MarkChanged();
        }
    }

    public double CabinTargetTemperatureC
    {
        get => _settings.CabinTargetTemperatureC;
        set
        {
            _settings.CabinTargetTemperatureC = Math.Round(value, 1);
            OnPropertyChanged();
            MarkChanged();
        }
    }

    public bool AutomaticAnnouncementsEnabled
    {
        get => _settings.AutomaticAnnouncementsEnabled;
        set
        {
            _settings.AutomaticAnnouncementsEnabled = value;
            OnPropertyChanged();
            MarkChanged();
        }
    }

    public bool SeatbackDisplaysEnabled
    {
        get => _settings.SeatbackDisplaysEnabled;
        set
        {
            _settings.SeatbackDisplaysEnabled = value;
            OnPropertyChanged();
            MarkChanged();
        }
    }

    public bool BoardingMusicEnabled
    {
        get => _settings.BoardingMusicEnabled;
        set
        {
            _settings.BoardingMusicEnabled = value;
            OnPropertyChanged();
            MarkChanged();
        }
    }

    public int BoardingMusicVolume
    {
        get => _settings.BoardingMusicVolume;
        set
        {
            _settings.BoardingMusicVolume = value;
            OnPropertyChanged();
            MarkChanged();
        }
    }

    private void SelectPanel(object? parameter)
    {
        if (parameter is string panel && !string.IsNullOrWhiteSpace(panel))
        {
            SelectedPanel = panel;
            LastAction = $"{panel} panel selected";
        }
    }

    private void QueueEvent(object? parameter)
    {
        if (parameter is not string eventName || string.IsNullOrWhiteSpace(eventName))
        {
            return;
        }

        ActivityQueue.Insert(0, eventName);
        while (ActivityQueue.Count > 5)
        {
            ActivityQueue.RemoveAt(ActivityQueue.Count - 1);
        }

        QueueDepth++;
        LastAction = Status.IsConnected
            ? $"{eventName} queued for the aircraft bridge"
            : $"{eventName} queued locally; waiting for the X-Plane bridge";
    }

    private void ExecuteAction(object? parameter)
    {
        if (parameter is not string action || string.IsNullOrWhiteSpace(action))
        {
            return;
        }

        switch (action)
        {
            case "Return displays to IFE":
                LastAction = "Seatback displays assigned to the normal IFE source";
                break;
            case "Rescan aircraft bridge":
                LastAction = "Bridge rescan requested; the X-Plane plugin is not connected yet";
                break;
            default:
                QueueEvent(action);
                break;
        }
    }

    private void ClearQueue()
    {
        ActivityQueue.Clear();
        QueueDepth = 0;
        LastAction = "Cabin event queue cleared";
    }

    private async Task SaveAsync()
    {
        await _settingsStore.SaveAsync(_settings);
        SaveStatus = $"Cabin panel preferences saved at {DateTime.Now:t}";
    }

    private void MarkChanged() => SaveStatus = "Unsaved cabin panel changes";

    private void ShowSaveError(Exception exception)
    {
        SaveStatus = "Cabin panel preferences could not be saved";
        MessageBox.Show(exception.Message, "Save failed", MessageBoxButton.OK, MessageBoxImage.Error);
    }
}
