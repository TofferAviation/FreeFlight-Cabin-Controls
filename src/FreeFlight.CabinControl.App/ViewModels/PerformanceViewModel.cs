using System.Collections.ObjectModel;
using System.Windows.Threading;
using FreeFlight.CabinControl.App.Infrastructure;
using FreeFlight.CabinControl.App.Services;
using FreeFlight.CabinControl.Core.Configuration;

namespace FreeFlight.CabinControl.App.ViewModels;

public sealed class PerformanceViewModel : PageViewModel, IDisposable
{
    private readonly AppSettings _settings;
    private readonly PerformanceSampler _sampler = new();
    private readonly DispatcherTimer _timer;
    private string _cpuUsage = "0.0%";
    private string _memoryUsage = "0 MB";
    private string _performanceMode;

    public PerformanceViewModel(AppSettings settings, SharedStatusViewModel status)
        : base("System Performance", "Live Cabin Core resource monitoring")
    {
        _settings = settings;
        Status = status;
        _performanceMode = settings.PerformanceMode;
        DiagnosticEntries =
        [
            new("Cabin Core application initialized", true),
            new("Settings store ready", true),
            new("Generic airline pack available", true),
            new("X-Plane bridge awaiting implementation", false)
        ];

        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _timer.Tick += (_, _) => UpdateSample();
        _timer.Start();
        UpdateSample();
    }

    public SharedStatusViewModel Status { get; }

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

    public string BridgeFrameTime => "—";

    public string PerformanceMode
    {
        get => _performanceMode;
        set
        {
            if (SetProperty(ref _performanceMode, value))
            {
                _settings.PerformanceMode = value;
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
        GC.SuppressFinalize(this);
    }

    private void UpdateSample()
    {
        var sample = _sampler.Sample();
        CpuUsage = $"{sample.CoreCpuPercent:F1}%";
        MemoryUsage = $"{sample.CoreMemoryMegabytes:F0} MB";
    }

    private void NotifyModeProperties()
    {
        OnPropertyChanged(nameof(IsQualityMode));
        OnPropertyChanged(nameof(IsBalancedMode));
        OnPropertyChanged(nameof(IsLowImpactMode));
    }
}

public sealed record DiagnosticEntry(string Message, bool IsReady);
