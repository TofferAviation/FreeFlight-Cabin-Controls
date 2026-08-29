using FreeFlight.CabinControl.Core.Integration;
using FreeFlight.CabinControl.Core.Operations;

namespace FreeFlight.CabinControl.App.Services;

public sealed class LocalOperationsClock : IOperationsClock
{
    private readonly object _sync = new();
    private DateTimeOffset? _simulatorTime;
    private DateTimeOffset _lastTelemetryReceivedAt;
    private string _sourceLabel = "LOCAL TIME";

    public DateTimeOffset Now
    {
        get
        {
            lock (_sync)
            {
                return _simulatorTime is { } simulatorTime
                    ? simulatorTime + (DateTimeOffset.UtcNow - _lastTelemetryReceivedAt)
                    : DateTimeOffset.Now;
            }
        }
    }

    public string SourceLabel
    {
        get
        {
            lock (_sync)
            {
                return _sourceLabel;
            }
        }
    }

    public void ApplyTelemetry(CabinTelemetrySnapshot snapshot, string simulatorName)
    {
        if (!snapshot.Signals.TryGetValue("sim_local_time_sec", out var secondsSinceMidnight) ||
            double.IsNaN(secondsSinceMidnight))
        {
            return;
        }

        var localToday = DateTimeOffset.Now;
        var normalizedSeconds = ((secondsSinceMidnight % 86_400d) + 86_400d) % 86_400d;
        var simulatorTime = new DateTimeOffset(
            localToday.Year,
            localToday.Month,
            localToday.Day,
            0,
            0,
            0,
            localToday.Offset).AddSeconds(normalizedSeconds);
        lock (_sync)
        {
            _simulatorTime = simulatorTime;
            _lastTelemetryReceivedAt = DateTimeOffset.UtcNow;
            _sourceLabel = simulatorName.Contains("MSFS", StringComparison.OrdinalIgnoreCase)
                ? "MSFS TIME"
                : "X-PLANE TIME";
        }
    }
}
