using System.Diagnostics;
using System.Windows;
using System.Windows.Input;

namespace FreeFlight.CabinControl.App.Views;

public partial class FlightLoggerView
{
    private const string FlightLoggerUrl = "https://flightlogger.app/";

    public FlightLoggerView()
    {
        InitializeComponent();
    }

    private void OpenFlightLogger_Click(object sender, RoutedEventArgs e) => OpenFlightLogger();

    private void Promo_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e) => OpenFlightLogger();

    private static void OpenFlightLogger()
    {
        try
        {
            Process.Start(new ProcessStartInfo(FlightLoggerUrl)
            {
                UseShellExecute = true
            });
        }
        catch
        {
            MessageBox.Show(
                $"Windows could not open your default browser.\n\nOpen this address manually:\n{FlightLoggerUrl}",
                "FlightLogger",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }
}
