using System.Diagnostics;
using System.Runtime.InteropServices;

namespace FreeFlight.CabinControl.App.Services;

public sealed class PerformanceSampler
{
    private readonly Process _process = Process.GetCurrentProcess();
    private DateTimeOffset _previousTimestamp = DateTimeOffset.UtcNow;
    private TimeSpan _previousCpuTime;
    private ulong _previousSystemIdle;
    private ulong _previousSystemKernel;
    private ulong _previousSystemUser;
    private int _simulatorProcessId;
    private TimeSpan _previousSimulatorCpu;
    private DateTimeOffset _previousSimulatorTimestamp;

    public PerformanceSampler()
    {
        _previousCpuTime = _process.TotalProcessorTime;
        ReadSystemTimes(out _previousSystemIdle, out _previousSystemKernel, out _previousSystemUser);
    }

    public DetailedPerformanceSample Sample()
    {
        _process.Refresh();
        var timestamp = DateTimeOffset.UtcNow;
        var wallTime = timestamp - _previousTimestamp;
        var cpuTime = _process.TotalProcessorTime;
        var appCpu = CalculateCpu(cpuTime - _previousCpuTime, wallTime);
        _previousTimestamp = timestamp;
        _previousCpuTime = cpuTime;

        var systemCpu = SampleSystemCpu();
        var memory = new MemoryStatusEx();
        _ = GlobalMemoryStatusEx(memory);
        var simulator = FindSimulatorProcess();
        var simulatorCpu = 0d;
        long simulatorMemory = 0;
        string simulatorName = "Not running";
        if (simulator is not null)
        {
            using (simulator)
            {
                simulator.Refresh();
                simulatorName = simulator.ProcessName.Contains("FlightSimulator", StringComparison.OrdinalIgnoreCase)
                    ? "Microsoft Flight Simulator 2024"
                    : "X-Plane";
                simulatorMemory = simulator.WorkingSet64;
                if (_simulatorProcessId == simulator.Id && _previousSimulatorTimestamp != default)
                {
                    simulatorCpu = CalculateCpu(
                        simulator.TotalProcessorTime - _previousSimulatorCpu,
                        timestamp - _previousSimulatorTimestamp);
                }

                _simulatorProcessId = simulator.Id;
                _previousSimulatorCpu = simulator.TotalProcessorTime;
                _previousSimulatorTimestamp = timestamp;
            }
        }
        else
        {
            _simulatorProcessId = 0;
            _previousSimulatorTimestamp = default;
        }

        return new DetailedPerformanceSample(
            timestamp,
            appCpu,
            _process.PrivateMemorySize64,
            systemCpu,
            memory.TotalPhysical,
            memory.AvailablePhysical,
            _process.Threads.Count,
            _process.HandleCount,
            GC.GetTotalMemory(false),
            simulatorName,
            simulatorCpu,
            simulatorMemory);
    }

    private Process? FindSimulatorProcess()
    {
        if (_simulatorProcessId > 0)
        {
            try
            {
                var existing = Process.GetProcessById(_simulatorProcessId);
                if (!existing.HasExited)
                {
                    return existing;
                }

                existing.Dispose();
            }
            catch (ArgumentException)
            {
            }
        }

        return Process.GetProcessesByName("X-Plane").FirstOrDefault() ??
               Process.GetProcessesByName("FlightSimulator2024").FirstOrDefault() ??
               Process.GetProcessesByName("FlightSimulator").FirstOrDefault();
    }

    private double SampleSystemCpu()
    {
        if (!ReadSystemTimes(out var idle, out var kernel, out var user))
        {
            return 0d;
        }

        var idleDelta = idle - _previousSystemIdle;
        var kernelDelta = kernel - _previousSystemKernel;
        var userDelta = user - _previousSystemUser;
        _previousSystemIdle = idle;
        _previousSystemKernel = kernel;
        _previousSystemUser = user;
        var total = kernelDelta + userDelta;
        return total == 0 ? 0d : Math.Clamp((total - idleDelta) * 100d / total, 0d, 100d);
    }

    private static double CalculateCpu(TimeSpan cpuDelta, TimeSpan wallDelta) => wallDelta.TotalMilliseconds <= 0d
        ? 0d
        : Math.Clamp(cpuDelta.TotalMilliseconds / wallDelta.TotalMilliseconds / Environment.ProcessorCount * 100d, 0d, 100d);

    private static bool ReadSystemTimes(out ulong idle, out ulong kernel, out ulong user)
    {
        if (!GetSystemTimes(out var idleTime, out var kernelTime, out var userTime))
        {
            idle = kernel = user = 0;
            return false;
        }

        idle = idleTime.Value;
        kernel = kernelTime.Value;
        user = userTime.Value;
        return true;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out NativeFileTime idle, out NativeFileTime kernel, out NativeFileTime user);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatusEx buffer);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativeFileTime
    {
        private readonly uint _low;
        private readonly uint _high;
        public ulong Value => ((ulong)_high << 32) | _low;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private sealed class MemoryStatusEx
    {
        private uint _length = checked((uint)Marshal.SizeOf<MemoryStatusEx>());
        private uint _memoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        private ulong _totalPageFile;
        private ulong _availablePageFile;
        private ulong _totalVirtual;
        private ulong _availableVirtual;
        private ulong _availableExtendedVirtual;
    }
}

public sealed record DetailedPerformanceSample(
    DateTimeOffset Timestamp,
    double AppCpuPercent,
    long AppMemoryBytes,
    double SystemCpuPercent,
    ulong SystemTotalMemoryBytes,
    ulong SystemAvailableMemoryBytes,
    int AppThreadCount,
    int AppHandleCount,
    long ManagedHeapBytes,
    string SimulatorName,
    double SimulatorCpuPercent,
    long SimulatorMemoryBytes);
