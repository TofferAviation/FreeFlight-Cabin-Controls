using System.IO;
using System.Text.Json;

namespace FreeFlight.CabinControl.App.Services;

/// <summary>
/// Small, local journal for ACARS points which have not yet been acknowledged
/// by BAV. It is intentionally separate from the passenger-session snapshot:
/// restarting Ember must not turn a network interruption into a lost flight.
/// </summary>
public sealed record PendingAcarsTelemetryRecovery(
    FleetAcarsTelemetryDto Telemetry,
    DateTimeOffset CapturedAt);

public sealed record AcarsTelemetryRecoverySnapshot(
    string SessionId,
    IReadOnlyList<PendingAcarsTelemetryRecovery> PendingTelemetry,
    DateTimeOffset SavedAt);

public sealed class AcarsTelemetryRecoveryStore
{
    private const int MaximumPendingPoints = 17_280; // 24 hours at one point per five seconds.
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _filePath;
    private readonly FileLogService _log;

    public AcarsTelemetryRecoveryStore(string filePath, FileLogService log)
    {
        _filePath = Path.GetFullPath(filePath);
        _log = log;
    }

    public AcarsTelemetryRecoverySnapshot? Load()
    {
        try
        {
            return File.Exists(_filePath)
                ? JsonSerializer.Deserialize<AcarsTelemetryRecoverySnapshot>(File.ReadAllText(_filePath), SerializerOptions)
                : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            _log.Error("The pending ACARS telemetry journal could not be restored.", exception);
            return null;
        }
    }

    public void Save(string sessionId, IReadOnlyList<PendingAcarsTelemetryRecovery> pendingTelemetry)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        try
        {
            var retained = pendingTelemetry.TakeLast(MaximumPendingPoints).ToArray();
            var directory = Path.GetDirectoryName(_filePath)!;
            Directory.CreateDirectory(directory);
            var temporaryPath = Path.Combine(directory, $"acars-recovery-{Guid.NewGuid():N}.tmp");
            var snapshot = new AcarsTelemetryRecoverySnapshot(sessionId, retained, DateTimeOffset.UtcNow);
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(snapshot, SerializerOptions));
            File.Move(temporaryPath, _filePath, true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _log.Error("The pending ACARS telemetry journal could not be saved.", exception);
        }
    }

    public void Clear()
    {
        try
        {
            if (File.Exists(_filePath)) File.Delete(_filePath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _log.Error("The pending ACARS telemetry journal could not be cleared.", exception);
        }
    }
}
