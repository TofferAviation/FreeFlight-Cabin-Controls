using System.ComponentModel;
using System.Runtime.InteropServices;
using FreeFlight.CabinControl.Core.Configuration;
using FreeFlight.CabinControl.Core.Integration;

namespace FreeFlight.CabinControl.App.Services;

public sealed class Msfs2024SimConnectBridgeService : ISimulatorBridge
{
    private const uint DefinitionId = 1;
    private const uint RequestId = 1;
    private const uint UserObjectId = 0;
    private readonly AppSettings _settings;
    private readonly FileLogService _log;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _reconnectSignal = new(0, 1);
    private Task? _runTask;
    private IntPtr _connection;
    private BridgeStatus _currentStatus = new(
        BridgeConnectionState.Disconnected,
        "MSFS 2024 not connected",
        "No aircraft detected",
        "Waiting for the local SimConnect runtime.");
    private long _lastFrameUtcTicks;
    private bool _disposed;

    public Msfs2024SimConnectBridgeService(AppSettings settings, FileLogService log)
    {
        _settings = settings;
        _log = log;
    }

    public BridgeStatus CurrentStatus => _currentStatus;

    public TimeSpan? LastFrameAge
    {
        get
        {
            var ticks = Interlocked.Read(ref _lastFrameUtcTicks);
            return ticks == 0 ? null : DateTimeOffset.UtcNow - new DateTimeOffset(ticks, TimeSpan.Zero);
        }
    }

    public event Action<BridgeStatus>? StatusChanged;
    public event Action<CabinTelemetrySnapshot>? TelemetryReceived;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _runTask ??= Task.Run(() => RunAsync(_lifetime.Token));
    }

    public void RequestReconnect()
    {
        CloseConnection();
        if (_reconnectSignal.CurrentCount == 0)
        {
            try { _reconnectSignal.Release(); } catch (SemaphoreFullException) { }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        CloseConnection();
        _reconnectSignal.Dispose();
        _lifetime.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            if (!_settings.Msfs2024AutoConnect)
            {
                PublishStatus(new BridgeStatus(
                    BridgeConnectionState.Disconnected,
                    "MSFS 2024 auto-connect disabled",
                    "Manual cabin controls available",
                    "Enable MSFS 2024 auto-connect in Settings."));
                await WaitForRetryAsync(TimeSpan.FromSeconds(3), cancellationToken).ConfigureAwait(false);
                continue;
            }

            try
            {
                PublishStatus(new BridgeStatus(
                    BridgeConnectionState.Connecting,
                    "Looking for MSFS 2024",
                    "Detecting aircraft",
                    "Opening a local out-of-process SimConnect connection."));
                OpenConnection();
                PublishStatus(new BridgeStatus(
                    BridgeConnectionState.Connected,
                    "Microsoft Flight Simulator 2024",
                    "User aircraft telemetry detected",
                    "SimConnect · doors, seat-belt sign and flight state live"));

                var callback = new DispatchProc(HandleDispatch);
                while (!cancellationToken.IsCancellationRequested && _connection != IntPtr.Zero)
                {
                    var result = SimConnectCallDispatch(_connection, callback, IntPtr.Zero);
                    if (result < 0)
                    {
                        throw new Win32Exception(result, "SimConnect stopped dispatching telemetry.");
                    }

                    await Task.Delay(50, cancellationToken).ConfigureAwait(false);
                }
                GC.KeepAlive(callback);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException or
                                               BadImageFormatException or Win32Exception)
            {
                var detail = exception is DllNotFoundException
                    ? "The SimConnect runtime was not found. Install/repair MSFS 2024 or its SDK runtime; X-Plane and manual controls remain available."
                    : "MSFS 2024 is not accepting SimConnect clients yet. Start a flight; connection retries automatically.";
                PublishStatus(new BridgeStatus(
                    BridgeConnectionState.Disconnected,
                    "MSFS 2024 not connected",
                    "Manual cabin controls available",
                    detail));
            }
            finally
            {
                CloseConnection();
            }

            await WaitForRetryAsync(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
        }
    }

    private void OpenConnection()
    {
        var result = SimConnectOpen(out _connection, "FreeFlight Cabin Control", IntPtr.Zero, 0, IntPtr.Zero, 0);
        if (result < 0 || _connection == IntPtr.Zero)
        {
            throw new Win32Exception(result, "Could not open SimConnect.");
        }

        AddDouble("PLANE ALTITUDE", "feet", 0);
        AddDouble("PLANE ALT ABOVE GROUND", "feet", 1);
        AddDouble("GROUND VELOCITY", "meters per second", 2);
        AddDouble("VERTICAL SPEED", "feet per minute", 3);
        AddDouble("SIM ON GROUND", "bool", 4);
        AddDouble("CABIN SEATBELTS ALERT SWITCH", "bool", 5);
        AddDouble("EXIT OPEN:1", "percent over 100", 6);
        AddDouble("EXIT OPEN:2", "percent over 100", 7);
        AddDouble("GENERAL ENG COMBUSTION:1", "bool", 8);
        AddDouble("GENERAL ENG COMBUSTION:2", "bool", 9);
        AddDouble("LOCAL TIME", "seconds", 10);
        AddDouble("PUSHBACK STATE", "enum", 11);
        result = SimConnectRequestDataOnSimObject(
            _connection, RequestId, DefinitionId, UserObjectId, SimConnectPeriod.Second, 0, 0, 0, 0);
        if (result < 0)
        {
            throw new Win32Exception(result, "Could not request MSFS telemetry.");
        }
    }

    private void AddDouble(string name, string units, uint datumId)
    {
        var result = SimConnectAddToDataDefinition(
            _connection, DefinitionId, name, units, SimConnectDataType.Float64, 0f, datumId);
        if (result < 0)
        {
            throw new Win32Exception(result, $"Could not subscribe to {name}.");
        }
    }

    private void HandleDispatch(IntPtr data, uint size, IntPtr context)
    {
        if (data == IntPtr.Zero || size < 40)
        {
            return;
        }

        var id = Marshal.ReadInt32(data, 8);
        if (id == (int)ReceiveId.Quit)
        {
            CloseConnection();
            return;
        }

        if (id != (int)ReceiveId.SimObjectData || Marshal.ReadInt32(data, 12) != RequestId)
        {
            return;
        }

        var telemetry = Marshal.PtrToStructure<MsfsTelemetry>(IntPtr.Add(data, 40));
        var timestamp = DateTimeOffset.UtcNow;
        Interlocked.Exchange(ref _lastFrameUtcTicks, timestamp.UtcTicks);
        var onGround = telemetry.OnGround >= 0.5d;
        var enginesRunning = telemetry.Engine1Running >= 0.5d || telemetry.Engine2Running >= 0.5d;
        var phase = XPlaneFlightPhaseClassifier.Classify(
            onGround,
            telemetry.GroundSpeed,
            telemetry.AltitudeAglFeet,
            telemetry.VerticalSpeedFeetPerMinute,
            enginesRunning);
        var signals = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["altitude_agl_ft"] = telemetry.AltitudeAglFeet,
            ["groundspeed_mps"] = telemetry.GroundSpeed,
            ["vertical_speed_fpm"] = telemetry.VerticalSpeedFeetPerMinute,
            ["engines_running"] = enginesRunning ? 1d : 0d,
            ["door_l1_ratio"] = Math.Clamp(telemetry.Exit1Percent / 100d, 0d, 1d),
            ["door_l2_ratio"] = Math.Clamp(telemetry.Exit2Percent / 100d, 0d, 1d),
            ["sim_local_time_sec"] = telemetry.LocalTimeSeconds,
            ["seatbelt_signal_available"] = 1d,
            ["seatbelt_signal_raw"] = telemetry.SeatbeltSign,
            ["pushback_active"] = telemetry.PushbackState >= 0.5d ||
                                  (onGround && telemetry.GroundSpeed >= 0.35d && telemetry.AltitudeAglFeet < 15d)
                ? 1d
                : 0d
        };
        try
        {
            TelemetryReceived?.Invoke(new CabinTelemetrySnapshot(
                timestamp,
                phase,
                telemetry.AltitudeFeet,
                onGround,
                telemetry.SeatbeltSign >= 0.5d,
                signals));
        }
        catch (Exception exception)
        {
            _log.Error("An MSFS telemetry subscriber failed.", exception);
        }
    }

    private void CloseConnection()
    {
        var connection = Interlocked.Exchange(ref _connection, IntPtr.Zero);
        if (connection == IntPtr.Zero) return;
        try { _ = SimConnectClose(connection); } catch (DllNotFoundException) { }
    }

    private void PublishStatus(BridgeStatus status)
    {
        if (status == _currentStatus) return;
        _currentStatus = status;
        try { StatusChanged?.Invoke(status); }
        catch (Exception exception) { _log.Error("An MSFS bridge status subscriber failed.", exception); }
    }

    private async Task WaitForRetryAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        using var retryCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        retryCancellation.CancelAfter(delay);
        try { await _reconnectSignal.WaitAsync(retryCancellation.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) { }
    }

    private delegate void DispatchProc(IntPtr data, uint size, IntPtr context);

    private enum ReceiveId : uint { Null, Exception, Open, Quit, Event, EventObject, EventFilename, EventFrame, SimObjectData }
    private enum SimConnectDataType : uint { Invalid, Int32, Int64, Float32, Float64 }
    private enum SimConnectPeriod : uint { Never, Once, VisualFrame, SimFrame, Second }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct MsfsTelemetry
    {
        public double AltitudeFeet;
        public double AltitudeAglFeet;
        public double GroundSpeed;
        public double VerticalSpeedFeetPerMinute;
        public double OnGround;
        public double SeatbeltSign;
        public double Exit1Percent;
        public double Exit2Percent;
        public double Engine1Running;
        public double Engine2Running;
        public double LocalTimeSeconds;
        public double PushbackState;
    }

    [DllImport("SimConnect.dll", EntryPoint = "SimConnect_Open", CharSet = CharSet.Ansi)]
    private static extern int SimConnectOpen(
        out IntPtr connection,
        [MarshalAs(UnmanagedType.LPStr)] string name,
        IntPtr window,
        uint userEvent,
        IntPtr eventHandle,
        uint configurationIndex);

    [DllImport("SimConnect.dll", EntryPoint = "SimConnect_Close")]
    private static extern int SimConnectClose(IntPtr connection);

    [DllImport("SimConnect.dll", EntryPoint = "SimConnect_AddToDataDefinition", CharSet = CharSet.Ansi)]
    private static extern int SimConnectAddToDataDefinition(
        IntPtr connection,
        uint definitionId,
        [MarshalAs(UnmanagedType.LPStr)] string datumName,
        [MarshalAs(UnmanagedType.LPStr)] string unitsName,
        SimConnectDataType dataType,
        float epsilon,
        uint datumId);

    [DllImport("SimConnect.dll", EntryPoint = "SimConnect_RequestDataOnSimObject")]
    private static extern int SimConnectRequestDataOnSimObject(
        IntPtr connection,
        uint requestId,
        uint definitionId,
        uint objectId,
        SimConnectPeriod period,
        uint flags,
        uint origin,
        uint interval,
        uint limit);

    [DllImport("SimConnect.dll", EntryPoint = "SimConnect_CallDispatch")]
    private static extern int SimConnectCallDispatch(IntPtr connection, DispatchProc callback, IntPtr context);
}
