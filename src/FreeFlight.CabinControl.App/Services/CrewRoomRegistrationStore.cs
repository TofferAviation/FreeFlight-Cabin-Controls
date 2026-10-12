using System.IO;
using System.Linq;
using System.Text.Json;

namespace FreeFlight.CabinControl.App.Services;

/// <summary>
/// Stores a pilot's Crew Room follows on this Windows profile. Event registration
/// remains authoritative on the BAV website; this is deliberately only a local
/// convenience list for the desktop application.
/// </summary>
public sealed class CrewRoomRegistrationStore
{
    private const int CurrentSchemaVersion = 1;
    private readonly string _path;

    public CrewRoomRegistrationStore(string settingsDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsDirectory);
        _path = Path.Combine(settingsDirectory, "crew-room-follows.json");
    }

    public IReadOnlySet<string> Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return new HashSet<string>(StringComparer.Ordinal);
            }

            var json = File.ReadAllText(_path);
            var persisted = JsonSerializer.Deserialize<PersistedFollows>(json);
            if (persisted?.SchemaVersion != CurrentSchemaVersion || persisted.EventIds is null)
            {
                return new HashSet<string>(StringComparer.Ordinal);
            }

            return persisted.EventIds
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToHashSet(StringComparer.Ordinal);
        }
        catch (IOException)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }
        catch (UnauthorizedAccessException)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }
    }

    public bool TrySave(IEnumerable<string> eventIds)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var ids = eventIds
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray();
            var body = JsonSerializer.Serialize(new PersistedFollows(CurrentSchemaVersion, ids));
            var temporaryPath = _path + ".tmp";
            File.WriteAllText(temporaryPath, body);
            File.Move(temporaryPath, _path, true);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private sealed record PersistedFollows(int SchemaVersion, IReadOnlyList<string> EventIds);
}
