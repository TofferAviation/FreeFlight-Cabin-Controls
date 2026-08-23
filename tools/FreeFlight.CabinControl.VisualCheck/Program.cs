using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FreeFlight.CabinControl.App.ViewModels;
using FreeFlight.CabinControl.App.Services;
using FreeFlight.CabinControl.Core.Configuration;
using FreeFlight.CabinControl.Core.Persistence;
using CabinControlApplication = FreeFlight.CabinControl.App.App;
using CabinControlWindow = FreeFlight.CabinControl.App.MainWindow;

namespace FreeFlight.CabinControl.VisualCheck;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var outputDirectory = args.Length > 0
            ? Path.GetFullPath(args[0])
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "visual-check"));
        Directory.CreateDirectory(outputDirectory);

        var application = new CabinControlApplication();
        application.InitializeComponent();

        try
        {
            var playbackDevices = new AudioOutputDeviceService().GetActiveOutputDevices();
            Console.WriteLine($"Detected {playbackDevices.Count} active Windows playback endpoints for the visual check.");
        }
        catch (Exception exception)
        {
            Console.WriteLine($"Windows playback endpoint enumeration unavailable in this session: {exception.Message}");
        }

        var settingsPath = Path.Combine(outputDirectory, "visual-check-settings.json");
        var viewModel = new MainWindowViewModel(
            new AppSettings(),
            new JsonSettingsStore(settingsPath),
            Path.Combine(outputDirectory, "logs"));
        var window = new CabinControlWindow
        {
            DataContext = viewModel,
            Width = 1540,
            Height = 900,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -20000,
            Top = 0,
            ShowActivated = false,
            ShowInTaskbar = false
        };

        window.Show();
        Render(window, Path.Combine(outputDirectory, "dashboard.png"));

        foreach (var page in new[] { "Airliners", "Audio", "Performance", "Settings" })
        {
            viewModel.NavigateCommand.Execute(page);
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Loaded);
            Render(window, Path.Combine(outputDirectory, $"{page.ToLowerInvariant()}.png"));
        }

        window.Close();
        application.Shutdown();
        Console.WriteLine($"Rendered five visual checks to {outputDirectory}");
        return 0;
    }

    private static void Render(Window window, string path)
    {
        window.Measure(new Size(window.Width, window.Height));
        window.Arrange(new Rect(0, 0, window.Width, window.Height));
        window.UpdateLayout();

        var bitmap = new RenderTargetBitmap(
            (int)window.Width,
            (int)window.Height,
            96,
            96,
            PixelFormats.Pbgra32);
        bitmap.Render(window);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
