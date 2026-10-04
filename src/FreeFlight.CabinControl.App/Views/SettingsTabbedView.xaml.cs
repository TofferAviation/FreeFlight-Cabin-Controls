using System.Diagnostics;
using System.IO;
using System.Windows;

namespace FreeFlight.CabinControl.App.Views;

public partial class SettingsTabbedView
{
    public SettingsTabbedView()
    {
        InitializeComponent();
    }

    public event RoutedEventHandler? PreviewUpdateRequested;

    public event RoutedEventHandler? CheckForUpdatesRequested;

    public event RoutedEventHandler? ThemeToggleRequested;

    private void PreviewUpdateButton_Click(object sender, RoutedEventArgs e) =>
        PreviewUpdateRequested?.Invoke(this, e);

    private void CheckForUpdatesButton_Click(object sender, RoutedEventArgs e) =>
        CheckForUpdatesRequested?.Invoke(this, e);

    private void ToggleThemeButton_Click(object sender, RoutedEventArgs e) =>
        ThemeToggleRequested?.Invoke(this, e);

    private void OpenLicenceButton_Click(object sender, RoutedEventArgs e)
    {
        var licencePath = Path.Combine(AppContext.BaseDirectory, "LICENSE.txt");
        if (!File.Exists(licencePath))
        {
            MessageBox.Show("The installed licence file could not be found. Reinstall Ember from an authorised download channel.", "Licence file unavailable", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Process.Start(new ProcessStartInfo { FileName = licencePath, UseShellExecute = true });
    }
}
