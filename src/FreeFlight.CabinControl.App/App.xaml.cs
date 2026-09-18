using System.IO;
using System.Windows;
using System.Windows.Threading;
using FreeFlight.CabinControl.App.Services;
using FreeFlight.CabinControl.App.ViewModels;
using FreeFlight.CabinControl.Core.Configuration;
using FreeFlight.CabinControl.Core.Diagnostics;
using FreeFlight.CabinControl.Core.Persistence;

namespace FreeFlight.CabinControl.App;

public partial class App
{
    private FileLogService? _logService;
    private CrashReportWriter? _crashReportWriter;

    protected override async void OnStartup(StartupEventArgs e)
    {
        var settingsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FreeFlight",
            "CabinControl");
        _crashReportWriter = new CrashReportWriter(
            Path.Combine(settingsDirectory, "crash-reports"),
            "Ember ACARS Systems",
            typeof(App).Assembly.GetName().Version?.ToString(3) ?? "unknown");

        DispatcherUnhandledException += HandleDispatcherException;
        AppDomain.CurrentDomain.UnhandledException += HandleAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += HandleUnobservedTaskException;

        base.OnStartup(e);

        _logService = new FileLogService(Path.Combine(settingsDirectory, "logs"));
        _logService.Information("Ember starting.");
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
                    "Your vAMSYS account was connected. Return to the open Ember window.",
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
        _logService?.Information("Ember stopped.");
        DispatcherUnhandledException -= HandleDispatcherException;
        AppDomain.CurrentDomain.UnhandledException -= HandleAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException -= HandleUnobservedTaskException;
        base.OnExit(e);
    }

    private void HandleDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        var reportPath = WriteCrashReport("Unhandled user-interface exception", e.Exception);
        MessageBox.Show(
            $"Ember encountered an unexpected error.\n\n{ReportLocationMessage(reportPath)}",
            "Ember",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }

    private void HandleAppDomainUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        var exception = e.ExceptionObject as Exception ??
                        new InvalidOperationException("The process ended because of an unknown unhandled exception.");
        WriteCrashReport("Unhandled process exception", exception);
    }

    private void HandleUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        WriteCrashReport("Unobserved background task exception", e.Exception);
        e.SetObserved();
    }

    private string? WriteCrashReport(string source, Exception exception)
    {
        var reportPath = _crashReportWriter?.TryWrite(source, exception);
        if (reportPath is not null)
        {
            _logService?.Information($"A crash report was saved to {reportPath}.");
        }

        return reportPath;
    }

    private string ReportLocationMessage(string? reportPath) => reportPath is null
        ? "Ember could not save a crash report on this computer."
        : $"A diagnostic report was saved locally:\n{reportPath}\n\nAttach it to a support ticket if you need help.";

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
