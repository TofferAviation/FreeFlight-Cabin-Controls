using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace FreeFlight.CabinControl.Core.Integration;

/// <summary>
/// A Mac/Linux-safe simulator bridge that receives MSFS telemetry from a
/// separately launched Windows Ember relay. It does not load SimConnect and
/// deliberately retains no pairing code or access token after process exit.
/// </summary>
public sealed class MsfsRelayBridgeService : ISimulatorBridge
{
    private readonly CancellationTokenSource _lifetime = new();
    private HttpClient? _client;
    private Task? _pollingTask;
    private BridgeStatus _currentStatus = new(
        BridgeConnectionState.Disconnected,
        "MSFS relay not connected",
        "No aircraft detected",
        "Pair with your private Windows Ember relay to view MSFS telemetry.");
    private long _lastFrameUtcTicks;
    private bool _disposed;

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
        // Pairing is explicit so a client never scans or contacts another
        // device in the background.
    }

    public async Task ConnectAsync(
        Uri relayUri,
        string certificateFingerprint,
        string pairingCode,
        string clientName,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(relayUri);
        if (!string.Equals(relayUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The MSFS relay must use HTTPS.", nameof(relayUri));
        }

        var expectedFingerprint = NormalizeFingerprint(certificateFingerprint);
        if (string.IsNullOrWhiteSpace(pairingCode))
        {
            throw new ArgumentException("Enter the pairing code shown by the Windows relay.", nameof(pairingCode));
        }

        var client = CreatePinnedClient(expectedFingerprint);
        try
        {
            var pairUri = new Uri(relayUri, MsfsRelayProtocol.PairPath);
            using var response = await client.PostAsJsonAsync(
                pairUri,
                new RemoteMsfsRelayPairRequest(pairingCode.Trim(), NormalizeClientName(clientName)),
                cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    response.StatusCode == System.Net.HttpStatusCode.Unauthorized
                        ? "The pairing code was not accepted by the Windows relay."
                        : "The Windows relay did not accept this pairing request.");
            }

            var pairing = await response.Content.ReadFromJsonAsync<RemoteMsfsRelayPairResponse>(cancellationToken)
                .ConfigureAwait(false);
            if (pairing is null || string.IsNullOrWhiteSpace(pairing.AccessToken) || pairing.ExpiresAt <= DateTimeOffset.UtcNow)
            {
                throw new InvalidOperationException("The Windows relay returned an invalid pairing response.");
            }

            await ReplaceConnectionAsync(client, relayUri, pairing, cancellationToken).ConfigureAwait(false);
            client = null;
        }
        finally
        {
            client?.Dispose();
        }
    }

    public void RequestReconnect()
    {
        // The relay poll loop retries transient network failures. Re-pairing is
        // intentionally never attempted without the pilot entering a new code.
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _lifetime.Cancel();
        _client?.Dispose();
        _lifetime.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task ReplaceConnectionAsync(
        HttpClient client,
        Uri relayUri,
        RemoteMsfsRelayPairResponse pairing,
        CancellationToken cancellationToken)
    {
        if (_pollingTask is not null)
        {
            throw new InvalidOperationException("This Ember session is already paired with an MSFS relay.");
        }

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", pairing.AccessToken);
        _client = client;
        PublishStatus(new BridgeStatus(
            BridgeConnectionState.Connecting,
            "MSFS relay paired",
            "Waiting for telemetry",
            $"Secure private relay paired until {pairing.ExpiresAt.LocalDateTime:t}."));
        _pollingTask = Task.Run(() => PollAsync(client, new Uri(relayUri, MsfsRelayProtocol.StatePath), cancellationToken));
        await Task.Yield();
    }

    private async Task PollAsync(HttpClient client, Uri stateUri, CancellationToken connectCancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, connectCancellationToken);
        var cancellationToken = linked.Token;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var state = await client.GetFromJsonAsync<RemoteMsfsRelayState>(stateUri, cancellationToken)
                        .ConfigureAwait(false);
                    if (state is null)
                    {
                        throw new InvalidOperationException("The relay returned an empty state.");
                    }

                    var remote = state.Status;
                    PublishStatus(remote with
                    {
                        Detail = $"Private MSFS relay · {remote.Detail}"
                    });
                    if (state.Telemetry is { } telemetry)
                    {
                        Interlocked.Exchange(ref _lastFrameUtcTicks, telemetry.Timestamp.UtcTicks);
                        TelemetryReceived?.Invoke(telemetry);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException or System.Text.Json.JsonException)
                {
                    PublishStatus(new BridgeStatus(
                        BridgeConnectionState.Disconnected,
                        "MSFS relay disconnected",
                        "No aircraft telemetry",
                        "The private relay could not be reached. Keep the Windows relay open and pair again if its code has changed."));
                    break;
                }

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
        finally
        {
            if (ReferenceEquals(_client, client))
            {
                _client = null;
                client.Dispose();
            }

            _pollingTask = null;
        }
    }

    private static HttpClient CreatePinnedClient(string expectedFingerprint)
    {
        var expectedBytes = Convert.FromHexString(expectedFingerprint);
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, certificate, _, _) =>
            {
                if (certificate is null)
                {
                    return false;
                }

                var actual = SHA256.HashData(certificate.GetRawCertData());
                return CryptographicOperations.FixedTimeEquals(actual, expectedBytes);
            }
        };
        return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(12) };
    }

    private static string NormalizeFingerprint(string value)
    {
        var normalized = value.Replace(":", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Trim();
        if (normalized.Length != 64 || normalized.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException("Enter the relay's 64-character SHA-256 certificate fingerprint.", nameof(value));
        }

        return normalized.ToUpperInvariant();
    }

    private static string NormalizeClientName(string value)
    {
        var normalized = value.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? "Ember desktop client" : normalized[..Math.Min(normalized.Length, 80)];
    }

    private void PublishStatus(BridgeStatus status)
    {
        if (status == _currentStatus)
        {
            return;
        }

        _currentStatus = status;
        StatusChanged?.Invoke(status);
    }
}
