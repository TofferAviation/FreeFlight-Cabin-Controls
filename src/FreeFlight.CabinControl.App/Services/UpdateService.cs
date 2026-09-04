using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FreeFlight.CabinControl.App.Services;

public sealed record ApplicationUpdate(
    Version Version,
    string Tag,
    string ReleaseNotes,
    Uri ReleasePage,
    string? AssetName,
    Uri? AssetDownload,
    string? AssetSha256 = null);

public sealed record UpdateCheckResult(
    ApplicationUpdate? LatestRelease,
    string FeedStatus);

public sealed class UpdateService
{
    public const string ReleasesPage = "https://github.com/TofferAviation/FreeFlight-Cabin-Controls/releases";
    private const string LatestReleaseApi = "https://api.github.com/repos/TofferAviation/FreeFlight-Cabin-Controls/releases/latest";
    private readonly HttpClient _httpClient;
    private readonly string _updatesDirectory;

    public UpdateService(string applicationDataDirectory)
    {
        _updatesDirectory = Path.Combine(applicationDataDirectory, "updates");
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd($"FreeFlight-Cabin-Control/{FormatVersion(CurrentVersion)}");
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        _httpClient.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
    }

    public Version CurrentVersion => NormalizeVersion(
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0, 0));

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseApi);
        request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return new UpdateCheckResult(
                null,
                "GitHub is connected, but no published release exists yet. Publish a GitHub Release to activate updates.");
        }

        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;
        var tag = root.GetProperty("tag_name").GetString() ?? string.Empty;
        var version = ParseReleaseVersion(tag);
        if (version is null)
        {
            return new UpdateCheckResult(
                null,
                $"GitHub release '{tag}' does not use a supported numeric version such as v0.3.1 or v0.5.0.1.");
        }

        var assets = root.TryGetProperty("assets", out var assetList)
            ? assetList.EnumerateArray().Select(asset => new
            {
                Name = asset.GetProperty("name").GetString() ?? string.Empty,
                Url = asset.GetProperty("browser_download_url").GetString() ?? string.Empty,
                Digest = asset.TryGetProperty("digest", out var digest) ? digest.GetString() : null
            }).ToArray()
            : [];
        var selectedAsset = assets
            .OrderByDescending(asset => asset.Name.Contains("win-x64", StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault(asset => asset.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
        var update = new ApplicationUpdate(
            NormalizeVersion(version),
            tag,
            root.TryGetProperty("body", out var body) ? body.GetString() ?? "No release notes supplied." : "No release notes supplied.",
            new Uri(root.GetProperty("html_url").GetString()!),
            selectedAsset?.Name,
            Uri.TryCreate(selectedAsset?.Url, UriKind.Absolute, out var assetUri) ? assetUri : null,
            NormalizeSha256(selectedAsset?.Digest));
        return new UpdateCheckResult(
            update,
            update.AssetDownload is null
                ? $"GitHub release {tag} was found, but it has no Windows ZIP package."
                : $"Connected to GitHub Releases · latest {tag}");
    }

    public async Task StageAndInstallAsync(ApplicationUpdate update, CancellationToken cancellationToken = default)
    {
        if (update.AssetDownload is null || string.IsNullOrWhiteSpace(update.AssetName))
        {
            throw new InvalidOperationException("This release does not contain a Windows ZIP package. Open the release page to install it manually.");
        }

        Directory.CreateDirectory(_updatesDirectory);
        var packagePath = Path.Combine(_updatesDirectory, update.AssetName);
        using (var response = await _httpClient.GetAsync(update.AssetDownload, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
        {
            response.EnsureSuccessStatusCode();
            await using var target = new FileStream(packagePath, FileMode.Create, FileAccess.Write, FileShare.None);
            await response.Content.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
        }

        if (!string.IsNullOrWhiteSpace(update.AssetSha256))
        {
            await using var package = File.OpenRead(packagePath);
            var actualHash = Convert.ToHexString(await SHA256.HashDataAsync(package, cancellationToken)).ToLowerInvariant();
            if (!string.Equals(actualHash, update.AssetSha256, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(packagePath);
                throw new InvalidDataException("The downloaded update did not match GitHub's SHA-256 checksum.");
            }
        }

        var stagingDirectory = Path.Combine(_updatesDirectory, $"stage-{FormatVersion(update.Version)}");
        if (Directory.Exists(stagingDirectory)) Directory.Delete(stagingDirectory, true);
        ZipFile.ExtractToDirectory(packagePath, stagingDirectory);
        var stagedExecutable = Directory.EnumerateFiles(stagingDirectory, "FreeFlight.CabinControl.exe", SearchOption.AllDirectories).FirstOrDefault();
        if (stagedExecutable is null)
        {
            throw new InvalidDataException("The downloaded package does not contain FreeFlight.CabinControl.exe.");
        }

        var payloadDirectory = Path.GetDirectoryName(stagedExecutable)!;
        var installedExecutable = Environment.ProcessPath ?? throw new InvalidOperationException("The current installation path is unavailable.");
        var installDirectory = Path.GetDirectoryName(installedExecutable)!;
        var helperPath = Path.Combine(_updatesDirectory, "apply-update.ps1");
        File.WriteAllText(helperPath, UpdateHelperScript);
        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(helperPath);
        startInfo.ArgumentList.Add("-ApplicationProcessId");
        startInfo.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("-SourceDirectory");
        startInfo.ArgumentList.Add(payloadDirectory);
        startInfo.ArgumentList.Add("-TargetDirectory");
        startInfo.ArgumentList.Add(installDirectory);
        startInfo.ArgumentList.Add("-ExecutablePath");
        startInfo.ArgumentList.Add(installedExecutable);
        _ = Process.Start(startInfo) ?? throw new InvalidOperationException("The update helper could not be started.");
    }

    public string ReadBundledChangelog()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "CHANGELOG.md");
        return File.Exists(path) ? File.ReadAllText(path) : "No bundled changelog is available.";
    }

    public static Version? ParseReleaseVersion(string? tag)
    {
        var match = Regex.Match(tag ?? string.Empty, @"(?<!\d)(?<major>\d+)\.(?<minor>\d+)\.(?<build>\d+)(?:\.(?<revision>\d+))?");
        if (!match.Success ||
            !int.TryParse(match.Groups["major"].Value, out var major) ||
            !int.TryParse(match.Groups["minor"].Value, out var minor) ||
            !int.TryParse(match.Groups["build"].Value, out var build))
        {
            return null;
        }

        return match.Groups["revision"].Success && int.TryParse(match.Groups["revision"].Value, out var revision)
            ? new Version(major, minor, build, revision)
            : new Version(major, minor, build, 0);
    }

    public static string FormatVersion(Version version) =>
        version.Revision > 0 ? version.ToString(4) : version.ToString(3);

    private static Version NormalizeVersion(Version version) =>
        new(
            version.Major,
            version.Minor,
            Math.Max(0, version.Build),
            Math.Max(0, version.Revision));

    private static string? NormalizeSha256(string? digest)
    {
        if (string.IsNullOrWhiteSpace(digest))
        {
            return null;
        }

        var value = digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
            ? digest[7..]
            : digest;
        return value.Length == 64 && value.All(Uri.IsHexDigit) ? value.ToLowerInvariant() : null;
    }

    private const string UpdateHelperScript = """
param(
    [int]$ApplicationProcessId,
    [string]$SourceDirectory,
    [string]$TargetDirectory,
    [string]$ExecutablePath
)
$ErrorActionPreference = 'Stop'
Wait-Process -Id $ApplicationProcessId -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 500
Get-ChildItem -LiteralPath $SourceDirectory -Force | Copy-Item -Destination $TargetDirectory -Recurse -Force
Start-Process -FilePath $ExecutablePath -WorkingDirectory $TargetDirectory -WindowStyle Hidden
""";
}
