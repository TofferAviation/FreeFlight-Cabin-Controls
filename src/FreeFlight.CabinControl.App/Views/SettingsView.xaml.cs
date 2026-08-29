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
}
