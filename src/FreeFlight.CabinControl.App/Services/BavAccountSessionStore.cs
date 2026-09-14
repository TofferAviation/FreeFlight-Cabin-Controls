using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FreeFlight.CabinControl.App.Services;

/// <summary>
/// Persists a revocable BAV device credential for the current Windows user only.
/// The pilot's website password is never written to disk.
/// </summary>
public sealed class BavAccountSessionStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("FreeFlightLTD.ember.bav-account-session.v1");
    private readonly string _path;

    public BavAccountSessionStore(string settingsDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsDirectory);
        Directory.CreateDirectory(settingsDirectory);
        _path = Path.Combine(settingsDirectory, "bav-account-session.dat");
    }

    public FleetAccountSession? Load()
    {
        if (!File.Exists(_path))
        {
            return null;
        }

        try
        {
            var protectedBytes = File.ReadAllBytes(_path);
            var plaintext = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
            var stored = JsonSerializer.Deserialize<PersistedSession>(plaintext);
            if (stored is null || string.IsNullOrWhiteSpace(stored.DeviceSessionToken) || string.IsNullOrWhiteSpace(stored.PilotId))
            {
                Clear();
                return null;
            }

            return new FleetAccountSession(string.Empty, stored.PilotId, stored.PilotNumber, stored.Name, stored.Email, DeviceSessionToken: stored.DeviceSessionToken);
        }
        catch (Exception exception) when (exception is CryptographicException or IOException or JsonException or UnauthorizedAccessException)
        {
            Clear();
            return null;
        }
    }

    public bool TrySave(FleetAccountSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (string.IsNullOrWhiteSpace(session.DeviceSessionToken)) return false;
        try
        {
            var stored = new PersistedSession(session.DeviceSessionToken, session.PilotId, session.PilotNumber, session.Name, session.Email);
            var plaintext = JsonSerializer.SerializeToUtf8Bytes(stored);
            var protectedBytes = ProtectedData.Protect(plaintext, Entropy, DataProtectionScope.CurrentUser);
            var temporaryPath = _path + ".tmp";
            File.WriteAllBytes(temporaryPath, protectedBytes);
            File.Move(temporaryPath, _path, true);
            return true;
        }
        catch (Exception exception) when (exception is CryptographicException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public void Clear()
    {
        try
        {
            File.Delete(_path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A locked credential file is unavailable until the next app run.
        }
    }

    private sealed record PersistedSession(string DeviceSessionToken, string PilotId, string PilotNumber, string Name, string Email);
}
