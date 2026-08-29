using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using FreeFlight.CabinControl.App.Infrastructure;
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
    private string _cpuUsage = "0.0%";
    private string _memoryUsage = "0 MB";
    private string _performanceMode;
    private readonly Queue<double> _cpuHistory = new();
    private readonly Queue<double> _memoryHistory = new();
    private PointCollection _cpuGraphPoints = [];
    private PointCollection _memoryGraphPoints = [];
    private string _systemCpuUsage = "0.0%";
    private string _systemMemoryUsage = "0 / 0 GB";
    private string _simulatorProcess = "Not running";
    private string _simulatorCpuUsage = "—";
    private string _simulatorMemoryUsage = "—";
    private string _processDetail = "0 threads · 0 handles";
    private string _managedHeap = "0 MB managed heap";
    private string _recommendation = "Collecting a baseline…";
    private string _recommendationColor = "#8FC8FF";
    private double _simulatorFps;

    public PerformanceViewModel(
        AppSettings settings,
        SharedStatusViewModel status,
        string logDirectory,
        ISimulatorBridge? simulatorBridge = null,
        ISettingsStore? settingsStore = null)
        : base("System Performance", "Live Cabin Core resource monitoring")
    {
        _settings = settings;
        _simulatorBridge = simulatorBridge;
        _settingsStore = settingsStore;
        Status = status;
        _performanceMode = settings.PerformanceMode;
        LogDirectory = Path.GetFullPath(logDirectory);
        OpenLogFolderCommand = new RelayCommand(_ => OpenLogFolder());
        DiagnosticEntries =
        [
            new("Cabin Core application initialized", true),
            new("Settings store ready", true),
            new("Generic airline pack available", true),
            new("X-Plane Web API waiting for simulator", false)
        ];

        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _timer.Tick += (_, _) => UpdateSample();
        ApplySamplingInterval();
        _timer.Start();
        if (_simulatorBridge is not null)
        {
            _simulatorBridge.TelemetryReceived += HandleSimulatorTelemetry;
        }
        UpdateSample();
    }

    public SharedStatusViewModel Status { get; }

    public string LogDirectory { get; }

    public ICommand OpenLogFolderCommand { get; }

    public ObservableCollection<DiagnosticEntry> DiagnosticEntries { get; }

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

    public string AudioLatency => "—";

    public string ActiveSounds => "0 / 64";

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

    public string SystemCpuUsage { get => _systemCpuUsage; private set => SetProperty(ref _systemCpuUsage, value); }
    public string SystemMemoryUsage { get => _systemMemoryUsage; private set => SetProperty(ref _systemMemoryUsage, value); }
    public string SimulatorProcess { get => _simulatorProcess; private set => SetProperty(ref _simulatorProcess, value); }
    public string SimulatorCpuUsage { get => _simulatorCpuUsage; private set => SetProperty(ref _simulatorCpuUsage, value); }
    public string SimulatorMemoryUsage { get => _simulatorMemoryUsage; private set => SetProperty(ref _simulatorMemoryUsage, value); }
    public string SimulatorFps => _simulatorFps > 0d ? $"{_simulatorFps:F0} FPS" : "—";
    public string ProcessDetail { get => _processDetail; private set => SetProperty(ref _processDetail, value); }
    public string ManagedHeap { get => _managedHeap; private set => SetProperty(ref _managedHeap, value); }
    public string Recommendation { get => _recommendation; private set => SetProperty(ref _recommendation, value); }
    public string RecommendationColor { get => _recommendationColor; private set => SetProperty(ref _recommendationColor, value); }

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
                ? $"{age.Value.TotalMilliseconds:F0} ms ago"
                : $"{age.Value.TotalSeconds:F1} s ago";
        }
    }

    public string PerformanceMode
    {
        get => _performanceMode;
        set
        {
            if (SetProperty(ref _performanceMode, value))
            {
                _settings.PerformanceMode = value;
                ApplySamplingInterval();
                if (_settingsStore is not null)
                {
                    _ = _settingsStore.SaveAsync(_settings);
                }
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
        if (_simulatorBridge is not null)
        {
            _simulatorBridge.TelemetryReceived -= HandleSimulatorTelemetry;
        }
        GC.SuppressFinalize(this);
    }

    private void UpdateSample()
    {
        var sample = _sampler.Sample();
        var appMemoryMb = sample.AppMemoryBytes / 1024d / 1024d;
        CpuUsage = $"{sample.AppCpuPercent:F1}%";
        MemoryUsage = $"{appMemoryMb:F0} MB";
        SystemCpuUsage = $"{sample.SystemCpuPercent:F1}%";
        var usedGb = (sample.SystemTotalMemoryBytes - sample.SystemAvailableMemoryBytes) / 1024d / 1024d / 1024d;
        var totalGb = sample.SystemTotalMemoryBytes / 1024d / 1024d / 1024d;
        SystemMemoryUsage = $"{usedGb:F1} / {totalGb:F1} GB";
        SimulatorProcess = sample.SimulatorName;
        SimulatorCpuUsage = sample.SimulatorName == "Not running" ? "—" : $"{sample.SimulatorCpuPercent:F1}% CPU";
        SimulatorMemoryUsage = sample.SimulatorName == "Not running" ? "—" : $"{sample.SimulatorMemoryBytes / 1024d / 1024d:F0} MB";
        ProcessDetail = $"{sample.AppThreadCount} threads · {sample.AppHandleCount} handles";
        ManagedHeap = $"{sample.ManagedHeapBytes / 1024d / 1024d:F0} MB managed heap";
        OnPropertyChanged(nameof(SimulatorFps));
        PushHistory(_cpuHistory, sample.AppCpuPercent);
        PushHistory(_memoryHistory, appMemoryMb);
        CpuGraphPoints = BuildPoints(_cpuHistory, 100d);
        MemoryGraphPoints = BuildPoints(_memoryHistory, Math.Max(512d, _memoryHistory.DefaultIfEmpty().Max() * 1.15d));
        UpdateRecommendation(sample, usedGb, totalGb);
        OnPropertyChanged(nameof(BridgeFrameTime));
        var bridgeEntry = new DiagnosticEntry(
            Status.IsConnected
                ? $"{Status.SimulatorName} live · {Status.FlightPhase}"
                : Status.ConnectionDetail,
            Status.IsConnected);
        if (DiagnosticEntries.Count > 3 && DiagnosticEntries[3] != bridgeEntry)
        {
            DiagnosticEntries[3] = bridgeEntry;
        }
    }

    private void HandleSimulatorTelemetry(CabinTelemetrySnapshot snapshot)
    {
        if (snapshot.Signals.TryGetValue("simulator_fps", out var fps))
        {
            _simulatorFps = Math.Clamp(fps, 0d, 500d);
        }
    }

    private void UpdateRecommendation(DetailedPerformanceSample sample, double usedGb, double totalGb)
    {
        if (_simulatorFps is > 0d and < 25d)
        {
            Recommendation = "Simulator frame rate is below 25 FPS. Reduce world objects/traffic first, then lower cloud or shadow quality.";
            RecommendationColor = "#FFB55F";
        }
        else if (totalGb > 0d && usedGb / totalGb > 0.90d)
        {
            Recommendation = "System memory pressure is above 90%. Close background apps and reduce texture resolution to avoid stutters.";
            RecommendationColor = "#FF6B6B";
        }
        else if (sample.SystemCpuPercent > 92d)
        {
            Recommendation = "System CPU load is saturated. Reduce simulator traffic and object density, or select Low Impact mode here.";
            RecommendationColor = "#FFB55F";
        }
        else if (sample.AppCpuPercent > 12d)
        {
            Recommendation = "Cabin Control is using more CPU than expected. Low Impact mode reduces background refresh work.";
            RecommendationColor = "#FFB55F";
        }
        else
        {
            Recommendation = "No current bottleneck detected. Balanced mode is appropriate for this system.";
            RecommendationColor = "#58E68A";
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
            points.Add(new Point(x, y));
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

    private void OpenLogFolder()
    {
        try
        {
            Directory.CreateDirectory(LogDirectory);
            Process.Start(new ProcessStartInfo(LogDirectory) { UseShellExecute = true });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(exception.Message, "Could not open log folder", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}

public sealed record DiagnosticEntry(string Message, bool IsReady);
