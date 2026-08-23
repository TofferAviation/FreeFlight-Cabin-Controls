using System.Diagnostics;
using FreeFlight.CabinControl.Core.Diagnostics;

namespace FreeFlight.CabinControl.App.Services;

public sealed class PerformanceSampler
{
    private readonly Process _process = Process.GetCurrentProcess();
    private DateTimeOffset _previousTimestamp = DateTimeOffset.UtcNow;
    private TimeSpan _previousCpuTime;

    public PerformanceSampler()
    {
        _previousCpuTime = _process.TotalProcessorTime;
    }

    public PerformanceSnapshot Sample()
    {
        _process.Refresh();
        var timestamp = DateTimeOffset.UtcNow;
        var cpuTime = _process.TotalProcessorTime;
        var wallTime = timestamp - _previousTimestamp;
        var cpuDelta = cpuTime - _previousCpuTime;

        var cpuPercent = wallTime.TotalMilliseconds <= 0
            ? 0
            : cpuDelta.TotalMilliseconds / wallTime.TotalMilliseconds / Environment.ProcessorCount * 100;

        _previousTimestamp = timestamp;
        _previousCpuTime = cpuTime;

        return new PerformanceSnapshot(
            timestamp,
            Math.Clamp(cpuPercent, 0, 100),
            _process.PrivateMemorySize64,
            null,
            null,
            0,
            0);
    }
}
