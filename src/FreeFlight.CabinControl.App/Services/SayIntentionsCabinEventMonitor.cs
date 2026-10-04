using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows.Threading;
using FreeFlight.CabinControl.Core.Configuration;
using FreeFlight.CabinControl.Core.Operations;
using FreeFlight.CabinControl.Core.Persistence;

namespace FreeFlight.CabinControl.App.Services;

/// <summary>
/// Watches the pilot's local SayIntentions session. The personal API key is
/// read only from the active localhost flight payload, used in memory for the
/// current request, and is never persisted, logged, or sent to BAV.
/// </summary>
public sealed class SayIntentionsCabinEventMonitor : IDisposable
{
    private static readonly Uri LocalFlightUri = new("http://127.0.0.1:43117/flightJSON");
    private static readonly Uri CommsBaseUri = new("https://apipri.sayintentions.ai/sapi/getCommsHistory");
    private readonly AppSettings _settings;
    private readonly ISettingsStore _settingsStore;
    private readonly HttpClient _httpClient;
    private readonly DispatcherTimer _pollTimer;
    private readonly SemaphoreSlim _pollGate = new(1, 1);
    private readonly HashSet<string> _processedEventIds;
    private bool _disposed;
    private string _status = "SayIntentions cabin-event sync is off.";

    public SayIntentionsCabinEventMonitor(
        AppSettings settings,
        ISettingsStore settingsStore,
        HttpClient? httpClient = null)
    {
        _settings = settings;
        _settingsStore = settingsStore;
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        _processedEventIds = new HashSet<string>(settings.ProcessedSayIntentionsCabinEventIds ?? [], StringComparer.Ordinal);
        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        _pollTimer.Tick += HandlePollTimerTick;
    }

    public event Func<SayIntentionsFleetDefectProposal, Task<bool>>? TechnicalDefectDetected;

    public event EventHandler<string>? StatusChanged;

    public string Status => _status;

    public void Start()
    {
        ThrowIfDisposed();
        _pollTimer.Start();
        _ = PollAsync();
    }

    private async void HandlePollTimerTick(object? sender, EventArgs e) => await PollAsync();

    private async Task PollAsync()
    {
        if (!_settings.SayIntentionsCabinEventSync)
        {
            SetStatus("SayIntentions cabin-event sync is off.");
            return;
        }
        if (!await _pollGate.WaitAsync(0))
        {
            return;
        }

        try
        {
            var flightJson = await _httpClient.GetStringAsync(LocalFlightUri);
            using var flightDocument = JsonDocument.Parse(flightJson);
            var flight = ResolveFlightDetails(flightDocument.RootElement);
            var apiKey = ReadString(flight, "api_key");
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                SetStatus("Open SayIntentions and start a flight to connect cabin-event sync.");
                return;
            }

            var endpoint = new UriBuilder(CommsBaseUri)
            {
                Query = $"api_key={Uri.EscapeDataString(apiKey)}"
            }.Uri;
            var commsJson = await _httpClient.GetStringAsync(endpoint);
            using var commsDocument = JsonDocument.Parse(commsJson);
            var proposals = ExtractEvents(commsDocument.RootElement)
                .Where(item => !_processedEventIds.Contains(item.EventId))
                .Select(item => SayIntentionsCabinEventClassifier.TryCreateProposal(item.EventId, item.OccurredAt, item.Transcript, out var proposal)
                    ? proposal
                    : null)
                .OfType<SayIntentionsFleetDefectProposal>()
                .ToList();

            if (proposals.Count == 0)
            {
                SetStatus("Monitoring SayIntentions cabin events for clear technical defects.");
                return;
            }

            foreach (var proposal in proposals)
            {
                if (await PublishAsync(proposal))
                {
                    await MarkProcessedAsync(proposal.EventId);
                }
            }
        }
        catch (HttpRequestException)
        {
            SetStatus("SayIntentions is not available locally. Start an active SayIntentions flight to connect.");
        }
        catch (JsonException)
        {
            SetStatus("SayIntentions returned an unreadable flight or communications payload. No fleet record was created.");
        }
        catch (OperationCanceledException)
        {
            // Shutdown and transient timeouts are intentionally quiet.
        }
        finally
        {
            _pollGate.Release();
        }
    }

    private async Task<bool> PublishAsync(SayIntentionsFleetDefectProposal proposal)
    {
        var callbacks = TechnicalDefectDetected?.GetInvocationList()
            .Cast<Func<SayIntentionsFleetDefectProposal, Task<bool>>>()
            .ToArray() ?? [];
        if (callbacks.Length == 0)
        {
            SetStatus("SayIntentions detected a technical event, but Fleet is not ready to receive it.");
            return false;
        }

        foreach (var callback in callbacks)
        {
            if (await callback(proposal))
            {
                return true;
            }
        }
        return false;
    }

    private async Task MarkProcessedAsync(string eventId)
    {
        if (!_processedEventIds.Add(eventId))
        {
            return;
        }

        _settings.ProcessedSayIntentionsCabinEventIds ??= [];
        _settings.ProcessedSayIntentionsCabinEventIds.Add(eventId);
        if (_settings.ProcessedSayIntentionsCabinEventIds.Count > 250)
        {
            _settings.ProcessedSayIntentionsCabinEventIds.RemoveRange(0, _settings.ProcessedSayIntentionsCabinEventIds.Count - 250);
        }
        await _settingsStore.SaveAsync(_settings);
        SetStatus("A SayIntentions technical cabin event was filed against the reserved aircraft.");
    }

    private static JsonElement ResolveFlightDetails(JsonElement root) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty("flight_details", out var details) && details.ValueKind == JsonValueKind.Object
            ? details
            : root;

    private static IEnumerable<SayIntentionsCabinEvent> ExtractEvents(JsonElement root)
    {
        foreach (var entry in EnumerateCommunicationEntries(root))
        {
            var transcript = string.Join(" ", new[]
                {
                    ReadString(entry, "outgoing_message_english"),
                    ReadString(entry, "outgoing_message"),
                    ReadString(entry, "incoming_message_english"),
                    ReadString(entry, "incoming_message")
                }
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.Ordinal));
            if (string.IsNullOrWhiteSpace(transcript))
            {
                continue;
            }

            var occurredAt = ParseTimestamp(ReadString(entry, "stamp_zulu") ?? ReadString(entry, "timestamp"));
            var eventId = ReadString(entry, "id") ?? CreateStableEventId(occurredAt, transcript);
            yield return new SayIntentionsCabinEvent(eventId, occurredAt, transcript);
        }
    }

    private static IEnumerable<JsonElement> EnumerateCommunicationEntries(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in root.EnumerateArray()) yield return item;
            yield break;
        }
        if (root.ValueKind != JsonValueKind.Object)
        {
            yield break;
        }

        if (root.TryGetProperty("comm_history", out var history) && history.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in history.EnumerateArray()) yield return item;
            yield break;
        }
        if (root.TryGetProperty("data", out var data))
        {
            foreach (var item in EnumerateCommunicationEntries(data)) yield return item;
        }
    }

    private static string? ReadString(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value))
        {
            return null;
        }
        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
    }

    private static DateTimeOffset ParseTimestamp(string? value) =>
        DateTimeOffset.TryParse(value, out var parsed) ? parsed : DateTimeOffset.UtcNow;

    private static string CreateStableEventId(DateTimeOffset occurredAt, string transcript)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{occurredAt.UtcTicks}|{transcript}"));
        return Convert.ToHexString(bytes[..12]);
    }

    private void SetStatus(string value)
    {
        if (string.Equals(_status, value, StringComparison.Ordinal))
        {
            return;
        }
        _status = value;
        StatusChanged?.Invoke(this, value);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _pollTimer.Stop();
        _pollTimer.Tick -= HandlePollTimerTick;
        _httpClient.Dispose();
        _pollGate.Dispose();
    }

    private sealed record SayIntentionsCabinEvent(string EventId, DateTimeOffset OccurredAt, string Transcript);
}
