using Avalonia.Controls;
using Avalonia.Threading;
using FreeFlight.CabinControl.App.Services;
using FreeFlight.CabinControl.Core.Configuration;
using FreeFlight.CabinControl.Core.Integration;
using FreeFlight.CabinControl.Core.Persistence;

namespace FreeFlight.CabinControl.XPlane.App;

public partial class MainWindow : Window
{
    private readonly AppSettings _settings;
    private readonly ISettingsStore _settingsStore;
    private readonly FileLogService _log;
    private readonly XPlaneWebApiBridgeService _bridge;
    private readonly MsfsRelayBridgeService _msfsRelay;
    private readonly DispatcherTimer _statusTimer;
    private bool _usingMsfsRelay;
    private bool _disposed;

    public MainWindow()
    {
        InitializeComponent();

        var settingsDirectory = GetSettingsDirectory();
        _settingsStore = new JsonSettingsStore(Path.Combine(settingsDirectory, "settings.json"));
        _settings = LoadSettings();
        _settings.PreferredSimulator = "X-Plane";
        _log = new FileLogService(Path.Combine(settingsDirectory, "logs"));
        _log.Information("Ember X-Plane desktop client starting.");

        WebApiPortBox.Text = _settings.XPlaneWebApiPort.ToString(System.Globalization.CultureInfo.InvariantCulture);
        MsfsRelayUrlBox.Text = _settings.MsfsRelayUrl;
        MsfsRelayFingerprintBox.Text = _settings.MsfsRelayCertificateFingerprint;
        _bridge = new XPlaneWebApiBridgeService(_settings, _log);
        _bridge.StatusChanged += HandleBridgeStatus;
        _bridge.TelemetryReceived += HandleTelemetry;
        _bridge.Start();
        _msfsRelay = new MsfsRelayBridgeService();
        _msfsRelay.StatusChanged += HandleRelayStatus;
        _msfsRelay.TelemetryReceived += HandleTelemetry;

        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _statusTimer.Tick += (_, _) => RefreshDiagnostics();
        _statusTimer.Start();
        Closed += HandleClosed;
    }

    private AppSettings LoadSettings()
    {
        try
        {
            return _settingsStore.LoadAsync().GetAwaiter().GetResult();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            return new AppSettings();
        }
    }

    private static string GetSettingsDirectory()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "FreeFlight",
            "Ember");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private void HandleBridgeStatus(BridgeStatus status) => Dispatcher.UIThread.Post(() =>
    {
        if (_usingMsfsRelay)
        {
            return;
        }

        ShowConnectionStatus(status, "X-Plane");
        RefreshDiagnostics();
    });

    private void HandleRelayStatus(BridgeStatus status) => Dispatcher.UIThread.Post(() =>
    {
        if (!_usingMsfsRelay)
        {
            return;
        }

        ShowConnectionStatus(status, "MSFS relay");
        RefreshDiagnostics();
    });

    private void ShowConnectionStatus(BridgeStatus status, string connectionName)
    {
        ConnectionTitle.Text = status.Simulator;
        ConnectionDetail.Text = status.Detail;
        ConnectionBadge.Text = status.State switch
        {
            BridgeConnectionState.Connected => $"{connectionName} connected",
            BridgeConnectionState.Connecting => $"Connecting {connectionName}",
            BridgeConnectionState.Incompatible => $"{connectionName} needs attention",
            _ => $"{connectionName} offline"
        };
    }

    private void HandleTelemetry(CabinTelemetrySnapshot snapshot) => Dispatcher.UIThread.Post(() =>
    {
        TelemetrySummary.Text = $"{snapshot.FlightPhase} · {snapshot.AltitudeFeet:N0} ft · " +
                                (snapshot.OnGround ? "on the ground" : "airborne") +
                                (snapshot.SeatbeltSignOn ? " · seatbelts on" : " · seatbelts off");
        TelemetryAge.Text = $"Last frame: {snapshot.Timestamp.LocalDateTime:t}";
    });

    private void RefreshDiagnostics()
    {
        if (_usingMsfsRelay)
        {
            AircraftName.Text = _msfsRelay.CurrentStatus.Aircraft;
            ProfileTitle.Text = "MSFS private relay";
            ProfileSummary.Text = "Live aircraft telemetry is shared from your chosen Windows PC. Cabin controls remain local to MSFS Windows Ember in this first release.";
            if (_msfsRelay.LastFrameAge is { } relayAge)
            {
                TelemetryAge.Text = $"Latest relay frame: {Math.Max(0, relayAge.TotalSeconds):N1}s ago";
            }

            return;
        }

        var report = _bridge.GetSharedFlightProfileReport();
        AircraftName.Text = _bridge.CurrentStatus.Aircraft;
        ProfileTitle.Text = report.ProfileName;
        ProfileSummary.Text = report.Summary;
        if (_bridge.LastFrameAge is { } age)
        {
            TelemetryAge.Text = $"Latest frame: {Math.Max(0, age.TotalSeconds):N1}s ago";
        }
    }

    private async void ReconnectClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!int.TryParse(WebApiPortBox.Text, out var port) || port is < 1 or > 65535)
        {
            SettingsMessage.Text = "Enter a valid X-Plane Web API port (1–65535).";
            return;
        }

        _settings.XPlaneWebApiPort = port;
        try
        {
            await _settingsStore.SaveAsync(_settings);
            _usingMsfsRelay = false;
            SettingsMessage.Text = "Saved locally. Reconnecting to X-Plane…";
            _bridge.RequestReconnect();
            RefreshDiagnostics();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            SettingsMessage.Text = "The port was used for this session, but Ember could not save the local setting.";
        }
    }

    private async void ConnectMsfsRelayClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!Uri.TryCreate(MsfsRelayUrlBox.Text?.Trim(), UriKind.Absolute, out var relayUri))
        {
            MsfsRelayMessage.Text = "Enter the HTTPS address printed by the Windows relay.";
            return;
        }

        var fingerprint = MsfsRelayFingerprintBox.Text?.Trim() ?? string.Empty;
        var pairingCode = MsfsRelayCodeBox.Text?.Trim() ?? string.Empty;
        try
        {
            _usingMsfsRelay = true;
            MsfsRelayMessage.Text = "Pairing with the private Windows relay…";
            await _msfsRelay.ConnectAsync(relayUri, fingerprint, pairingCode, Environment.MachineName);
            MsfsRelayCodeBox.Text = string.Empty;
            _settings.MsfsRelayUrl = relayUri.GetLeftPart(UriPartial.Authority);
            _settings.MsfsRelayCertificateFingerprint = fingerprint;
            await _settingsStore.SaveAsync(_settings);
            MsfsRelayMessage.Text = "Paired for this Ember session. The one-time code and relay access token are not saved.";
            RefreshDiagnostics();
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or HttpRequestException or TaskCanceledException)
        {
            _usingMsfsRelay = false;
            MsfsRelayMessage.Text = exception switch
            {
                TaskCanceledException => "The relay did not respond in time. Check the address and local firewall, then try again.",
                _ => exception.Message
            };
            _bridge.RequestReconnect();
            RefreshDiagnostics();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            MsfsRelayMessage.Text = "Paired, but Ember could not save the relay address locally. You can still use it this session.";
        }
    }

    private async void OpenL1Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        await RunCabinCommandAsync("Open L1", () => _bridge.SetPassengerDoorOpenAsync(1, true));

    private async void CloseL1Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        await RunCabinCommandAsync("Close L1", () => _bridge.SetPassengerDoorOpenAsync(1, false));

    private async void OpenL2Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        await RunCabinCommandAsync("Open L2", () => _bridge.SetPassengerDoorOpenAsync(2, true));

    private async void CloseL2Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        await RunCabinCommandAsync("Close L2", () => _bridge.SetPassengerDoorOpenAsync(2, false));

    private async void SeatbeltOnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        await RunCabinCommandAsync("Seatbelts on", () => _bridge.SetSeatbeltSignAsync(true));

    private async void SeatbeltOffClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        await RunCabinCommandAsync("Seatbelts off", () => _bridge.SetSeatbeltSignAsync(false));

    private async void JetwayClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        await RunCabinCommandAsync("Jetway", () => _bridge.OperateJetwaysAsync());

    private async Task RunCabinCommandAsync(string action, Func<Task<bool>> command)
    {
        if (_usingMsfsRelay)
        {
            CommandResult.Text = "MSFS relay mode is read-only in this release. Use Windows Ember to send MSFS cabin controls.";
            return;
        }

        CommandResult.Text = $"Sending {action.ToLowerInvariant()} to X-Plane…";
        try
        {
            CommandResult.Text = await command()
                ? $"{action} was accepted by X-Plane."
                : $"{action} is unavailable for the current X-Plane connection or aircraft.";
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or TaskCanceledException)
        {
            CommandResult.Text = $"{action} could not be sent. Check the X-Plane connection and try again.";
        }
    }

    private void HandleClosed(object? sender, EventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _statusTimer.Stop();
        _bridge.StatusChanged -= HandleBridgeStatus;
        _bridge.TelemetryReceived -= HandleTelemetry;
        _bridge.Dispose();
        _msfsRelay.StatusChanged -= HandleRelayStatus;
        _msfsRelay.TelemetryReceived -= HandleTelemetry;
        _msfsRelay.Dispose();
        _log.Information("Ember X-Plane desktop client stopped.");
    }
}
