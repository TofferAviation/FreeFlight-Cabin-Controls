using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace FreeFlight.CabinControl.Core.Diagnostics;

/// <summary>
/// Creates a small, local-only diagnostic report when the application reaches an
/// unexpected exception boundary. Reports intentionally do not include settings,
/// account data, request headers, or unredacted credential-like values.
/// </summary>
public sealed class CrashReportWriter
{
    private const int MaximumRetainedReports = 20;

    private static readonly Regex QueryValuePattern = new(
        @"(?ix)(?<prefix>[?&;]\s*(?:token|access[_-]?token|authorization|api[_-]?key|password|secret|session(?:[_-]?id)?|cookie)\s*=\s*)[^&#\s]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex LabelledValuePattern = new(
        @"(?ix)(?<prefix>\b(?:token|access[_-]?token|authorization|api[_-]?key|password|secret|session(?:[_-]?id)?|cookie)\b\s*(?:[:=]\s*|is\s+))(?<value>[^,\s\r\n]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex BearerValuePattern = new(
        @"(?ix)\bbearer\s+[a-z0-9._~+\-/]+=*",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly string _applicationName;
    private readonly string _applicationVersion;
    private readonly string _reportDirectory;
    private int _isWriting;

    public CrashReportWriter(string reportDirectory, string applicationName, string applicationVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reportDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationName);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationVersion);

        _reportDirectory = Path.GetFullPath(reportDirectory);
        _applicationName = applicationName.Trim();
        _applicationVersion = applicationVersion.Trim();
    }

    public string ReportDirectory => _reportDirectory;

    /// <summary>
    /// Writes one report and never throws. A null result means a report could not
    /// be written, for example because local storage is unavailable.
    /// </summary>
    public string? TryWrite(string source, Exception exception)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentNullException.ThrowIfNull(exception);

        if (Interlocked.Exchange(ref _isWriting, 1) != 0)
        {
            return null;
        }

        try
        {
            Directory.CreateDirectory(_reportDirectory);

            var occurredAt = DateTimeOffset.UtcNow;
            var reportPath = Path.Combine(
                _reportDirectory,
                $"ember-crash-{occurredAt:yyyyMMddTHHmmssfffZ}-{Environment.ProcessId}-{Guid.NewGuid():N}.txt");

            File.WriteAllText(reportPath, BuildReport(source, exception, occurredAt), new UTF8Encoding(false));
            TrimOldReports();
            return reportPath;
        }
        catch
        {
            // Crash reporting is deliberately best-effort. It must never turn an
            // already failing application path into a second exception.
            return null;
        }
        finally
        {
            Volatile.Write(ref _isWriting, 0);
        }
    }

    private string BuildReport(string source, Exception exception, DateTimeOffset occurredAt)
    {
        var builder = new StringBuilder()
            .AppendLine("Ember ACARS Systems crash report")
            .AppendLine("================================")
            .Append("Occurred (UTC): ").AppendLine(occurredAt.ToString("O"))
            .Append("Source: ").AppendLine(Redact(source))
            .Append("Application: ").AppendLine(_applicationName)
            .Append("Version: ").AppendLine(_applicationVersion)
            .Append("Operating system: ").AppendLine(RuntimeInformation.OSDescription)
            .Append("Runtime: ").AppendLine(RuntimeInformation.FrameworkDescription)
            .Append("Process architecture: ").AppendLine(RuntimeInformation.ProcessArchitecture.ToString())
            .Append("Process ID: ").AppendLine(Environment.ProcessId.ToString())
            .AppendLine()
            .AppendLine("Exception")
            .AppendLine("---------")
            .AppendLine(Redact(exception.ToString()))
            .AppendLine()
            .AppendLine("Privacy")
            .AppendLine("-------")
            .AppendLine("This report is stored only on this computer. Common password, token, cookie, secret and API-key values are redacted before it is written.");

        return builder.ToString();
    }

    private void TrimOldReports()
    {
        try
        {
            var obsoleteReports = new DirectoryInfo(_reportDirectory)
                .EnumerateFiles("ember-crash-*.txt", SearchOption.TopDirectoryOnly)
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .Skip(MaximumRetainedReports);

            foreach (var report in obsoleteReports)
            {
                report.Delete();
            }
        }
        catch
        {
            // Retention is a convenience; a failure here must not discard a new report.
        }
    }

    private static string Redact(string value)
    {
        var redacted = QueryValuePattern.Replace(value, match => $"{match.Groups["prefix"].Value}[REDACTED]");
        redacted = LabelledValuePattern.Replace(redacted, match => $"{match.Groups["prefix"].Value}[REDACTED]");
        return BearerValuePattern.Replace(redacted, "Bearer [REDACTED]");
    }
}
