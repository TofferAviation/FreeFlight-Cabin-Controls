using System.ComponentModel;
using System.Windows;
using FreeFlight.CabinControl.App.ViewModels;

namespace FreeFlight.CabinControl.App;

public partial class MainWindow
{
    public MainWindow()
    {
        InitializeComponent();
        Closing += OnClosing;
        StateChanged += (_, _) => UpdateMaximizeGlyph();
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaximizeButton_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void UpdateMaximizeGlyph()
    {
        // The standard maximize glyph remains understandable in both window states.
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.Dispose();
        }
    }
}
