using System.Windows;

namespace FreeFlight.CabinControl.App.Views;

public partial class DashboardView
{
    public event RoutedEventHandler? ThemeToggleRequested;

    public DashboardView()
    {
        InitializeComponent();
    }

    private void ToggleThemeButton_Click(object sender, RoutedEventArgs e) =>
        ThemeToggleRequested?.Invoke(this, e);
}
