using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using FreeFlight.CabinControl.App.Services;
using FreeFlight.CabinControl.Core.Configuration;
using FreeFlight.CabinControl.Core.Integration;

var options = RelayOptions.Parse(args);
if (!options.AllowLan)
{
    Console.Error.WriteLine("Ember MSFS Relay will not open a network listener without --allow-lan.");
    Console.Error.WriteLine("Example: Ember.MsfsRelay.exe --allow-lan --port 52187");
    return;
}

using var certificate = RelayCertificate.Create();
var fingerprint = Convert.ToHexString(SHA256.HashData(certificate.Export(X509ContentType.Cert)));
var settingsDirectory = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "FreeFlight",
    "EmberMsfsRelay");
var log = new FileLogService(Path.Combine(settingsDirectory, "logs"));
var settings = new AppSettings { Msfs2024AutoConnect = true, PreferredSimulator = "MSFS 2024" };
using var simulator = new Msfs2024SimConnectBridgeService(settings, log);
var state = new RelayState(fingerprint, simulator.CurrentStatus);
simulator.StatusChanged += state.UpdateStatus;
simulator.TelemetryReceived += state.UpdateTelemetry;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(server =>
    server.Listen(IPAddress.Any, options.Port, listener => listener.UseHttps(certificate)));
var app = builder.Build();

app.MapGet(MsfsRelayProtocol.InfoPath, () => Results.Ok(state.GetInfo(Environment.MachineName)));
app.MapPost(MsfsRelayProtocol.PairPath, (RemoteMsfsRelayPairRequest request) =>
    state.TryPair(request, out var response)
        ? Results.Ok(response)
        : Results.Unauthorized());
app.MapGet(MsfsRelayProtocol.StatePath, (HttpRequest request) =>
    state.IsAuthorized(request)
        ? Results.Ok(state.GetState())
        : Results.Unauthorized());

app.Lifetime.ApplicationStopping.Register(() =>
{
    simulator.StatusChanged -= state.UpdateStatus;
    simulator.TelemetryReceived -= state.UpdateTelemetry;
    log.Information("Ember MSFS Relay stopped.");
});

Console.WriteLine("Ember MSFS Relay is ready for a private LAN pairing.");
Console.WriteLine($"Address: https://{Environment.MachineName}.local:{options.Port}");
foreach (var address in RelayAddressHelper.GetPrivateIpv4Addresses())
{
    Console.WriteLine($"Address: https://{address}:{options.Port}");
}
Console.WriteLine($"Certificate fingerprint: {fingerprint}");
Console.WriteLine($"Pairing code: {state.PairingCode}");
Console.WriteLine("The pairing code expires when this relay closes. Do not share it publicly.");
log.Information($"Ember MSFS Relay started on HTTPS port {options.Port}.");
simulator.Start();
await app.RunAsync();

internal sealed record RelayOptions(int Port, bool AllowLan)
{
    public static RelayOptions Parse(string[] args)
    {
        var port = 52187;
        for (var index = 0; index < args.Length; index++)
        {
            if (!string.Equals(args[index], "--port", StringComparison.OrdinalIgnoreCase) || index + 1 >= args.Length)
            {
                continue;
            }

            if (!int.TryParse(args[++index], out port) || port is < 1024 or > 65535)
            {
                throw new ArgumentException("--port must be a number from 1024 to 65535.");
            }
        }

        return new RelayOptions(port, args.Any(argument => string.Equals(argument, "--allow-lan", StringComparison.OrdinalIgnoreCase)));
    }
}

internal sealed class RelayState
{
    private readonly object _sync = new();
    private readonly byte[] _pairingCodeBytes;
    private readonly ConcurrentDictionary<string, DateTimeOffset> _accessTokens = new(StringComparer.Ordinal);
    private BridgeStatus _status;
    private CabinTelemetrySnapshot? _telemetry;
    private DateTimeOffset _updatedAt = DateTimeOffset.UtcNow;

    public RelayState(string certificateFingerprint, BridgeStatus status)
    {
        CertificateFingerprint = certificateFingerprint;
        PairingCode = RandomNumberGenerator.GetInt32(100000, 1000000).ToString(System.Globalization.CultureInfo.InvariantCulture);
        _pairingCodeBytes = Encoding.UTF8.GetBytes(PairingCode);
        _status = status;
    }

    public string CertificateFingerprint { get; }

    public string PairingCode { get; }

    public void UpdateStatus(BridgeStatus status)
    {
        lock (_sync)
        {
            _status = status;
            _updatedAt = DateTimeOffset.UtcNow;
        }
    }

    public void UpdateTelemetry(CabinTelemetrySnapshot telemetry)
    {
        lock (_sync)
        {
            _telemetry = telemetry;
            _updatedAt = DateTimeOffset.UtcNow;
        }
    }

    public bool TryPair(RemoteMsfsRelayPairRequest request, out RemoteMsfsRelayPairResponse? response)
    {
        response = null;
        var supplied = Encoding.UTF8.GetBytes(request.PairingCode?.Trim() ?? string.Empty);
        if (supplied.Length != _pairingCodeBytes.Length || !CryptographicOperations.FixedTimeEquals(supplied, _pairingCodeBytes))
        {
            return false;
        }

        var accessToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var expiresAt = DateTimeOffset.UtcNow.AddHours(8);
        _accessTokens[accessToken] = expiresAt;
        response = new RemoteMsfsRelayPairResponse(accessToken, expiresAt);
        return true;
    }

    public bool IsAuthorized(HttpRequest request)
    {
        var header = request.Headers.Authorization.ToString();
        const string bearerPrefix = "Bearer ";
        if (!header.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var token = header[bearerPrefix.Length..].Trim();
        if (!_accessTokens.TryGetValue(token, out var expiresAt))
        {
            return false;
        }

        if (expiresAt > DateTimeOffset.UtcNow)
        {
            return true;
        }

        _accessTokens.TryRemove(token, out _);
        return false;
    }

    public RemoteMsfsRelayInfo GetInfo(string relayName) => new(
        MsfsRelayProtocol.CurrentVersion,
        relayName,
        CertificateFingerprint,
        _updatedAt);

    public RemoteMsfsRelayState GetState()
    {
        lock (_sync)
        {
            return new RemoteMsfsRelayState(_status, _telemetry, _updatedAt);
        }
    }
}

internal static class RelayCertificate
{
    public static X509Certificate2 Create()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=Ember MSFS Relay",
            key,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, false));
        var enhancedKeyUsages = new OidCollection
        {
            new Oid("1.3.6.1.5.5.7.3.1") // TLS server authentication
        };
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(enhancedKeyUsages, false));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        var subjectAlternativeNames = new SubjectAlternativeNameBuilder();
        subjectAlternativeNames.AddDnsName("localhost");
        subjectAlternativeNames.AddDnsName(Environment.MachineName);
        subjectAlternativeNames.AddDnsName($"{Environment.MachineName}.local");
        subjectAlternativeNames.AddIpAddress(IPAddress.Loopback);
        subjectAlternativeNames.AddIpAddress(IPAddress.IPv6Loopback);
        request.CertificateExtensions.Add(subjectAlternativeNames.Build());
        using var generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(1));
        return X509CertificateLoader.LoadPkcs12(
            generated.Export(X509ContentType.Pkcs12),
            password: null,
            X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.Exportable);
    }
}

internal static class RelayAddressHelper
{
    public static IEnumerable<IPAddress> GetPrivateIpv4Addresses() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(network => network.OperationalStatus == OperationalStatus.Up &&
                              network.NetworkInterfaceType is not NetworkInterfaceType.Loopback and not NetworkInterfaceType.Tunnel)
            .SelectMany(network => network.GetIPProperties().UnicastAddresses)
            .Select(address => address.Address)
            .Where(address => address.AddressFamily == AddressFamily.InterNetwork &&
                              !IPAddress.IsLoopback(address) &&
                              !address.ToString().StartsWith("169.254.", StringComparison.Ordinal))
            .Distinct();
}
