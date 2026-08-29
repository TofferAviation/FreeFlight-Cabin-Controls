using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using FreeFlight.CabinControl.App.Views;
using FreeFlight.CabinControl.App.ViewModels;

namespace FreeFlight.CabinControl.App;

public partial class MainWindow
{
    private bool _startupUpdateCheckStarted;
    private bool _automaticUpdateCheckInProgress;
    private bool _flightLoggerPageInstalled;
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
        InstallFlightLoggerPage();

        if (_startupUpdateCheckStarted || DataContext is not MainWindowViewModel)
        {
            return;
        }

        _startupUpdateCheckStarted = true;
        await CheckForAutomaticUpdateAsync();
        _updateCheckTimer.Start();
    }

    private void InstallFlightLoggerPage()
    {
        if (_flightLoggerPageInstalled)
        {
            return;
        }

        var navigationPanel = FindVisualDescendant<StackPanel>(
            this,
            panel => panel.Children.OfType<RadioButton>()
                .Any(button => string.Equals(button.Content?.ToString(), "Diagnostics", StringComparison.Ordinal)));
        var pageHost = FindVisualDescendant<Grid>(
            this,
            grid => grid.Children.OfType<DashboardView>().Any());

        if (navigationPanel is null || pageHost is null)
        {
            return;
        }

        var flightLoggerButton = new RadioButton
        {
            Content = "FlightLogger",
            Tag = "\uE774",
            GroupName = "Navigation",
            CommandParameter = "FlightLogger",
            ToolTip = "Log and track your real-life flights"
        };
        if (FindResource("CompactNavRadioStyle") is Style navigationStyle)
        {
            flightLoggerButton.Style = navigationStyle;
        }

        flightLoggerButton.SetBinding(
            ToggleButton.IsCheckedProperty,
            new Binding(nameof(MainWindowViewModel.ActivePage))
            {
                Mode = BindingMode.OneWay,
                Converter = (System.Windows.Data.IValueConverter)FindResource("StringEqualsConverter"),
                ConverterParameter = "FlightLogger"
            });
        flightLoggerButton.SetBinding(
            ButtonBase.CommandProperty,
            new Binding(nameof(MainWindowViewModel.NavigateCommand)));
        navigationPanel.Children.Add(flightLoggerButton);

        var flightLoggerPage = new FlightLoggerView();
        flightLoggerPage.SetBinding(
            UIElement.VisibilityProperty,
            new Binding("DataContext.ActivePage")
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(Window), 1),
                Converter = (System.Windows.Data.IValueConverter)FindResource("StringEqualsToVisibilityConverter"),
                ConverterParameter = "FlightLogger"
            });
        pageHost.Children.Add(flightLoggerPage);
        _flightLoggerPageInstalled = true;
    }

    private static T? FindVisualDescendant<T>(DependencyObject root, Func<T, bool> predicate)
        where T : DependencyObject
    {
        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < childCount; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match && predicate(match))
            {
                return match;
            }

            var descendant = FindVisualDescendant(child, predicate);
            if (descendant is not null)
            {
                return descendant;
            }
        }

        return null;
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
