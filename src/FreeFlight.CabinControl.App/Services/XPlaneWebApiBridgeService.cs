using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.IO;
using System.Text;
using System.Text.Json;
using FreeFlight.CabinControl.Core.Configuration;
using FreeFlight.CabinControl.Core.Integration;

namespace FreeFlight.CabinControl.App.Services;

public sealed class XPlaneWebApiBridgeService : ISimulatorBridge, ISimulatorCabinControlBridge
{
    private const double MetresToFeet = 3.280839895d;
    private const string AltitudeMsl = "sim/flightmodel/position/elevation";
    private const string AltitudeAgl = "sim/flightmodel/position/y_agl";
    private const string GroundSpeed = "sim/flightmodel/position/groundspeed";
    private const string VerticalSpeed = "sim/flightmodel/position/vh_ind_fpm";
    private const string OnGroundAny = "sim/flightmodel/failures/onground_any";
    private const string GearOnGround = "sim/flightmodel2/gear/on_ground";
    private const string SeatbeltAnnunciator = "sim/cockpit2/annunciators/fasten_seatbelt";
    private const string SeatbeltSwitch = "sim/cockpit2/switches/fasten_seat_belts";
    private const string LegacySeatbeltSwitch = "sim/cockpit/switches/fasten_seat_belts";
    private const string DoorOpenRatio = "sim/flightmodel2/misc/door_open_ratio";
    private const string FlightFactorDoorL1Ratio = "1-sim/anim/doorL1";
    private const string FlightFactorDoorL2Ratio = "1-sim/anim/doorL2";
    private const string FlightFactorSeatbeltLight = "1-sim/anim/seatbeltLight";
    private const string FlightFactorSeatbeltSelector = "1-sim/ckpt/passSignsSeatbeltsSwitch/anim";
    private const string EnginesRunning = "sim/flightmodel/engine/ENGN_running";
    private const string AircraftIcao = "sim/aircraft/view/acf_ICAO";
    private const string AircraftDescription = "sim/aircraft/view/acf_descrip";
    private const string AircraftRelativePath = "sim/aircraft/view/acf_relative_path";
    private const string SimulatorRunningTime = "sim/time/total_running_time_sec";
    private const string SimulatorLocalTime = "sim/time/local_time_sec";
    private const string FrameRatePeriod = "sim/operation/misc/frame_rate_period";
    private const string FreeFlightPluginOnline = "freeflight/cabin/plugin_online";
    private const string FreeFlightSeatbeltAvailable = "freeflight/cabin/seatbelt_available";
    private const string FreeFlightSeatbeltSign = "freeflight/cabin/seatbelt_sign";
    private const string FreeFlightDoorL1Available = "freeflight/cabin/door_l1_available";
    private const string FreeFlightDoorL1Ratio = "freeflight/cabin/door_l1_ratio";
    private const string FreeFlightDoorL2Available = "freeflight/cabin/door_l2_available";
    private const string FreeFlightDoorL2Ratio = "freeflight/cabin/door_l2_ratio";

    private static readonly HashSet<string> RequestedDatarefs =
    [
        AltitudeMsl,
        AltitudeAgl,
        GroundSpeed,
        VerticalSpeed,
        OnGroundAny,
        GearOnGround,
        SeatbeltAnnunciator,
        SeatbeltSwitch,
        LegacySeatbeltSwitch,
        DoorOpenRatio,
        FlightFactorDoorL1Ratio,
        FlightFactorDoorL2Ratio,
        FlightFactorSeatbeltLight,
        FlightFactorSeatbeltSelector,
        EnginesRunning,
        AircraftIcao,
        AircraftDescription,
        AircraftRelativePath,
        FrameRatePeriod,
        SimulatorRunningTime,
        SimulatorLocalTime,
        FreeFlightPluginOnline,
        FreeFlightSeatbeltAvailable,
        FreeFlightSeatbeltSign,
        FreeFlightDoorL1Available,
        FreeFlightDoorL1Ratio,
        FreeFlightDoorL2Available,
        FreeFlightDoorL2Ratio
    ];

    private static readonly IReadOnlyList<IAircraftCabinAdapter> AircraftCabinAdapters =
    [
        new FlightFactor777V2CabinAdapter()
    ];

    private readonly AppSettings _settings;
    private readonly FileLogService _log;
    private readonly HttpClient _httpClient;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _reconnectSignal = new(0, 1);
    private readonly object _socketLock = new();
    private readonly object _valuesLock = new();
    private readonly Dictionary<string, XPlaneValue> _values = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SignalHistory> _signalHistory = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _controlWriteLock = new(1, 1);
    private IReadOnlyDictionary<string, XPlaneDataref> _datarefsByName =
        new Dictionary<string, XPlaneDataref>(StringComparer.Ordinal);
    private ClientWebSocket? _activeSocket;
    private Task? _runTask;
    private BridgeStatus _currentStatus = BridgeStatus.XPlaneOffline;
    private long _lastFrameUtcTicks;
    private string? _lastLoggedFailure;
    private string _apiVersion = "v1";
    private string _simulatorVersion = "12.1.1+";
    private string _lastDatarefDiscoveryAircraftPath = string.Empty;
    private int _aircraftRediscoveryPending;
    private bool _disposed;

    public XPlaneWebApiBridgeService(AppSettings settings, FileLogService log)
    {
        _settings = settings;
        _log = log;
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
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
        if (_disposed)
        {
            return;
        }

        AbortActiveSocket();
        if (_reconnectSignal.CurrentCount == 0)
        {
            try
            {
                _reconnectSignal.Release();
            }
            catch (SemaphoreFullException)
            {
            }
        }
    }

    public async Task<bool> SetPassengerDoorOpenAsync(
        int doorNumber,
        bool isOpen,
        CancellationToken cancellationToken = default)
    {
        if (doorNumber is not (1 or 2))
        {
            return false;
        }

        foreach (var target in ResolveWritableDoorTargets(doorNumber))
        {
            if (await WriteDatarefAsync(target.Dataref, isOpen ? 1d : 0d, target.Index, cancellationToken)
                    .ConfigureAwait(false))
            {
                return true;
            }
        }

        return false;
    }

    public async Task<bool> SetSeatbeltSignAsync(
        bool isOn,
        CancellationToken cancellationToken = default)
    {
        foreach (var target in ResolveWritableSeatbeltTargets())
        {
            var value = target.Name == FlightFactorSeatbeltSelector
                ? isOn ? 2d : 0d
                : isOn ? 1d : 0d;
            if (await WriteDatarefAsync(target, value, null, cancellationToken).ConfigureAwait(false))
            {
                return true;
            }
        }

        return false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _lifetime.Cancel();
        AbortActiveSocket();
        GC.SuppressFinalize(this);
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            if (!_settings.XPlaneAutoConnect)
            {
                PublishStatus(new BridgeStatus(
                    BridgeConnectionState.Disconnected,
                    "X-Plane auto-connect disabled",
                    "Manual cabin controls available",
                    "Enable X-Plane auto-connect in Settings."));
                await WaitForRetryAsync(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
                continue;
            }

            PublishStatus(new BridgeStatus(
                BridgeConnectionState.Connecting,
                "Looking for X-Plane",
                "Detecting aircraft",
                $"Probing the local Web API on port {SanitizePort(_settings.XPlaneWebApiPort)}."));

            try
            {
                await ConnectAndStreamAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (XPlaneBridgeException exception)
            {
                PublishStatus(new BridgeStatus(
                    exception.State,
                    "X-Plane not connected",
                    "Manual cabin controls available",
                    exception.Message));
                LogConnectionFailureOnce(exception.Message);
            }
            catch (Exception exception) when (exception is HttpRequestException or WebSocketException or
                                               JsonException or IOException or OperationCanceledException)
            {
                var detail = DescribeConnectionFailure(exception);
                PublishStatus(new BridgeStatus(
                    BridgeConnectionState.Disconnected,
                    "X-Plane not connected",
                    "Manual cabin controls available",
                    detail));
                LogConnectionFailureOnce(detail);
            }
            finally
            {
                ClearActiveSocket();
                lock (_valuesLock)
                {
                    _values.Clear();
                    _signalHistory.Clear();
                    _datarefsByName = new Dictionary<string, XPlaneDataref>(StringComparer.Ordinal);
                }
            }

            await WaitForRetryAsync(TimeSpan.FromSeconds(3), cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ConnectAndStreamAsync(CancellationToken cancellationToken)
    {
        var port = SanitizePort(_settings.XPlaneWebApiPort);
        var capabilities = await DiscoverCapabilitiesAsync(port, cancellationToken).ConfigureAwait(false);
        _apiVersion = capabilities.ApiVersion;
        _simulatorVersion = capabilities.SimulatorVersion;

        var descriptors = await DiscoverDatarefsAsync(port, _apiVersion, cancellationToken).ConfigureAwait(false);
        lock (_valuesLock)
        {
            _datarefsByName = descriptors;
        }
        if (!descriptors.ContainsKey(GroundSpeed) ||
            (!descriptors.ContainsKey(OnGroundAny) && !descriptors.ContainsKey(GearOnGround)))
        {
            throw new XPlaneBridgeException(
                BridgeConnectionState.Incompatible,
                "The X-Plane API is reachable, but its standard flight datarefs are unavailable.");
        }

        using var socket = new ClientWebSocket();
        SetActiveSocket(socket);
        var socketUri = new Uri($"ws://127.0.0.1:{port}/api/{_apiVersion}");
        await socket.ConnectAsync(socketUri, cancellationToken).ConfigureAwait(false);
        await SubscribeAsync(socket, descriptors.Values, cancellationToken).ConfigureAwait(false);

        _lastLoggedFailure = null;
        PublishStatus(new BridgeStatus(
            BridgeConnectionState.Connected,
            $"X-Plane {_simulatorVersion}",
            "Aircraft telemetry detected",
            $"Web API {_apiVersion} · {descriptors.Count} cabin datarefs · live at up to 10 Hz"));
        _log.Information($"Connected to X-Plane {_simulatorVersion} through Web API {_apiVersion} on port {port}.");
        LogCabinSignalDiscovery(descriptors.Values);

        var descriptorsById = descriptors.Values.ToDictionary(item => item.Id);
        await ReceiveLoopAsync(socket, descriptorsById, cancellationToken).ConfigureAwait(false);
        throw new HttpRequestException("The X-Plane WebSocket closed.");
    }

    private async Task<XPlaneCapabilities> DiscoverCapabilitiesAsync(int port, CancellationToken cancellationToken)
    {
        var uri = new Uri($"http://127.0.0.1:{port}/api/capabilities");
        using var response = await _httpClient.GetAsync(uri, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return new XPlaneCapabilities("v1", "12.1.1+");
        }

        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            throw new XPlaneBridgeException(
                BridgeConnectionState.Incompatible,
                "X-Plane is blocking incoming traffic. In X-Plane Settings → Network, allow incoming traffic.");
        }

        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;
        var versions = root.GetProperty("api").GetProperty("versions")
            .EnumerateArray()
            .Select(item => item.GetString())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item!)
            .ToArray();
        var apiVersion = versions
            .OrderByDescending(ParseApiVersion)
            .FirstOrDefault() ?? "v1";
        var simulatorVersion = root.TryGetProperty("x-plane", out var simulator) &&
                               simulator.TryGetProperty("version", out var version)
            ? version.GetString() ?? "12"
            : "12";
        return new XPlaneCapabilities(apiVersion, simulatorVersion);
    }

    private async Task<Dictionary<string, XPlaneDataref>> DiscoverDatarefsAsync(
        int port,
        string apiVersion,
        CancellationToken cancellationToken)
    {
        var uri = new Uri($"http://127.0.0.1:{port}/api/{apiVersion}/datarefs?fields=id,name,value_type");
        using var response = await _httpClient.GetAsync(uri, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            throw new XPlaneBridgeException(
                BridgeConnectionState.Incompatible,
                "X-Plane is blocking incoming traffic. In X-Plane Settings → Network, allow incoming traffic.");
        }

        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var available = new List<XPlaneDataref>();
        foreach (var item in document.RootElement.GetProperty("data").EnumerateArray())
        {
            var name = item.GetProperty("name").GetString();
            var valueType = item.GetProperty("value_type").GetString() ?? "float";
            if (name is null)
            {
                continue;
            }

            available.Add(new XPlaneDataref(
                item.GetProperty("id").GetInt64(),
                name,
                valueType));
        }

        var requested = available.Where(item => RequestedDatarefs.Contains(item.Name));
        var doorCandidates = available
            .Where(item => !RequestedDatarefs.Contains(item.Name) && IsDoorCandidate(item.Name, item.ValueType))
            .OrderByDescending(item => ScoreDoorDiscoveryName(item.Name))
            .ThenBy(item => item.Name, StringComparer.Ordinal)
            .Take(96);
        var seatbeltCandidates = available
            .Where(item => !RequestedDatarefs.Contains(item.Name) && IsSeatbeltCandidate(item.Name, item.ValueType))
            .OrderByDescending(item => ScoreSeatbeltName(item.Name))
            .ThenBy(item => item.Name, StringComparer.Ordinal)
            .Take(64);
        var discovered = requested
            .Concat(doorCandidates)
            .Concat(seatbeltCandidates)
            .DistinctBy(item => item.Name, StringComparer.Ordinal)
            .ToDictionary(item => item.Name, StringComparer.Ordinal);

        return discovered;
    }

    private static async Task SubscribeAsync(
        ClientWebSocket socket,
        IEnumerable<XPlaneDataref> descriptors,
        CancellationToken cancellationToken)
    {
        var message = JsonSerializer.SerializeToUtf8Bytes(new
        {
            req_id = 1,
            type = "dataref_subscribe_values",
            @params = new
            {
                datarefs = descriptors.Select(item => new { id = item.Id }).ToArray()
            }
        });
        await socket.SendAsync(message, WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);
    }

    private async Task ReceiveLoopAsync(
        ClientWebSocket socket,
        IReadOnlyDictionary<long, XPlaneDataref> descriptorsById,
        CancellationToken cancellationToken)
    {
        var receiveBuffer = new byte[32 * 1024];
        while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            using var messageBuffer = new MemoryStream();
            WebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(receiveBuffer, cancellationToken).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return;
                }

                messageBuffer.Write(receiveBuffer, 0, result.Count);
                if (messageBuffer.Length > 1024 * 1024)
                {
                    throw new JsonException("X-Plane sent an unexpectedly large telemetry message.");
                }
            } while (!result.EndOfMessage);

            if (result.MessageType != WebSocketMessageType.Text)
            {
                continue;
            }

            ProcessMessage(messageBuffer.GetBuffer().AsMemory(0, checked((int)messageBuffer.Length)), descriptorsById);
        }
    }

    private void ProcessMessage(
        ReadOnlyMemory<byte> message,
        IReadOnlyDictionary<long, XPlaneDataref> descriptorsById)
    {
        using var document = JsonDocument.Parse(message);
        var root = document.RootElement;
        if (!root.TryGetProperty("type", out var messageType) ||
            messageType.GetString() != "dataref_update_values" ||
            !root.TryGetProperty("data", out var data))
        {
            return;
        }

        lock (_valuesLock)
        {
            foreach (var item in data.EnumerateObject())
            {
                if (!long.TryParse(item.Name, out var id) || !descriptorsById.TryGetValue(id, out var descriptor))
                {
                    continue;
                }

                var parsed = XPlaneValue.Parse(item.Value, descriptor.ValueType);
                if (_values.TryGetValue(descriptor.Name, out var previous) && !previous.EquivalentTo(parsed))
                {
                    if (!_signalHistory.TryGetValue(descriptor.Name, out var history))
                    {
                        history = new SignalHistory();
                        _signalHistory[descriptor.Name] = history;
                    }

                    history.ObserveChange(DateTimeOffset.UtcNow);
                }

                _values[descriptor.Name] = parsed;
            }
        }

        ScheduleAircraftDatarefRediscoveryIfNeeded();

        var timestamp = DateTimeOffset.UtcNow;
        Interlocked.Exchange(ref _lastFrameUtcTicks, timestamp.UtcTicks);
        UpdateConnectedAircraftStatus();
        PublishTelemetry(CreateSnapshot(timestamp));
    }

    private CabinTelemetrySnapshot CreateSnapshot(DateTimeOffset timestamp)
    {
        var altitudeFeet = GetScalar(AltitudeMsl) * MetresToFeet;
        var altitudeAglFeet = GetScalar(AltitudeAgl) * MetresToFeet;
        var groundSpeed = GetScalar(GroundSpeed);
        var verticalSpeed = GetScalar(VerticalSpeed);
        var onGround = GetScalar(OnGroundAny) >= 0.5d || GetArray(GearOnGround).Any(value => value >= 0.5d);
        var anyEngineRunning = GetArray(EnginesRunning).Any(value => value >= 0.5d);
        var seatbeltSignal = ResolveSeatbeltSignal();
        var seatbeltSignOn = seatbeltSignal.IsAvailable && seatbeltSignal.Value >= 0.5d;
        var (l1DoorRatio, l2DoorRatio) = ResolveDoorRatios();
        var phase = XPlaneFlightPhaseClassifier.Classify(
            onGround,
            groundSpeed,
            altitudeAglFeet,
            verticalSpeed,
            anyEngineRunning);
        var signals = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["altitude_agl_ft"] = altitudeAglFeet,
            ["groundspeed_mps"] = groundSpeed,
            ["vertical_speed_fpm"] = verticalSpeed,
            ["engines_running"] = anyEngineRunning ? 1d : 0d,
            ["simulator_fps"] = GetScalar(FrameRatePeriod) > 0.0001d ? 1d / GetScalar(FrameRatePeriod) : 0d,
            ["simulator_running_time_sec"] = GetScalar(SimulatorRunningTime),
            ["sim_local_time_sec"] = GetScalar(SimulatorLocalTime),
            ["freeflight_plugin_online"] = GetScalar(FreeFlightPluginOnline) >= 0.5d ? 1d : 0d,
            ["seatbelt_signal_available"] = seatbeltSignal.IsAvailable ? 1d : 0d,
            ["seatbelt_signal_raw"] = seatbeltSignal.IsAvailable ? seatbeltSignal.Value : double.NaN,
            ["pushback_active"] = onGround && groundSpeed >= 0.35d && altitudeAglFeet < 15d ? 1d : 0d
        };
        if (!double.IsNaN(l1DoorRatio))
        {
            signals["door_l1_ratio"] = l1DoorRatio;
        }

        if (!double.IsNaN(l2DoorRatio))
        {
            signals["door_l2_ratio"] = l2DoorRatio;
        }

        return new CabinTelemetrySnapshot(
            timestamp,
            phase,
            altitudeFeet,
            onGround,
            seatbeltSignOn,
            signals);
    }

    private void ScheduleAircraftDatarefRediscoveryIfNeeded()
    {
        var aircraftPath = GetText(AircraftRelativePath).Trim();
        if (aircraftPath.Length == 0 ||
            string.Equals(aircraftPath, _lastDatarefDiscoveryAircraftPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _lastDatarefDiscoveryAircraftPath = aircraftPath;
        if (Interlocked.Exchange(ref _aircraftRediscoveryPending, 1) != 0)
        {
            return;
        }

        _log.Information($"Active ACF changed to {aircraftPath}; scheduling cabin-dataref rediscovery.");
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(1.5d), _lifetime.Token).ConfigureAwait(false);
                RequestReconnect();
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                Interlocked.Exchange(ref _aircraftRediscoveryPending, 0);
            }
        });
    }

    private void UpdateConnectedAircraftStatus()
    {
        var icao = GetText(AircraftIcao).ToUpperInvariant();
        var description = GetText(AircraftDescription);
        var acfPath = ResolveActiveAircraftPath(GetText(AircraftRelativePath));
        var adapter = AircraftCabinAdapters.FirstOrDefault(candidate => candidate.Matches(new AircraftIdentity(
            icao,
            description,
            GetText(AircraftRelativePath))));
        var aircraft = !string.IsNullOrWhiteSpace(description)
            ? description
            : !string.IsNullOrWhiteSpace(icao) ? icao : "Aircraft telemetry detected";
        if (!string.IsNullOrWhiteSpace(icao) && !aircraft.Contains(icao, StringComparison.OrdinalIgnoreCase))
        {
            aircraft = $"{icao} · {aircraft}";
        }

        if (aircraft.Length > 72)
        {
            aircraft = aircraft[..69] + "…";
        }

        PublishStatus(new BridgeStatus(
            BridgeConnectionState.Connected,
            $"X-Plane {_simulatorVersion}",
            aircraft,
            string.IsNullOrWhiteSpace(acfPath)
                ? $"Web API {_apiVersion} · live telemetry up to 10 Hz{FormatPluginStatus()}{FormatAdapterStatus(adapter)}"
                : $"Web API {_apiVersion} · {Path.GetFileName(acfPath)} · live telemetry up to 10 Hz{FormatPluginStatus()}{FormatAdapterStatus(adapter)}"));
    }

    private static string FormatAdapterStatus(IAircraftCabinAdapter? adapter) => adapter is null
        ? string.Empty
        : $" · {adapter.DisplayName} adapter-ready";

    private string FormatPluginStatus() => GetScalar(FreeFlightPluginOnline) >= 0.5d
        ? " · FreeFlight plugin active"
        : " · plugin fallback";

    private (double L1, double L2) ResolveDoorRatios()
    {
        var pluginL1 = double.NaN;
        var pluginL2 = double.NaN;
        if (GetScalar(FreeFlightPluginOnline) >= 0.5d)
        {
            pluginL1 = GetScalar(FreeFlightDoorL1Available) >= 0.5d
                ? NormalizeDoorRatio(GetScalar(FreeFlightDoorL1Ratio))
                : double.NaN;
            pluginL2 = GetScalar(FreeFlightDoorL2Available) >= 0.5d
                ? NormalizeDoorRatio(GetScalar(FreeFlightDoorL2Ratio))
                : double.NaN;
        }

        var standard = GetArray(DoorOpenRatio);
        var l1 = !double.IsNaN(pluginL1)
            ? pluginL1
            : _values.TryGetValue(FlightFactorDoorL1Ratio, out var flightFactorL1)
                ? NormalizeDoorRatio(flightFactorL1.Scalar)
                : standard.Length > 0 ? NormalizeDoorRatio(standard[0]) : double.NaN;
        var l2 = !double.IsNaN(pluginL2)
            ? pluginL2
            : _values.TryGetValue(FlightFactorDoorL2Ratio, out var flightFactorL2)
                ? NormalizeDoorRatio(flightFactorL2.Scalar)
                : standard.Length > 1 ? NormalizeDoorRatio(standard[1]) : double.NaN;
        var candidates = _values
            .Where(pair => !string.Equals(pair.Key, DoorOpenRatio, StringComparison.Ordinal) &&
                           IsDoorCandidate(pair.Key, "float"))
            .ToArray();
        var namedL1 = ResolveNamedDoor(candidates, 1);
        var namedL2 = ResolveNamedDoor(candidates, 2);
        l1 = namedL1 is null || !double.IsNaN(pluginL1) ? l1 : NormalizeDoorRatio(namedL1.Value.Value.Scalar);
        l2 = namedL2 is null || !double.IsNaN(pluginL2) ? l2 : NormalizeDoorRatio(namedL2.Value.Value.Scalar);

        var arrayCandidate = candidates.Select(pair => pair.Value.Array).FirstOrDefault(array => array.Length >= 2);
        if (arrayCandidate is not null)
        {
            l1 = double.IsNaN(l1) ? NormalizeDoorRatio(arrayCandidate[0]) : l1;
            l2 = double.IsNaN(l2) ? NormalizeDoorRatio(arrayCandidate[1]) : l2;
        }

        return (l1, l2);
    }

    private (bool IsAvailable, double Value) ResolveSeatbeltSignal()
    {
        if (GetScalar(FreeFlightPluginOnline) >= 0.5d && GetScalar(FreeFlightSeatbeltAvailable) >= 0.5d)
        {
            return (true, GetScalar(FreeFlightSeatbeltSign));
        }

        if (_values.TryGetValue(FlightFactorSeatbeltLight, out var flightFactorLight) &&
            double.IsFinite(flightFactorLight.Scalar))
        {
            return (true, flightFactorLight.Scalar >= 0.5d ? 1d : 0d);
        }

        var selected = _values
            .Where(pair => IsStandardSeatbeltDataref(pair.Key) || IsSeatbeltCandidate(pair.Key, "float"))
            .Where(pair => double.IsFinite(pair.Value.Scalar))
            .Select(pair => new
            {
                pair.Value.Scalar,
                Score = ScoreSeatbeltSignal(pair.Key, pair.Value.Scalar)
            })
            .OrderByDescending(item => item.Score)
            .FirstOrDefault();
        return selected is null ? (false, 0d) : (true, selected.Scalar);
    }

    private KeyValuePair<string, XPlaneValue>? ResolveNamedDoor(
        IEnumerable<KeyValuePair<string, XPlaneValue>> candidates,
        int doorNumber)
    {
        var selected = candidates
            .Select(pair => new
            {
                Pair = pair,
                Score = ScoreDoorName(pair.Key, doorNumber) + ScoreLiveSignal(pair.Key, pair.Value.Scalar)
            })
            .Where(item => item.Score > 0)
            .OrderByDescending(item => item.Score)
            .FirstOrDefault();
        return selected?.Pair;
    }

    private static int ScoreDoorName(string name, int doorNumber)
    {
        var value = name.ToLowerInvariant().Replace('-', '_');
        var score = 0;
        if (value.Contains($"l{doorNumber}", StringComparison.Ordinal) ||
            value.Contains($"door_{doorNumber}", StringComparison.Ordinal) ||
            value.Contains($"door{doorNumber}", StringComparison.Ordinal) ||
            value.Contains($"entry_{doorNumber}", StringComparison.Ordinal))
        {
            score += 20;
        }

        if (value.Contains("left", StringComparison.Ordinal) || value.Contains("entry", StringComparison.Ordinal))
        {
            score += 6;
        }

        if (value.Contains("right", StringComparison.Ordinal) || value.Contains("cargo", StringComparison.Ordinal) ||
            value.Contains("service", StringComparison.Ordinal) || value.Contains("cockpit", StringComparison.Ordinal))
        {
            score -= 15;
        }

        return score;
    }

    private static int ScoreDoorDiscoveryName(string name) =>
        Math.Max(ScoreDoorName(name, 1), ScoreDoorName(name, 2)) +
        (!name.StartsWith("sim/", StringComparison.Ordinal) ? 8 : 0);

    private static bool IsDoorCandidate(string name, string valueType)
    {
        var value = name.ToLowerInvariant();
        var numeric = !valueType.Contains("data", StringComparison.OrdinalIgnoreCase) &&
                      !valueType.Contains("string", StringComparison.OrdinalIgnoreCase);
        return numeric &&
               (value.Contains("door", StringComparison.Ordinal) || value.Contains("exit", StringComparison.Ordinal)) &&
               !value.Contains("command", StringComparison.Ordinal);
    }

    private static bool IsSeatbeltCandidate(string name, string valueType)
    {
        var value = name.ToLowerInvariant();
        var numeric = !valueType.Contains("data", StringComparison.OrdinalIgnoreCase) &&
                      !valueType.Contains("string", StringComparison.OrdinalIgnoreCase);
        return numeric &&
               ((value.Contains("seat", StringComparison.Ordinal) && value.Contains("belt", StringComparison.Ordinal)) ||
                (value.Contains("fasten", StringComparison.Ordinal) && value.Contains("seat", StringComparison.Ordinal)));
    }

    private static bool IsStandardSeatbeltDataref(string name) => name is
        SeatbeltAnnunciator or SeatbeltSwitch or LegacySeatbeltSwitch;

    private static int ScoreSeatbeltName(string name)
    {
        var value = name.ToLowerInvariant();
        var score = 1;
        if (value.Contains("annunciator", StringComparison.Ordinal) ||
            value.Contains("light", StringComparison.Ordinal) ||
            value.Contains("status", StringComparison.Ordinal) ||
            value.Contains("sign_on", StringComparison.Ordinal))
        {
            score += 30;
        }
        if (value.Contains("switch", StringComparison.Ordinal) || value.Contains("command", StringComparison.Ordinal))
        {
            score -= 10;
        }
        if (!value.StartsWith("sim/", StringComparison.Ordinal))
        {
            score += 8;
        }

        return score;
    }

    private int ScoreSeatbeltSignal(string name, double value)
    {
        var score = name switch
        {
            SeatbeltAnnunciator => 45,
            SeatbeltSwitch => 35,
            LegacySeatbeltSwitch => 30,
            _ => ScoreSeatbeltName(name)
        };
        return score + ScoreLiveSignal(name, value);
    }

    private int ScoreLiveSignal(string name, double value)
    {
        var score = Math.Abs(value) >= 0.5d ? 8 : 0;
        if (!_signalHistory.TryGetValue(name, out var history))
        {
            return score;
        }

        score += Math.Min(history.ChangeCount, 4) * 18;
        if (DateTimeOffset.UtcNow - history.LastChangedAt <= TimeSpan.FromMinutes(5))
        {
            score += 80;
        }

        return score;
    }

    private static double NormalizeDoorRatio(double value)
    {
        if (!double.IsFinite(value) || value < 0d)
        {
            return double.NaN;
        }

        return Math.Clamp(value > 1.5d ? value / 100d : value, 0d, 1d);
    }

    private IReadOnlyList<XPlaneWriteTarget> ResolveWritableDoorTargets(int doorNumber)
    {
        lock (_valuesLock)
        {
            var targets = new List<XPlaneWriteTarget>();
            var pluginDoorName = doorNumber == 1 ? FreeFlightDoorL1Ratio : FreeFlightDoorL2Ratio;
            if (_datarefsByName.TryGetValue(pluginDoorName, out var pluginDoorDataref))
            {
                targets.Add(new XPlaneWriteTarget(pluginDoorDataref, null));
            }

            var customCandidates = _values
                .Where(pair => !string.Equals(pair.Key, DoorOpenRatio, StringComparison.Ordinal) &&
                               IsDoorCandidate(pair.Key, "float"))
                .ToArray();
            var selected = ResolveNamedDoor(customCandidates, doorNumber);
            if (selected is { } named && _datarefsByName.TryGetValue(named.Key, out var customDataref))
            {
                targets.Add(new XPlaneWriteTarget(customDataref, null));
            }

            if (_datarefsByName.TryGetValue(DoorOpenRatio, out var standardDataref))
            {
                targets.Add(new XPlaneWriteTarget(standardDataref, doorNumber - 1));
            }

            return targets
                .DistinctBy(target => (target.Dataref.Name, target.Index))
                .ToArray();
        }
    }

    private IReadOnlyList<XPlaneDataref> ResolveWritableSeatbeltTargets()
    {
        lock (_valuesLock)
        {
            return new[] { FreeFlightSeatbeltSign, FlightFactorSeatbeltSelector }
                .Concat(_values
                .Where(pair => IsSeatbeltCandidate(pair.Key, "float") &&
                               pair.Key != FlightFactorSeatbeltLight)
                .OrderByDescending(pair =>
                    ScoreSeatbeltSignal(pair.Key, pair.Value.Scalar) +
                    (pair.Key.Contains("switch", StringComparison.OrdinalIgnoreCase) ||
                     pair.Key.Contains("control", StringComparison.OrdinalIgnoreCase) ? 35 : 0))
                .Select(pair => pair.Key))
                .Concat([SeatbeltSwitch, LegacySeatbeltSwitch])
                .Distinct(StringComparer.Ordinal)
                .Select(name => _datarefsByName.GetValueOrDefault(name))
                .Where(dataref => dataref is not null)
                .Select(dataref => dataref!)
                .Take(6)
                .ToArray();
        }
    }

    private async Task<bool> WriteDatarefAsync(
        XPlaneDataref dataref,
        double value,
        int? index,
        CancellationToken cancellationToken)
    {
        if (_currentStatus.State != BridgeConnectionState.Connected)
        {
            return false;
        }

        await _controlWriteLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var port = SanitizePort(_settings.XPlaneWebApiPort);
            var suffix = index is null ? string.Empty : $"?index={index.Value}";
            var uri = new Uri($"http://127.0.0.1:{port}/api/{_apiVersion}/datarefs/{dataref.Id}/value{suffix}");
            using var request = new HttpRequestMessage(HttpMethod.Patch, uri)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new { data = value }),
                    Encoding.UTF8,
                    "application/json")
            };
            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _log.Information(
                    $"X-Plane rejected cabin control {dataref.Name}{(index is null ? string.Empty : $"[{index}]")} " +
                    $"with HTTP {(int)response.StatusCode}; trying the next safe mapping.");
                return false;
            }

            _log.Information(
                $"X-Plane cabin control wrote {dataref.Name}{(index is null ? string.Empty : $"[{index}]")} = {value:0}.");
            return true;
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or TaskCanceledException)
        {
            _log.Error($"X-Plane cabin control write failed for {dataref.Name}.", exception);
            return false;
        }
        finally
        {
            _controlWriteLock.Release();
        }
    }

    private void LogCabinSignalDiscovery(IEnumerable<XPlaneDataref> descriptors)
    {
        var cabinSignals = descriptors
            .Where(item => IsDoorCandidate(item.Name, item.ValueType) || IsSeatbeltCandidate(item.Name, item.ValueType))
            .Select(item => item.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
        _log.Information(cabinSignals.Length == 0
            ? "X-Plane exposed no numeric door or seat-belt candidates; manual fail-safes remain available."
            : $"X-Plane subscribed to {cabinSignals.Length} cabin signal candidates: {string.Join(", ", cabinSignals)}");
    }

    private string ResolveActiveAircraftPath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || string.IsNullOrWhiteSpace(_settings.XPlaneExecutablePath))
        {
            return string.Empty;
        }

        try
        {
            var configuredPath = _settings.XPlaneExecutablePath.Trim();
            var root = Directory.Exists(configuredPath)
                ? Path.GetFullPath(configuredPath)
                : Path.GetDirectoryName(configuredPath);
            if (string.IsNullOrWhiteSpace(root))
            {
                return string.Empty;
            }

            var candidate = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
            var rootWithSeparator = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return candidate.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase) && File.Exists(candidate)
                ? candidate
                : string.Empty;
        }
        catch (Exception exception) when (exception is IOException or ArgumentException or NotSupportedException)
        {
            return string.Empty;
        }
    }

    private double GetScalar(string name) =>
        _values.TryGetValue(name, out var value) ? value.Scalar : 0d;

    private double[] GetArray(string name) =>
        _values.TryGetValue(name, out var value) ? value.Array : [];

    private string GetText(string name) =>
        _values.TryGetValue(name, out var value) ? value.Text : string.Empty;

    private void PublishStatus(BridgeStatus status)
    {
        if (status == _currentStatus)
        {
            return;
        }

        _currentStatus = status;
        try
        {
            StatusChanged?.Invoke(status);
        }
        catch (Exception exception)
        {
            _log.Error("A bridge status subscriber failed.", exception);
        }
    }

    private void PublishTelemetry(CabinTelemetrySnapshot snapshot)
    {
        try
        {
            TelemetryReceived?.Invoke(snapshot);
        }
        catch (Exception exception)
        {
            LogFailureOnce("A telemetry subscriber failed.", exception);
        }
    }

    private async Task WaitForRetryAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        using var retryCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        retryCancellation.CancelAfter(delay);
        try
        {
            await _reconnectSignal.WaitAsync(retryCancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void SetActiveSocket(ClientWebSocket socket)
    {
        lock (_socketLock)
        {
            _activeSocket = socket;
        }
    }

    private void ClearActiveSocket()
    {
        lock (_socketLock)
        {
            _activeSocket = null;
        }
    }

    private void AbortActiveSocket()
    {
        lock (_socketLock)
        {
            try
            {
                _activeSocket?.Abort();
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }

    private void LogFailureOnce(string detail, Exception exception)
    {
        if (string.Equals(detail, _lastLoggedFailure, StringComparison.Ordinal))
        {
            return;
        }

        _lastLoggedFailure = detail;
        _log.Error($"X-Plane bridge: {detail}", exception);
    }

    private void LogConnectionFailureOnce(string detail)
    {
        if (string.Equals(detail, _lastLoggedFailure, StringComparison.Ordinal))
        {
            return;
        }

        _lastLoggedFailure = detail;
        _log.Information($"X-Plane bridge: {detail}");
    }

    private static string DescribeConnectionFailure(Exception exception) => exception switch
    {
        OperationCanceledException => "The X-Plane connection timed out. Retrying automatically.",
        WebSocketException => "X-Plane stopped sending telemetry. Retrying automatically.",
        _ => "Start X-Plane 12.1.1 or newer; connection will retry automatically."
    };

    private static int ParseApiVersion(string version) =>
        int.TryParse(version.TrimStart('v', 'V'), out var parsed) ? parsed : 0;

    private static int SanitizePort(int port) => port is >= 1 and <= 65_535 ? port : 8086;

    private sealed record XPlaneCapabilities(string ApiVersion, string SimulatorVersion);

    private sealed record XPlaneDataref(long Id, string Name, string ValueType);

    private sealed record XPlaneWriteTarget(XPlaneDataref Dataref, int? Index);

    private sealed class SignalHistory
    {
        public int ChangeCount { get; private set; }

        public DateTimeOffset LastChangedAt { get; private set; }

        public void ObserveChange(DateTimeOffset changedAt)
        {
            ChangeCount++;
            LastChangedAt = changedAt;
        }
    }

    private sealed class XPlaneBridgeException(BridgeConnectionState state, string message) : Exception(message)
    {
        public BridgeConnectionState State { get; } = state;
    }

    private readonly record struct XPlaneValue(double Scalar, double[] Array, string Text)
    {
        public bool EquivalentTo(XPlaneValue other) =>
            Math.Abs(Scalar - other.Scalar) < 0.0001d &&
            string.Equals(Text, other.Text, StringComparison.Ordinal) &&
            Array.AsSpan().SequenceEqual(other.Array);

        public static XPlaneValue Parse(JsonElement value, string valueType)
        {
            if (value.ValueKind == JsonValueKind.Number)
            {
                return new XPlaneValue(value.GetDouble(), [], string.Empty);
            }

            if (value.ValueKind == JsonValueKind.Array)
            {
                var array = value.EnumerateArray()
                    .Where(item => item.ValueKind == JsonValueKind.Number)
                    .Select(item => item.GetDouble())
                    .ToArray();
                return new XPlaneValue(array.FirstOrDefault(), array, string.Empty);
            }

            if (value.ValueKind == JsonValueKind.String)
            {
                var encoded = value.GetString() ?? string.Empty;
                var text = valueType == "data" ? DecodeDatarefText(encoded) : encoded;
                return new XPlaneValue(0d, [], text);
            }

            return new XPlaneValue(0d, [], string.Empty);
        }

        private static string DecodeDatarefText(string encoded)
        {
            try
            {
                return Encoding.UTF8.GetString(Convert.FromBase64String(encoded)).Trim('\0', ' ', '\r', '\n', '\t');
            }
            catch (FormatException)
            {
                return encoded.Trim('\0', ' ', '\r', '\n', '\t');
            }
        }
    }
}
