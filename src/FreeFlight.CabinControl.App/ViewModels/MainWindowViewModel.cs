using System.Windows.Input;
using FreeFlight.CabinControl.App.Infrastructure;
using FreeFlight.CabinControl.App.Services;
using FreeFlight.CabinControl.Core.Configuration;
using FreeFlight.CabinControl.Core.Operations;
using FreeFlight.CabinControl.Core.Persistence;

namespace FreeFlight.CabinControl.App.ViewModels;

public sealed class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly AppSettings _settings;
    private PageViewModel _currentPage;
    private string _activePage = "Dashboard";

    public MainWindowViewModel(
        AppSettings settings,
        ISettingsStore settingsStore,
        string logDirectory,
        string? safetyVideoLocalFilePath = null,
        string? boardingMusicDirectory = null,
        ISimBriefClient? simBriefClient = null,
        IOperationsClock? operationsClock = null,
        IBoardingPassPrinterService? boardingPassPrinterService = null)
    {
        _settings = settings;
        var resolvedOperationsClock = operationsClock ?? new LocalOperationsClock();
        Status = new SharedStatusViewModel();
        GateLogin = new GateLoginViewModel(settings, resolvedOperationsClock);
        Airliners = new AirlinersViewModel(settings, settingsStore, Status);
        Passengers = new PassengerFlowViewModel(settings, Status, settingsStore, simBriefClient, resolvedOperationsClock);
        Operations = new GateOperationsViewModel(
            settings,
            Passengers,
            resolvedOperationsClock,
            () => GateLogin.IsAuthenticated,
            boardingPassPrinterService);
        IportDcs = new IportDcsViewModel(Operations, GateLogin);
        Dashboard = Operations;
        CabinPanel = new CabinControlPanelViewModel(
            settings,
            settingsStore,
            Status,
            safetyVideoLocalFilePath,
            boardingMusicDirectory);
        Audio = new AudioViewModel(settings, settingsStore, Status, cabinPanel: CabinPanel);
        Performance = new PerformanceViewModel(settings, Status, logDirectory);
        Settings = new SettingsViewModel(settings, settingsStore, Status);
        _currentPage = Dashboard;
        NavigateCommand = new RelayCommand(Navigate);
        GateLogin.SignedIn += HandleGateSignedIn;
        GateLogin.SignedOut += HandleGateSignedOut;
    }

    public SharedStatusViewModel Status { get; }

    public GateLoginViewModel GateLogin { get; }

    public GateOperationsViewModel Operations { get; }

    public IportDcsViewModel IportDcs { get; }

    public GateOperationsViewModel Dashboard { get; }

    public AirlinersViewModel Airliners { get; }

    public PassengerFlowViewModel Passengers { get; }

    public CabinControlPanelViewModel CabinPanel { get; }

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
        GateLogin.SignedIn -= HandleGateSignedIn;
        GateLogin.SignedOut -= HandleGateSignedOut;
        GateLogin.Dispose();
        Audio.Dispose();
        IportDcs.Dispose();
        Operations.Dispose();
        Passengers.Dispose();
        Performance.Dispose();
        GC.SuppressFinalize(this);
    }

    private void Navigate(object? parameter)
    {
        if (parameter is not string destination)
        {
            return;
        }

        if (IsGateWorkspacePage(destination) && !GateLogin.IsAuthenticated)
        {
            CurrentPage = GateLogin;
            ActivePage = "GateLogin";
            return;
        }

        if (destination == "Passengers")
        {
            Passengers.ApplyCabinLayoutSelection(_settings.PassengerCabinLayoutId);
        }
        else if (destination == "Settings")
        {
            Settings.ApplyCabinLayoutSelection(_settings.PassengerCabinLayoutId);
        }

        Operations.ApplySettings();

        CurrentPage = destination switch
        {
            "GateLogin" => GateLogin,
            "GateDesk" => Operations,
            "PassengerManifest" => Operations,
            "BoardingPasses" => Operations,
            "IportDcs" => IportDcs,
            "Airliners" => Airliners,
            "Passengers" => Passengers,
            "CabinPanel" => CabinPanel,
            "Audio" => Audio,
            "Performance" => Performance,
            "Settings" => Settings,
            _ => Dashboard
        };
        ActivePage = destination;
    }

    private void HandleGateSignedIn(object? sender, EventArgs e)
    {
        Operations.ApplyGateAccessState();
        Navigate("GateDesk");
    }

    private void HandleGateSignedOut(object? sender, EventArgs e)
    {
        Operations.ApplyGateAccessState();
        CurrentPage = GateLogin;
        ActivePage = "GateLogin";
    }

    private static bool IsGateWorkspacePage(string destination) => destination is
        "GateDesk" or "PassengerManifest" or "BoardingPasses" or "IportDcs" or "Passengers";
}
