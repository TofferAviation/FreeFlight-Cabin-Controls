using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using FreeFlight.CabinControl.App.Infrastructure;

namespace FreeFlight.CabinControl.App.ViewModels;

public sealed class FlightLoggerViewModel : PageViewModel
{
    public const string OfficialUrl = "https://flightlogger.app/";

    public FlightLoggerViewModel()
        : base("FlightLogger", "Track and celebrate your real-world flying")
    {
        OpenFlightLoggerCommand = new RelayCommand(_ => OpenFlightLogger());
    }

    public string WebsiteLabel => "flightlogger.app";

    public ICommand OpenFlightLoggerCommand { get; }

    private static void OpenFlightLogger()
    {
        try
        {
            Process.Start(new ProcessStartInfo(OfficialUrl)
            {
                UseShellExecute = true
            });
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                $"FlightLogger could not be opened in your browser.\n\n{exception.Message}",
                "Open FlightLogger",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }
}
