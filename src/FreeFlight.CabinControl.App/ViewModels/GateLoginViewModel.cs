using System.Windows.Input;
using System.Windows.Threading;
using FreeFlight.CabinControl.App.Infrastructure;
using FreeFlight.CabinControl.Core.Configuration;
using FreeFlight.CabinControl.Core.Operations;

namespace FreeFlight.CabinControl.App.ViewModels;

public sealed class GateLoginViewModel : PageViewModel, IDisposable
{
    private readonly IOperationsClock _operationsClock;
    private readonly DispatcherTimer _clockTimer;
    private readonly RelayCommand _signInCommand;
    private string _employeeId = string.Empty;
    private string _password = string.Empty;
    private GateStationOption _selectedStation;
    private bool _rememberStation = true;
    private bool _isAuthenticated;
    private string _statusMessage = "Enter any staff ID and password to begin the local preview session.";
    private string _signedInStaff = string.Empty;

    public GateLoginViewModel(AppSettings settings, IOperationsClock operationsClock)
        : base("Gate Login", "Staff access for gate operations")
    {
        _operationsClock = operationsClock;
        Stations =
        [
            new GateStationOption("LHR", "London Heathrow (LHR)"),
            new GateStationOption("JFK", "New York JFK (JFK)"),
            new GateStationOption("OSL", "Oslo Gardermoen (OSL)"),
            new GateStationOption("LGW", "London Gatwick (LGW)")
        ];
        _selectedStation = Stations.FirstOrDefault(station =>
            string.Equals(station.Code, NormalizeAirport(settings.GateOriginIata), StringComparison.OrdinalIgnoreCase)) ??
            Stations[0];
        _signInCommand = new RelayCommand(_ => SignIn());
        SignInCommand = _signInCommand;
        SignOutCommand = new RelayCommand(_ => SignOut());
        _clockTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _clockTimer.Tick += HandleClockTick;
        _clockTimer.Start();
    }

    public event EventHandler? SignedIn;
    public event EventHandler? SignedOut;

    public IReadOnlyList<GateStationOption> Stations { get; }
    public ICommand SignInCommand { get; }
    public ICommand SignOutCommand { get; }

    public string EmployeeId
    {
        get => _employeeId;
        set
        {
            if (SetProperty(ref _employeeId, value))
            {
                _signInCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string Password
    {
        get => _password;
        set
        {
            if (SetProperty(ref _password, value))
            {
                _signInCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public GateStationOption SelectedStation
    {
        get => _selectedStation;
        set => SetProperty(ref _selectedStation, value);
    }

    public bool RememberStation
    {
        get => _rememberStation;
        set => SetProperty(ref _rememberStation, value);
    }

    public bool IsAuthenticated
    {
        get => _isAuthenticated;
        private set
        {
            if (!SetProperty(ref _isAuthenticated, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsSignedOut));
            OnPropertyChanged(nameof(NavigationLabel));
        }
    }

    public bool IsSignedOut => !IsAuthenticated;
    public string NavigationLabel => IsAuthenticated ? "Gate Session" : "Gate Login";
    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string SignedInStaff
    {
        get => _signedInStaff;
        private set => SetProperty(ref _signedInStaff, value);
    }

    public string CurrentTime => _operationsClock.Now.ToString("HH:mm");
    public string ClockSourceLabel => _operationsClock.SourceLabel;

    public void Dispose()
    {
        _clockTimer.Stop();
        _clockTimer.Tick -= HandleClockTick;
        GC.SuppressFinalize(this);
    }

    private void SignIn()
    {
        if (string.IsNullOrWhiteSpace(EmployeeId) || string.IsNullOrWhiteSpace(Password))
        {
            StatusMessage = "Enter both an employee ID and password. This preview accepts any non-empty values.";
            return;
        }

        SignedInStaff = EmployeeId.Trim().ToUpperInvariant();
        Password = string.Empty;
        IsAuthenticated = true;
        StatusMessage = $"{SignedInStaff} signed in at {SelectedStation.DisplayName}.";
        SignedIn?.Invoke(this, EventArgs.Empty);
    }

    private void SignOut()
    {
        if (!IsAuthenticated)
        {
            return;
        }

        IsAuthenticated = false;
        StatusMessage = "Gate session closed. Sign in to reopen the operational pages.";
        SignedOut?.Invoke(this, EventArgs.Empty);
    }

    private void HandleClockTick(object? sender, EventArgs e) =>
        OnPropertyChanged(nameof(CurrentTime));

    private static string NormalizeAirport(string value) => value.Trim().ToUpperInvariant() switch
    {
        "EGLL" => "LHR",
        "KJFK" => "JFK",
        "ENGM" => "OSL",
        "EGKK" => "LGW",
        var airport => airport
    };
}

public sealed record GateStationOption(string Code, string DisplayName)
{
    public override string ToString() => DisplayName;
}
