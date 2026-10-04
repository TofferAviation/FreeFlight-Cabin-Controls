using System.Diagnostics;
using System.IO;
using System.Windows;
using FreeFlight.CabinControl.App.ViewModels;

namespace FreeFlight.CabinControl.App.Views;

public partial class SettingsTabbedView
{
    private bool _appearanceControlsReady;

    public SettingsTabbedView()
    {
        InitializeComponent();
        Loaded += (_, _) => _appearanceControlsReady = true;
    }

    public event RoutedEventHandler? PreviewUpdateRequested;

    public event RoutedEventHandler? CheckForUpdatesRequested;

    public event RoutedEventHandler? ThemeToggleRequested;

    public event RoutedEventHandler? AppearanceChanged;

    private void PreviewUpdateButton_Click(object sender, RoutedEventArgs e) =>
        PreviewUpdateRequested?.Invoke(this, e);

    private void CheckForUpdatesButton_Click(object sender, RoutedEventArgs e) =>
        CheckForUpdatesRequested?.Invoke(this, e);

    private void ToggleThemeButton_Click(object sender, RoutedEventArgs e) =>
        ThemeToggleRequested?.Invoke(this, e);

    private void ThemeModeButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel settings && sender is FrameworkElement { Tag: string theme })
        {
            settings.Theme = theme;
            RaiseAppearanceChanged(e);
        }
    }

    private void ApplyCustomAccentButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel settings)
        {
            settings.AccentPreset = "Custom";
            RaiseAppearanceChanged(e);
        }
    }

    private void AccentPresetButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel settings && sender is FrameworkElement { Tag: string preset })
        {
            settings.AccentPreset = preset;
            RaiseAppearanceChanged(e);
        }
    }

    private void UiScaleButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel settings && sender is FrameworkElement { Tag: string scale } && int.TryParse(scale, out var percentage))
        {
            settings.UiScalePercent = percentage;
            RaiseAppearanceChanged(e);
        }
    }

    private void CardDensityButton_Click(object sender, RoutedEventArgs e) =>
        ApplyTaggedSetting(sender, e, (settings, value) => settings.CardDensity = value);

    private void SidebarStyleButton_Click(object sender, RoutedEventArgs e) =>
        ApplyTaggedSetting(sender, e, (settings, value) => settings.SidebarStyle = value);

    private void DashboardStyleButton_Click(object sender, RoutedEventArgs e) =>
        ApplyTaggedSetting(sender, e, (settings, value) => settings.DashboardImageStyle = value);

    private void VisualEffectsButton_Click(object sender, RoutedEventArgs e) =>
        ApplyTaggedSetting(sender, e, (settings, value) => settings.VisualEffectsProfile = value);

    private void AppearanceControlChanged(object sender, RoutedEventArgs e) => RaiseAppearanceChanged(e);

    private void ApplyTaggedSetting(object sender, RoutedEventArgs e, Action<SettingsViewModel, string> apply)
    {
        if (DataContext is SettingsViewModel settings && sender is FrameworkElement { Tag: string value })
        {
            apply(settings, value);
            RaiseAppearanceChanged(e);
        }
    }

    private void RaiseAppearanceChanged(RoutedEventArgs e)
    {
        if (_appearanceControlsReady)
        {
            AppearanceChanged?.Invoke(this, e);
        }
    }

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
