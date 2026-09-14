using System.Diagnostics;
using System.IO;
using System.Windows;

namespace FreeFlight.CabinControl.App.Views;

public partial class SettingsView
{
    public SettingsView()
    {
        InitializeComponent();
    }

    public event RoutedEventHandler? PreviewUpdateRequested;

    public event RoutedEventHandler? CheckForUpdatesRequested;

    private void PreviewUpdateButton_Click(object sender, RoutedEventArgs e) =>
        PreviewUpdateRequested?.Invoke(this, e);

    private void CheckForUpdatesButton_Click(object sender, RoutedEventArgs e) =>
        CheckForUpdatesRequested?.Invoke(this, e);

    private void OpenLicenceButton_Click(object sender, RoutedEventArgs e)
    {
        var licencePath = Path.Combine(AppContext.BaseDirectory, "LICENSE.txt");
        if (!File.Exists(licencePath))
        {
            MessageBox.Show(
                "The installed licence file could not be found. Reinstall FreeFlight Cabin Control from an authorised download channel.",
                "Licence file unavailable",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = licencePath,
            UseShellExecute = true,
        });
    }
}
