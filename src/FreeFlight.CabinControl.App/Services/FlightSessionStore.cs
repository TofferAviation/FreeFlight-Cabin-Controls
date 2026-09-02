using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using FreeFlight.CabinControl.Core.Passengers;

namespace FreeFlight.CabinControl.App.Services;

public sealed record FlightSessionSnapshot(
    DateTimeOffset SavedAt,
    string CabinLayoutProfileId,
    int BookedPassengerCount,
    bool HasSimBriefFlight,
    string SimBriefFlightSummary,
    string ImportedFlightNumber,
    string ImportedOrigin,
    string ImportedDestination,
    string ImportedAircraftIcao,
    DateTimeOffset? ImportedScheduledDepartureLocal,
    DateTimeOffset? LastSimBriefSyncTime,
    PassengerBoardingSession Boarding,
    DateTimeOffset? CrewRestCycleStartedAt = null,
    string LiveFlightPhase = "Preflight",
    DateTimeOffset? ImportedScheduledArrivalLocal = null);

public sealed class FlightSessionStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _filePath;
    private readonly FileLogService _log;

    public FlightSessionStore(string filePath, FileLogService log)
    {
        _filePath = Path.GetFullPath(filePath);
        _log = log;
    }

    public FlightSessionSnapshot? Load()
    {
        try
        {
            return File.Exists(_filePath)
                ? JsonSerializer.Deserialize<FlightSessionSnapshot>(File.ReadAllText(_filePath), SerializerOptions)
                : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            _log.Error("The saved flight session could not be restored.", exception);
            return null;
        }
    }

    public void SaveOrClear(FlightSessionSnapshot snapshot, bool flightCompleted)
    {
        try
        {
            if (flightCompleted || snapshot.BookedPassengerCount <= 0)
            {
                if (File.Exists(_filePath)) File.Delete(_filePath);
                return;
            }

            var directory = Path.GetDirectoryName(_filePath)!;
            Directory.CreateDirectory(directory);
            var temporaryPath = Path.Combine(directory, $"active-flight-{Guid.NewGuid():N}.tmp");
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(snapshot, SerializerOptions));
            File.Move(temporaryPath, _filePath, true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _log.Error("The active flight session could not be saved.", exception);
        }
    }

    public void Clear()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                File.Delete(_filePath);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _log.Error("The active flight session could not be cleared.", exception);
        }
    }
}
