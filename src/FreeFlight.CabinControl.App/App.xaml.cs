using System.IO;
using System.Windows;
using System.Windows.Threading;
using FreeFlight.CabinControl.App.Services;
using FreeFlight.CabinControl.App.ViewModels;
using FreeFlight.CabinControl.Core.Configuration;
using FreeFlight.CabinControl.Core.Persistence;

namespace FreeFlight.CabinControl.App;

public partial class App
{
    private FileLogService? _logService;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += HandleDispatcherException;

        var settingsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FreeFlight",
            "CabinControl");
        _logService = new FileLogService(Path.Combine(settingsDirectory, "logs"));
        _logService.Information("FreeFlight Cabin Control starting.");
        var settingsStore = new JsonSettingsStore(Path.Combine(settingsDirectory, "settings.json"));

        AppSettings settings;
        try
        {
            settings = await settingsStore.LoadAsync();
            _logService.Information("Application settings loaded.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            _logService.Error("Application settings could not be loaded; defaults are in use.", exception);
            settings = new AppSettings();
            MessageBox.Show(
                "Your saved settings could not be loaded. FreeFlight will use safe defaults for this session. The original file has not been deleted.",
                "Settings could not be loaded",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        var viewModel = new MainWindowViewModel(settings, settingsStore, _logService.LogDirectory);
        MainWindow = new MainWindow
        {
            DataContext = viewModel
        };
        MainWindow.Show();
        _logService.Information("Main window opened successfully.");
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _logService?.Information("FreeFlight Cabin Control stopped.");
        base.OnExit(e);
    }

    private void HandleDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _logService?.Error("Unhandled user-interface exception.", e.Exception);
        MessageBox.Show(
            $"FreeFlight Cabin Control encountered an unexpected error.\n\n{e.Exception.Message}",
            "FreeFlight Cabin Control",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }
}
