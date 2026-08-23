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
        var (settings, settingsStore) = await LoadSettingsAsync(settingsDirectory);

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

    private async Task<(AppSettings Settings, ISettingsStore Store)> LoadSettingsAsync(string preferredDirectory)
    {
        var candidates = new[]
        {
            preferredDirectory,
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "FreeFlight",
                "CabinControl"),
            Path.Combine(Path.GetTempPath(), "FreeFlight", "CabinControl")
        }.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        ISettingsStore fallbackStore = new JsonSettingsStore(Path.Combine(candidates[^1], "settings.json"));
        foreach (var candidate in candidates)
        {
            if (!CanWriteToDirectory(candidate))
            {
                _logService?.Information($"Settings directory is not writable; trying fallback: {candidate}");
                continue;
            }

            var store = new JsonSettingsStore(Path.Combine(candidate, "settings.json"));
            fallbackStore = store;
            try
            {
                var settings = await store.LoadAsync();
                _logService?.Information($"Application settings loaded from {candidate}.");
                return (settings, store);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
            {
                _logService?.Error($"Application settings could not be loaded from {candidate}; trying a safe fallback.", exception);
            }
        }

        _logService?.Information("No writable settings file was available; defaults are in use for this session.");
        return (new AppSettings(), fallbackStore);
    }

    private static bool CanWriteToDirectory(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var probePath = Path.Combine(directory, $".write-test-{Guid.NewGuid():N}.tmp");
            using var stream = new FileStream(
                probePath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                1,
                FileOptions.DeleteOnClose);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                          NotSupportedException or System.Security.SecurityException)
        {
            return false;
        }
    }
}
