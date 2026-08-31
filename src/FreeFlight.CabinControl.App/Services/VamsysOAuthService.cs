using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FreeFlight.CabinControl.Core.Configuration;
using Microsoft.Win32;

namespace FreeFlight.CabinControl.App.Services;

public sealed record VamsysPilotProfile(
    long UserId,
    long PilotId,
    long AirlineId,
    string FirstName,
    string LastName,
    string Email,
    string PilotUsername,
    string RankName,
    string AirlineName,
    string AirlineIcao)
{
    public string DisplayName => string.Join(' ', new[] { FirstName, LastName }
        .Where(part => !string.IsNullOrWhiteSpace(part)));
}

public interface IVamsysOAuthService
{
    bool IsConfigured { get; }

    Task BeginAuthorizationAsync(CancellationToken cancellationToken = default);

    Task HandleAuthorizationCallbackAsync(string callbackUri, CancellationToken cancellationToken = default);

    Task<VamsysPilotProfile?> TryGetPilotProfileAsync(CancellationToken cancellationToken = default);

    Task DisconnectAsync(CancellationToken cancellationToken = default);
}

public sealed class VamsysOAuthService : IVamsysOAuthService, IDisposable
{
    public const string DefaultRedirectUri = "freeflight-cabin-control://oauth/vamsys";
    public const string AccountPortalUrl = "https://auth.vamsys.io";
    private const string AuthorizationEndpoint = "https://vamsys.io/oauth/authorize";
    private const string TokenEndpoint = "https://vamsys.io/oauth/token";
    private const string PilotApiBase = "https://vamsys.io/api/v3/pilot/";
    private const string RequestedScopes = "identity:basic pilot:read";
    private readonly AppSettings _settings;
    private readonly string _tokenPath;
    private readonly string _pendingPath;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly Action<Uri> _browserLauncher;
    private readonly Action _protocolRegistrar;
    private readonly Func<byte[], byte[]> _protectData;
    private readonly Func<byte[], byte[]> _unprotectData;

    public VamsysOAuthService(
        AppSettings settings,
        string settingsDirectory,
        HttpClient? httpClient = null,
        Action<Uri>? browserLauncher = null,
        Action? protocolRegistrar = null,
        Func<byte[], byte[]>? protectData = null,
        Func<byte[], byte[]>? unprotectData = null)
    {
        _settings = settings;
        Directory.CreateDirectory(settingsDirectory);
        _tokenPath = Path.Combine(settingsDirectory, "vamsys-oauth.dat");
        _pendingPath = Path.Combine(settingsDirectory, "vamsys-oauth-pending.dat");
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        _ownsHttpClient = httpClient is null;
        _browserLauncher = browserLauncher ?? (uri =>
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }));
        _protocolRegistrar = protocolRegistrar ?? RegisterCallbackProtocol;
        _protectData = protectData ?? (data => ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser));
        _unprotectData = unprotectData ?? (data => ProtectedData.Unprotect(data, null, DataProtectionScope.CurrentUser));
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public bool IsConfigured => long.TryParse(_settings.VamsysClientId, out var clientId) && clientId > 0
                                && !string.IsNullOrWhiteSpace(_settings.VamsysAirlineName)
                                && !string.IsNullOrWhiteSpace(_settings.VamsysAirlineIcao);

    public Task BeginAuthorizationAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsConfigured)
        {
            throw new InvalidOperationException("Complete the vAMSYS Pilot API client setup before connecting.");
        }

        _protocolRegistrar();
        var verifier = CreateRandomBase64Url(64);
        var challenge = Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var state = CreateRandomBase64Url(32);
        var pending = new PendingAuthorization(state, verifier, DateTimeOffset.UtcNow.AddMinutes(10));
        WriteProtectedJson(_pendingPath, pending);

        var redirectUri = GetRedirectUri();
        var authorizationUrl = AuthorizationEndpoint + "?" + string.Join("&", new Dictionary<string, string>
        {
            ["client_id"] = _settings.VamsysClientId,
            ["redirect_uri"] = redirectUri,
            ["response_type"] = "code",
            ["scope"] = RequestedScopes,
            ["state"] = state,
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256"
        }.Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));

        _browserLauncher(new Uri(authorizationUrl, UriKind.Absolute));
        return Task.CompletedTask;
    }

    public async Task HandleAuthorizationCallbackAsync(
        string callbackUri,
        CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(callbackUri, UriKind.Absolute, out var callback) ||
            !string.Equals(callback.Scheme, "freeflight-cabin-control", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The vAMSYS callback address was not valid.");
        }

        var query = ParseQuery(callback.Query);
        if (query.TryGetValue("error", out var oauthError))
        {
            throw new InvalidOperationException(query.GetValueOrDefault("error_description") ?? oauthError);
        }

        var code = query.GetValueOrDefault("code");
        var returnedState = query.GetValueOrDefault("state");
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(returnedState))
        {
            throw new InvalidOperationException("vAMSYS did not return an authorization code and state.");
        }

        var pending = ReadProtectedJson<PendingAuthorization>(_pendingPath)
            ?? throw new InvalidOperationException("The vAMSYS authorization request is no longer available. Start the connection again.");
        if (pending.ExpiresAtUtc <= DateTimeOffset.UtcNow)
        {
            DeleteIfPresent(_pendingPath);
            throw new InvalidOperationException("The vAMSYS authorization request expired. Start the connection again.");
        }

        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(pending.State),
                Encoding.UTF8.GetBytes(returnedState)))
        {
            throw new InvalidOperationException("The vAMSYS authorization state did not match. No account data was accepted.");
        }

        using var response = await _httpClient.PostAsync(TokenEndpoint, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = _settings.VamsysClientId,
            ["redirect_uri"] = GetRedirectUri(),
            ["code"] = code,
            ["code_verifier"] = pending.CodeVerifier
        }), cancellationToken).ConfigureAwait(false);
        var tokens = await ReadTokenResponseAsync(response, cancellationToken).ConfigureAwait(false);
        WriteProtectedJson(_tokenPath, tokens);
        DeleteIfPresent(_pendingPath);
    }

    public async Task<VamsysPilotProfile?> TryGetPilotProfileAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return null;
        }

        var tokens = ReadProtectedJson<TokenEnvelope>(_tokenPath);
        if (tokens is null)
        {
            return null;
        }

        if (tokens.ExpiresAtUtc <= DateTimeOffset.UtcNow.AddMinutes(2))
        {
            try
            {
                tokens = await RefreshAsync(tokens.RefreshToken, cancellationToken).ConfigureAwait(false);
                WriteProtectedJson(_tokenPath, tokens);
            }
            catch
            {
                DeleteIfPresent(_tokenPath);
                throw;
            }
        }

        try
        {
            return await FetchPilotProfileAsync(tokens.AccessToken, cancellationToken).ConfigureAwait(false);
        }
        catch (VamsysUnauthorizedException)
        {
            tokens = await RefreshAsync(tokens.RefreshToken, cancellationToken).ConfigureAwait(false);
            WriteProtectedJson(_tokenPath, tokens);
            try
            {
                return await FetchPilotProfileAsync(tokens.AccessToken, cancellationToken).ConfigureAwait(false);
            }
            catch (VamsysUnauthorizedException)
            {
                DeleteIfPresent(_tokenPath);
                throw new InvalidOperationException("vAMSYS access was revoked. Connect the account again.");
            }
        }
    }

    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DeleteIfPresent(_tokenPath);
        DeleteIfPresent(_pendingPath);
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    private async Task<TokenEnvelope> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsync(TokenEndpoint, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = _settings.VamsysClientId,
            ["refresh_token"] = refreshToken,
            ["scope"] = RequestedScopes
        }), cancellationToken).ConfigureAwait(false);
        return await ReadTokenResponseAsync(response, cancellationToken).ConfigureAwait(false);
    }

    private async Task<JsonDocument> GetApiDocumentAsync(
        string relativePath,
        string accessToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, PilotApiBase + relativePath);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new VamsysUnauthorizedException();
        }

        var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"vAMSYS returned {(int)response.StatusCode}: {ReadApiError(content)}");
        }

        return JsonDocument.Parse(content);
    }

    private async Task<VamsysPilotProfile> FetchPilotProfileAsync(
        string accessToken,
        CancellationToken cancellationToken)
    {
        using var user = await GetApiDocumentAsync("user", accessToken, cancellationToken).ConfigureAwait(false);
        using var pilot = await GetApiDocumentAsync("profile", accessToken, cancellationToken).ConfigureAwait(false);
        var userData = user.RootElement.GetProperty("data");
        var pilotData = pilot.RootElement.GetProperty("data");
        var rankName = pilotData.TryGetProperty("rank", out var rank) && rank.ValueKind == JsonValueKind.Object
            ? ReadString(rank, "name")
            : string.Empty;
        return new VamsysPilotProfile(
            ReadInt64(userData, "id"),
            ReadInt64(pilotData, "id"),
            ReadInt64(pilotData, "airline_id"),
            ReadString(userData, "first_name"),
            ReadString(userData, "last_name"),
            ReadString(userData, "email"),
            ReadString(pilotData, "username"),
            rankName,
            _settings.VamsysAirlineName.Trim(),
            _settings.VamsysAirlineIcao.Trim().ToUpperInvariant());
    }

    private static async Task<TokenEnvelope> ReadTokenResponseAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"vAMSYS authorization failed: {ReadApiError(content)}");
        }

        using var document = JsonDocument.Parse(content);
        var root = document.RootElement;
        var expiresIn = root.GetProperty("expires_in").GetInt32();
        return new TokenEnvelope(
            root.GetProperty("access_token").GetString() ?? string.Empty,
            root.GetProperty("refresh_token").GetString() ?? string.Empty,
            DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, expiresIn)));
    }

    private void RegisterCallbackProtocol()
    {
        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            throw new InvalidOperationException("FreeFlight could not determine its executable path for the vAMSYS callback.");
        }

        using var protocol = Registry.CurrentUser.CreateSubKey(@"Software\Classes\freeflight-cabin-control");
        protocol?.SetValue(string.Empty, "URL:FreeFlight Cabin Control vAMSYS Callback");
        protocol?.SetValue("URL Protocol", string.Empty);
        using var command = protocol?.CreateSubKey(@"shell\open\command");
        command?.SetValue(string.Empty, $"\"{executablePath}\" \"%1\"");
    }

    private string GetRedirectUri() => string.IsNullOrWhiteSpace(_settings.VamsysRedirectUri)
        ? DefaultRedirectUri
        : _settings.VamsysRedirectUri.Trim();

    private static Dictionary<string, string> ParseQuery(string query) => query
        .TrimStart('?')
        .Split('&', StringSplitOptions.RemoveEmptyEntries)
        .Select(part => part.Split('=', 2))
        .ToDictionary(
            pair => Uri.UnescapeDataString(pair[0].Replace('+', ' ')),
            pair => pair.Length > 1 ? Uri.UnescapeDataString(pair[1].Replace('+', ' ')) : string.Empty,
            StringComparer.OrdinalIgnoreCase);

    private static string CreateRandomBase64Url(int byteCount)
    {
        var bytes = RandomNumberGenerator.GetBytes(byteCount);
        return Base64UrlEncode(bytes);
    }

    private static string Base64UrlEncode(byte[] bytes) => Convert.ToBase64String(bytes)
        .TrimEnd('=')
        .Replace('+', '-')
        .Replace('/', '_');

    private void WriteProtectedJson<T>(string path, T value)
    {
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(value);
        var protectedBytes = _protectData(plaintext);
        var temporaryPath = path + ".tmp";
        File.WriteAllBytes(temporaryPath, protectedBytes);
        File.Move(temporaryPath, path, true);
    }

    private T? ReadProtectedJson<T>(string path)
    {
        if (!File.Exists(path))
        {
            return default;
        }

        try
        {
            var protectedBytes = File.ReadAllBytes(path);
            var plaintext = _unprotectData(protectedBytes);
            return JsonSerializer.Deserialize<T>(plaintext);
        }
        catch (Exception exception) when (exception is CryptographicException or IOException or JsonException)
        {
            DeleteIfPresent(path);
            return default;
        }
    }

    private static void DeleteIfPresent(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // A locked credential file is treated as unavailable and retried on the next application run.
        }
    }

    private static long ReadInt64(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.TryGetInt64(out var result) ? result : 0;

    private static string ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static string ReadApiError(string content)
    {
        try
        {
            using var document = JsonDocument.Parse(content);
            var root = document.RootElement;
            return ReadString(root, "error_description") is { Length: > 0 } description
                ? description
                : ReadString(root, "message") is { Length: > 0 } message
                    ? message
                    : ReadString(root, "error") is { Length: > 0 } error
                        ? error
                        : "Unknown API error";
        }
        catch (JsonException)
        {
            return string.IsNullOrWhiteSpace(content) ? "No response details were supplied" : content;
        }
    }

    private sealed record PendingAuthorization(string State, string CodeVerifier, DateTimeOffset ExpiresAtUtc);

    private sealed record TokenEnvelope(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAtUtc);

    private sealed class VamsysUnauthorizedException : Exception;
}
