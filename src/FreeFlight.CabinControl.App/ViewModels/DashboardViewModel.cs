using FreeFlight.CabinControl.App.Infrastructure;
using FreeFlight.CabinControl.Core.Configuration;

namespace FreeFlight.CabinControl.App.ViewModels;

public sealed class DashboardViewModel : PageViewModel
{
    private readonly AppSettings _settings;
    private bool _cabinImmersionEnabled;
    private bool _automaticAnnouncementsEnabled;
    private bool _passengerAmbienceEnabled;
    private bool _crewEventsEnabled;

    public DashboardViewModel(AppSettings settings, SharedStatusViewModel status)
        : base("Cabin Overview", "FlightFactor 777 v2 development profile")
    {
        _settings = settings;
        Status = status;
        _cabinImmersionEnabled = settings.CabinImmersionEnabled;
        _automaticAnnouncementsEnabled = settings.AutomaticAnnouncementsEnabled;
        _passengerAmbienceEnabled = settings.PassengerAmbienceEnabled;
        _crewEventsEnabled = settings.CrewEventsEnabled;
    }

    public SharedStatusViewModel Status { get; }

    public string FlightPhase => "PREVIEW";

    public string Altitude => "Awaiting telemetry";

    public string NowPlaying => "No X-Plane audio";

    public bool CabinImmersionEnabled
    {
        get => _cabinImmersionEnabled;
        set
        {
            if (SetProperty(ref _cabinImmersionEnabled, value))
            {
                _settings.CabinImmersionEnabled = value;
            }
        }
    }

    public bool AutomaticAnnouncementsEnabled
    {
        get => _automaticAnnouncementsEnabled;
        set
        {
            if (SetProperty(ref _automaticAnnouncementsEnabled, value))
            {
                _settings.AutomaticAnnouncementsEnabled = value;
            }
        }
    }

    public bool PassengerAmbienceEnabled
    {
        get => _passengerAmbienceEnabled;
        set
        {
            if (SetProperty(ref _passengerAmbienceEnabled, value))
            {
                _settings.PassengerAmbienceEnabled = value;
            }
        }
    }

    public bool CrewEventsEnabled
    {
        get => _crewEventsEnabled;
        set
        {
            if (SetProperty(ref _crewEventsEnabled, value))
            {
                _settings.CrewEventsEnabled = value;
            }
        }
    }
}
