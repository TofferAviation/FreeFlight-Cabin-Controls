using System.Windows.Input;
using FreeFlight.CabinControl.App.Infrastructure;
using FreeFlight.CabinControl.Core.Configuration;
using FreeFlight.CabinControl.Core.Persistence;

namespace FreeFlight.CabinControl.App.ViewModels;

public sealed class MainWindowViewModel : ObservableObject, IDisposable
{
    private PageViewModel _currentPage;
    private string _activePage = "Dashboard";

    public MainWindowViewModel(AppSettings settings, ISettingsStore settingsStore, string logDirectory)
    {
        Status = new SharedStatusViewModel();
        Dashboard = new DashboardViewModel(settings, Status);
        Audio = new AudioViewModel(settings, settingsStore, Status);
        Performance = new PerformanceViewModel(settings, Status, logDirectory);
        Settings = new SettingsViewModel(settings, settingsStore, Status);
        _currentPage = Dashboard;
        NavigateCommand = new RelayCommand(Navigate);
    }

    public SharedStatusViewModel Status { get; }

    public DashboardViewModel Dashboard { get; }

    public AudioViewModel Audio { get; }

    public PerformanceViewModel Performance { get; }

    public SettingsViewModel Settings { get; }

    public ICommand NavigateCommand { get; }

    public string ActivePage
    {
        get => _activePage;
        private set => SetProperty(ref _activePage, value);
    }

    public PageViewModel CurrentPage
    {
        get => _currentPage;
        private set => SetProperty(ref _currentPage, value);
    }

    public void Dispose()
    {
        Performance.Dispose();
        GC.SuppressFinalize(this);
    }

    private void Navigate(object? parameter)
    {
        if (parameter is not string destination)
        {
            return;
        }

        CurrentPage = destination switch
        {
            "Audio" => Audio,
            "Performance" => Performance,
            "Settings" => Settings,
            _ => Dashboard
        };
        ActivePage = destination;
    }
}
