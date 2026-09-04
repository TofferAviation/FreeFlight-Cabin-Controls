using System.Diagnostics;
using System.Windows.Media;
using System.Windows.Threading;
using FreeFlight.CabinControl.App.Services;
using FreeFlight.CabinControl.Core.Configuration;
using FreeFlight.CabinControl.Core.Integration;
using FreeFlight.CabinControl.Core.Persistence;

namespace FreeFlight.CabinControl.App.ViewModels;

public sealed class PerformanceViewModel : PageViewModel, IDisposable
{
    private readonly AppSettings _settings;
    private readonly PerformanceSampler _sampler = new();
    private readonly DispatcherTimer _timer;
    private readonly ISimulatorBridge? _simulatorBridge;
    private readonly ISettingsStore? _settingsStore;
    private readonly Queue<double> _cpuHistory = new();
    private readonly Queue<double> _memoryHistory = new();
    private string _cpuUsage = "0.0%";
    private string _memoryUsage = "0 MB";
    private string _performanceMode;
    private PointCollection _cpuGraphPoints = [];
    private PointCollection _memoryGraphPoints = [];
    private string _processDetail = "0 threads · 0 handles";
    private string _managedHeap = "0 MB";
    private string _samplingCost = "0.00 ms";
    private string _impactSummary = "Collecting FreeFlight performance samples…";
    private string _impactColor = "#8FC8FF";
    private double _simulatorFps;

    public PerformanceViewModel(
        AppSettings settings,
        SharedStatusViewModel status,
        string logDirectory,
        ISimulatorBridge? simulatorBridge = null,
        ISettingsStore? settingsStore = null)
        : base("Diagnostics", "Live FreeFlight Cabin Controls performance footprint")
    {
        _settings = settings;
        _simulatorBridge = simulatorBridge;
        _settingsStore = settingsStore;
        Status = status;
        _performanceMode = settings.PerformanceMode;

        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _timer.Tick += HandleTimerTick;
        ApplySamplingInterval();
        _timer.Start();
        if (_simulatorBridge is not null)
        {
            _simulatorBridge.TelemetryReceived += HandleSimulatorTelemetry;
        }

        UpdateSample();
    }

    public SharedStatusViewModel Status { get; }

    public string CpuUsage
    {
        get => _cpuUsage;
        private set => SetProperty(ref _cpuUsage, value);
    }

    public string MemoryUsage
    {
        get => _memoryUsage;
        private set => SetProperty(ref _memoryUsage, value);
    }

    public PointCollection CpuGraphPoints
    {
        get => _cpuGraphPoints;
        private set => SetProperty(ref _cpuGraphPoints, value);
    }

    public PointCollection MemoryGraphPoints
    {
        get => _memoryGraphPoints;
        private set => SetProperty(ref _memoryGraphPoints, value);
    }

    public string SimulatorFps => _simulatorFps > 0d ? $"{_simulatorFps:F0} FPS" : "—";

    public string ProcessDetail
    {
        get => _processDetail;
        private set => SetProperty(ref _processDetail, value);
    }

    public string ManagedHeap
    {
        get => _managedHeap;
        private set => SetProperty(ref _managedHeap, value);
    }

    public string SamplingCost
    {
        get => _samplingCost;
        private set => SetProperty(ref _samplingCost, value);
    }

    public string ImpactSummary
    {
        get => _impactSummary;
        private set => SetProperty(ref _impactSummary, value);
    }

    public string ImpactColor
    {
        get => _impactColor;
        private set => SetProperty(ref _impactColor, value);
    }

    public string BridgeFrameTime
    {
        get
        {
            var age = _simulatorBridge?.LastFrameAge;
            if (age is null)
            {
                return Status.IsConnected ? "Waiting for first frame" : "No live frames";
            }

            return age.Value.TotalSeconds < 1d
                ? $"{age.Value.TotalMilliseconds:F0} ms"
                : $"{age.Value.TotalSeconds:F1} s";
        }
    }

    public string PerformanceLossLabel => _simulatorFps > 0d
        ? "Exact FPS loss requires an app-off baseline; current FreeFlight resource use is shown directly."
        : "Connect the simulator to add live FPS context. FreeFlight CPU and memory are measured directly.";

    public string PerformanceMode
    {
        get => _performanceMode;
        set
        {
            if (!SetProperty(ref _performanceMode, value))
            {
                return;
            }

            _settings.PerformanceMode = value;
            ApplySamplingInterval();
            if (_settingsStore is not null)
            {
                _ = _settingsStore.SaveAsync(_settings);
            }
        }
    }

    public bool IsQualityMode
    {
        get => PerformanceMode == "Quality";
        set
        {
            if (value)
            {
                PerformanceMode = "Quality";
                NotifyModeProperties();
            }
        }
    }

    public bool IsBalancedMode
    {
        get => PerformanceMode == "Balanced";
        set
        {
            if (value)
            {
                PerformanceMode = "Balanced";
                NotifyModeProperties();
            }
        }
    }

    public bool IsLowImpactMode
    {
        get => PerformanceMode == "Low Impact";
        set
        {
            if (value)
            {
                PerformanceMode = "Low Impact";
                NotifyModeProperties();
            }
        }
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Tick -= HandleTimerTick;
        if (_simulatorBridge is not null)
        {
            _simulatorBridge.TelemetryReceived -= HandleSimulatorTelemetry;
        }

        GC.SuppressFinalize(this);
    }

    private void HandleTimerTick(object? sender, EventArgs e) => UpdateSample();

    private void UpdateSample()
    {
        var started = Stopwatch.GetTimestamp();
        var sample = _sampler.Sample();
        var samplingElapsed = Stopwatch.GetElapsedTime(started);
        var appMemoryMb = sample.AppMemoryBytes / 1024d / 1024d;

        CpuUsage = $"{sample.AppCpuPercent:F1}%";
        MemoryUsage = $"{appMemoryMb:F0} MB";
        ProcessDetail = $"{sample.AppThreadCount} threads · {sample.AppHandleCount} handles";
        ManagedHeap = $"{sample.ManagedHeapBytes / 1024d / 1024d:F0} MB";
        SamplingCost = $"{samplingElapsed.TotalMilliseconds:F2} ms";

        PushHistory(_cpuHistory, sample.AppCpuPercent);
        PushHistory(_memoryHistory, appMemoryMb);
        CpuGraphPoints = BuildPoints(_cpuHistory, 100d);
        MemoryGraphPoints = BuildPoints(
            _memoryHistory,
            Math.Max(512d, _memoryHistory.DefaultIfEmpty().Max() * 1.15d));

        UpdateImpactSummary(sample.AppCpuPercent, appMemoryMb, samplingElapsed.TotalMilliseconds);
        OnPropertyChanged(nameof(BridgeFrameTime));
        OnPropertyChanged(nameof(PerformanceLossLabel));
    }

    private void HandleSimulatorTelemetry(CabinTelemetrySnapshot snapshot)
    {
        if (snapshot.Signals.TryGetValue("simulator_fps", out var fps))
        {
            _simulatorFps = Math.Clamp(fps, 0d, 500d);
            OnPropertyChanged(nameof(SimulatorFps));
            OnPropertyChanged(nameof(PerformanceLossLabel));
        }
    }

    private void UpdateImpactSummary(double appCpuPercent, double appMemoryMb, double sampleCostMs)
    {
        if (appCpuPercent >= 12d || sampleCostMs >= 12d)
        {
            ImpactSummary = $"High FreeFlight load detected · {appCpuPercent:F1}% CPU · {appMemoryMb:F0} MB RAM · {sampleCostMs:F2} ms diagnostics sample";
            ImpactColor = "#FF6B6B";
        }
        else if (appCpuPercent >= 5d || sampleCostMs >= 5d)
        {
            ImpactSummary = $"Moderate FreeFlight load · {appCpuPercent:F1}% CPU · {appMemoryMb:F0} MB RAM · {sampleCostMs:F2} ms diagnostics sample";
            ImpactColor = "#FFB55F";
        }
        else
        {
            ImpactSummary = $"Low FreeFlight load · {appCpuPercent:F1}% CPU · {appMemoryMb:F0} MB RAM · {sampleCostMs:F2} ms diagnostics sample";
            ImpactColor = "#58E68A";
        }
    }

    private static void PushHistory(Queue<double> history, double value)
    {
        history.Enqueue(value);
        while (history.Count > 60)
        {
            history.Dequeue();
        }
    }

    private static PointCollection BuildPoints(IEnumerable<double> values, double maximum)
    {
        var samples = values.ToArray();
        var points = new PointCollection();
        if (samples.Length == 0)
        {
            return points;
        }

        for (var index = 0; index < samples.Length; index++)
        {
            var x = samples.Length == 1 ? 700d : index * 700d / (samples.Length - 1d);
            var y = 150d - (Math.Clamp(samples[index] / maximum, 0d, 1d) * 145d);
            points.Add(new System.Windows.Point(x, y));
        }

        points.Freeze();
        return points;
    }

    private void NotifyModeProperties()
    {
        OnPropertyChanged(nameof(IsQualityMode));
        OnPropertyChanged(nameof(IsBalancedMode));
        OnPropertyChanged(nameof(IsLowImpactMode));
    }

    private void ApplySamplingInterval()
    {
        _timer.Interval = PerformanceMode switch
        {
            "Quality" => TimeSpan.FromMilliseconds(500),
            "Low Impact" => TimeSpan.FromSeconds(2),
            _ => TimeSpan.FromSeconds(1)
        };
    }
}
