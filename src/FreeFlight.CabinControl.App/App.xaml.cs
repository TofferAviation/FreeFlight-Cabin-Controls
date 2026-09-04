using System.IO;
using System.Reflection;
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
    private string? _lastUiErrorSignature;
    private DateTimeOffset _lastUiErrorShownAt = DateTimeOffset.MinValue;
    private int _suppressedDuplicateUiErrors;
    private bool _errorDialogOpen;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += HandleDispatcherException;

        var settingsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FreeFlight",
            "CabinControl");
        _logService = new FileLogService(Path.Combine(settingsDirectory, "logs"));
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";
        _logService.Information($"FreeFlight Cabin Control starting · assembly {version}.");
        var (settings, settingsStore, activeSettingsDirectory) = await LoadSettingsAsync(settingsDirectory);

        var vamsysService = new VamsysOAuthService(settings, activeSettingsDirectory);
        var oauthCallback = e.Args.FirstOrDefault(argument =>
            argument.StartsWith("freeflight-cabin-control://", StringComparison.OrdinalIgnoreCase));
        if (oauthCallback is not null)
        {
            try
            {
                await vamsysService.HandleAuthorizationCallbackAsync(oauthCallback);
                MessageBox.Show(
                    "Your vAMSYS account was connected. Return to the open FreeFlight window.",
                    "vAMSYS connected",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception exception)
            {
                _logService.Error("vAMSYS authorization callback failed.", exception);
                MessageBox.Show(
                    exception.Message,
                    "vAMSYS connection failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }

            Shutdown();
            return;
        }

        var simulatorBridge = new AutomaticSimulatorBridgeService(settings, _logService);
        var flightSessionStore = new FlightSessionStore(
            Path.Combine(settingsDirectory, "active-flight.json"),
            _logService);
        var updateService = new UpdateService(settingsDirectory);
        var viewModel = new MainWindowViewModel(
            settings,
            settingsStore,
            _logService.LogDirectory,
            simulatorBridge: simulatorBridge,
            flightSessionStore: flightSessionStore,
            updateService: updateService,
            vamsysService: vamsysService,
            settingsDirectory: activeSettingsDirectory);
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
        e.Handled = true;

        var now = DateTimeOffset.UtcNow;
        var signature = $"{e.Exception.GetType().FullName}|{e.Exception.Message}";
        var isDuplicate = string.Equals(signature, _lastUiErrorSignature, StringComparison.Ordinal) &&
                          now - _lastUiErrorShownAt < TimeSpan.FromSeconds(5);

        if (isDuplicate)
        {
            _suppressedDuplicateUiErrors++;
            return;
        }

        if (_suppressedDuplicateUiErrors > 0)
        {
            _logService?.Information($"Suppressed {_suppressedDuplicateUiErrors} duplicate user-interface exceptions during the previous error window.");
            _suppressedDuplicateUiErrors = 0;
        }

        _lastUiErrorSignature = signature;
        _lastUiErrorShownAt = now;
        _logService?.Error("Unhandled user-interface exception.", e.Exception);

        if (_errorDialogOpen)
        {
            return;
        }

        _errorDialogOpen = true;
        try
        {
            var message = $"FreeFlight Cabin Control encountered an unexpected error.\n\n{e.Exception.Message}";
            if (MainWindow is Window owner && owner.IsVisible)
            {
                MessageBox.Show(
                    owner,
                    message,
                    "FreeFlight Cabin Control",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            else
            {
                MessageBox.Show(
                    message,
                    "FreeFlight Cabin Control",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
        finally
        {
            _errorDialogOpen = false;
        }
    }

    private async Task<(AppSettings Settings, ISettingsStore Store, string Directory)> LoadSettingsAsync(string preferredDirectory)
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
                return (settings, store, candidate);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
            {
                _logService?.Error($"Application settings could not be loaded from {candidate}; trying a safe fallback.", exception);
            }
        }

        _logService?.Information("No writable settings file was available; defaults are in use for this session.");
        return (new AppSettings(), fallbackStore, candidates[^1]);
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
