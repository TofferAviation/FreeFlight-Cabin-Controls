using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using FreeFlight.CabinControl.App.Views;
using FreeFlight.CabinControl.App.ViewModels;

namespace FreeFlight.CabinControl.App;

public partial class MainWindow
{
    private bool _startupUpdateCheckStarted;
    private bool _automaticUpdateCheckInProgress;
    private string? _notifiedUpdateTag;
    private readonly DispatcherTimer _updateCheckTimer;

    public MainWindow()
    {
        InitializeComponent();
        _updateCheckTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMinutes(30)
        };
        _updateCheckTimer.Tick += async (_, _) => await CheckForAutomaticUpdateAsync();
        Loaded += OnLoaded;
        Closing += OnClosing;
        StateChanged += (_, _) => UpdateMaximizeGlyph();
        PreviewKeyDown += OnPreviewKeyDown;
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaximizeButton_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_startupUpdateCheckStarted || DataContext is not MainWindowViewModel)
        {
            return;
        }

        _startupUpdateCheckStarted = true;
        await CheckForAutomaticUpdateAsync();
        _updateCheckTimer.Start();
    }

    private async Task CheckForAutomaticUpdateAsync()
    {
        if (_automaticUpdateCheckInProgress || DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        _automaticUpdateCheckInProgress = true;
        try
        {
            var updateAvailable = await viewModel.Updates.CheckForStartupUpdateAsync(viewModel.IsFlightInProgress);
            var tag = viewModel.Updates.AvailableUpdateTag;
            if (!updateAvailable || !IsVisible || string.Equals(tag, _notifiedUpdateTag, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _notifiedUpdateTag = tag;
            ShowUpdateNotification(viewModel);
        }
        finally
        {
            _automaticUpdateCheckInProgress = false;
        }
    }

    private void SettingsView_PreviewUpdateRequested(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        viewModel.Updates.PreparePreviewNotification(viewModel.IsFlightInProgress);
        ShowUpdateNotification(viewModel);
    }

    private async void SettingsView_CheckForUpdatesRequested(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        await viewModel.Updates.CheckAsync();
        if (viewModel.Updates.HasUpdate)
        {
            viewModel.Updates.PrepareNotification(viewModel.IsFlightInProgress);
            _notifiedUpdateTag = viewModel.Updates.AvailableUpdateTag;
            ShowUpdateNotification(viewModel);
            return;
        }

        MessageBox.Show(
            viewModel.Updates.Status,
            "FreeFlight updates",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void ShowUpdateNotification(MainWindowViewModel viewModel) =>
        new UpdateNotificationWindow
        {
            Owner = this,
            DataContext = viewModel.Updates
        }.ShowDialog();

    private void OpenChangelogButton_Click(object sender, RoutedEventArgs e)
    {
        var changelog = DataContext is MainWindowViewModel viewModel
            ? viewModel.Updates.Changelog
            : "No bundled changelog is available.";
        new ChangelogWindow(changelog) { Owner = this }.ShowDialog();
    }

    private void UpdateMaximizeGlyph()
    {
        // The standard maximize glyph remains understandable in both window states.
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        _updateCheckTimer.Stop();
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.Dispose();
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not MainWindowViewModel { ActivePage: "IportDcs" } viewModel)
        {
            return;
        }

        var functionNumber = e.Key switch
        {
            Key.F1 => 1,
            Key.F2 => 2,
            Key.F3 => 3,
            Key.F4 => 4,
            Key.F5 => 5,
            Key.F10 => 10,
            Key.F11 => 11,
            _ => 0
        };
        if (functionNumber > 0 && viewModel.IportDcs.HandleFunctionKey(functionNumber))
        {
            e.Handled = true;
        }
    }
}
