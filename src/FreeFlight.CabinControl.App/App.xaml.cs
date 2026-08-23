using System.IO;
using System.Windows;
using System.Windows.Threading;
using FreeFlight.CabinControl.App.ViewModels;
using FreeFlight.CabinControl.Core.Persistence;

namespace FreeFlight.CabinControl.App;

public partial class App
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += HandleDispatcherException;

        var settingsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FreeFlight",
            "CabinControl");
        var settingsStore = new JsonSettingsStore(Path.Combine(settingsDirectory, "settings.json"));

        var settings = await settingsStore.LoadAsync();
        var viewModel = new MainWindowViewModel(settings, settingsStore);
        MainWindow = new MainWindow
        {
            DataContext = viewModel
        };
        MainWindow.Show();
    }

    private static void HandleDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            $"FreeFlight Cabin Control encountered an unexpected error.\n\n{e.Exception.Message}",
            "FreeFlight Cabin Control",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }
}
