using System.Diagnostics;
using System.IO;
using System.Text;

namespace FreeFlight.CabinControl.App.Services;

public sealed class FileLogService
{
    private readonly Lock _writeLock = new();
    private readonly string _fallbackLogPath;

    public FileLogService(string logDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logDirectory);
        var requestedLogPath = Path.Combine(
            Path.GetFullPath(logDirectory),
            "FreeFlight.CabinControl.log");
        _fallbackLogPath = Path.Combine(
            Path.GetTempPath(),
            "FreeFlight",
            "CabinControl",
            "logs",
            "FreeFlight.CabinControl.log");
        LogPath = CanOpenForWriting(requestedLogPath) ? requestedLogPath : _fallbackLogPath;
        LogDirectory = Path.GetDirectoryName(LogPath) ?? Path.GetTempPath();
    }

    public string LogDirectory { get; }

    public string LogPath { get; }

    public void Information(string message) => Write("INFO", message, null);

    public void Error(string message, Exception exception) => Write("ERROR", message, exception);

    private void Write(string level, string message, Exception? exception)
    {
        var builder = new StringBuilder()
            .Append(DateTimeOffset.Now.ToString("O"))
            .Append(" [")
            .Append(level)
            .Append("] ")
            .AppendLine(message);

        if (exception is not null)
        {
            builder.AppendLine(exception.ToString());
        }

        lock (_writeLock)
        {
            var entry = builder.ToString();
            if (!TryAppend(LogPath, entry) && !TryAppend(_fallbackLogPath, entry))
            {
                Debug.WriteLine(entry);
            }
        }
    }

    private static bool TryAppend(string path, string entry)
    {
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.AppendAllText(path, entry, Encoding.UTF8);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                          NotSupportedException or System.Security.SecurityException)
        {
            return false;
        }
    }

    private static bool CanOpenForWriting(string path)
    {
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                          NotSupportedException or System.Security.SecurityException)
        {
            return false;
        }
    }
}
