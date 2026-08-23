using System.IO;
using System.Text;

namespace FreeFlight.CabinControl.App.Services;

public sealed class FileLogService
{
    private readonly Lock _writeLock = new();

    public FileLogService(string logDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logDirectory);
        LogDirectory = Path.GetFullPath(logDirectory);
        Directory.CreateDirectory(LogDirectory);
        LogPath = Path.Combine(LogDirectory, "FreeFlight.CabinControl.log");
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
            File.AppendAllText(LogPath, builder.ToString(), Encoding.UTF8);
        }
    }
}
