using System.Collections.ObjectModel;
using System.Net.Http;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using FreeFlight.CabinControl.App.Infrastructure;
using FreeFlight.CabinControl.App.Services;
using FreeFlight.CabinControl.Core.Configuration;

namespace FreeFlight.CabinControl.App.ViewModels;

public sealed class FleetViewModel : PageViewModel, IDisposable
{
    private readonly AppSettings _settings;
    private readonly FleetApiClient _fleetApiClient;
    private readonly DispatcherTimer _syncTimer;
    private string _connectionLabel = "Fleet data: not configured";
    private string _connectionDetail = "Add the protected website address and desktop access key in Settings.";
    private Brush _connectionColor = new SolidColorBrush(Color.FromRgb(255, 203, 105));
    private string _lastSynchronizedLabel = "No fleet data synchronized";
    private bool _isSynchronizing;

    public FleetViewModel(AppSettings settings, FleetApiClient fleetApiClient)
        : base("Fleet", "Shared aircraft condition, operational status and cabin restrictions")
    {
        _settings = settings;
        _fleetApiClient = fleetApiClient;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, exception => ConnectionDetail = exception.Message);
        _syncTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(Math.Clamp(settings.FleetSyncIntervalSeconds, 10, 300)) };
        _syncTimer.Tick += async (_, _) => await RefreshAsync();
        if (settings.FleetAutoSync) _syncTimer.Start();
        _ = RefreshAsync();
    }

    public ObservableCollection<FleetAircraftRow> Aircraft { get; } = [];
    public ICommand RefreshCommand { get; }
    public bool IsSynchronizing { get => _isSynchronizing; private set => SetProperty(ref _isSynchronizing, value); }
    public string ConnectionLabel { get => _connectionLabel; private set => SetProperty(ref _connectionLabel, value); }
    public string ConnectionDetail { get => _connectionDetail; private set => SetProperty(ref _connectionDetail, value); }
    public Brush ConnectionColor { get => _connectionColor; private set => SetProperty(ref _connectionColor, value); }
    public string LastSynchronizedLabel { get => _lastSynchronizedLabel; private set => SetProperty(ref _lastSynchronizedLabel, value); }

    public async Task RefreshAsync()
    {
        if (IsSynchronizing) return;
        IsSynchronizing = true;
        try
        {
            var aircraft = await _fleetApiClient.GetAircraftAsync(_settings);
            Aircraft.Clear();
            foreach (var item in aircraft.OrderBy(item => item.Registration, StringComparer.OrdinalIgnoreCase))
            {
                Aircraft.Add(new FleetAircraftRow(item));
            }
            ConnectionLabel = "Fleet data: live";
            ConnectionDetail = $"{Aircraft.Count} aircraft synchronized from the authoritative Fleet API.";
            ConnectionColor = new SolidColorBrush(Color.FromRgb(88, 230, 138));
            LastSynchronizedLabel = $"Last synchronized {DateTime.Now:t}";
        }
        catch (Exception exception) when (exception is FleetApiException or HttpRequestException or TaskCanceledException)
        {
            ConnectionLabel = Aircraft.Count > 0 ? "Fleet data: stale" : "Fleet data: offline";
            ConnectionDetail = exception.Message;
            ConnectionColor = new SolidColorBrush(Color.FromRgb(255, 203, 105));
            LastSynchronizedLabel = Aircraft.Count > 0 ? $"Showing last synchronized data · {LastSynchronizedLabel}" : "No cached fleet data is available.";
        }
        finally { IsSynchronizing = false; }
    }

    public void Dispose()
    {
        _syncTimer.Stop();
        _fleetApiClient.Dispose();
    }
}

public sealed class FleetAircraftRow(FleetAircraftSummaryDto aircraft)
{
    public string Registration { get; } = aircraft.Registration;
    public string AircraftType { get; } = string.IsNullOrWhiteSpace(aircraft.Variant) ? aircraft.AircraftModel : $"{aircraft.AircraftModel} · {aircraft.Variant}";
    public string Station { get; } = aircraft.CurrentStation ?? "Unknown";
    public string OperationalStatus { get; } = Label(aircraft.OperationalStatus);
    public string TechnicalStatus { get; } = Label(aircraft.TechnicalStatus);
    public string DispatchStatus { get; } = Label(aircraft.DispatchStatus);
    public string FlightHours { get; } = $"{Math.Max(0, aircraft.AirframeHoursMinutes) / 60:N0}:{Math.Max(0, aircraft.AirframeHoursMinutes) % 60:00}";
    public string Cycles { get; } = aircraft.AirframeCycles.ToString("N0");

    private static string Label(string value) => string.Join(" ", value.Split('_', StringSplitOptions.RemoveEmptyEntries).Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
}
