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

        foreach (var page in new[] { "Airliners", "CabinPanel", "Audio", "Performance", "Settings" })
        {
            viewModel.NavigateCommand.Execute(page);
            if (page == "Airliners")
            {
                var britishAirways = viewModel.Airliners.VisibleAirlines.Single(profile => profile.Icao == "BAW");
                var norwegian = viewModel.Airliners.VisibleAirlines.Single(profile => profile.Icao == "NOZ");
                if (!britishAirways.HasLogo || !norwegian.HasLogo)
                {
                    throw new InvalidOperationException("Expected BAW and NOZ ICAO logo mappings were not resolved.");
                }

                viewModel.Airliners.SelectAirlineCommand.Execute(britishAirways);
            }
            else if (page == "CabinPanel")
            {
                viewModel.CabinPanel.QueueCommand.Execute("Safety demonstration video");
                if (viewModel.CabinPanel.QueueDepth != 1)
                {
                    throw new InvalidOperationException("Cabin panel event queue did not accept a safety video event.");
                }
            }

            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Loaded);
            Render(window, Path.Combine(outputDirectory, $"{page.ToLowerInvariant()}.png"));

            if (page == "CabinPanel")
            {
                var panelPages = new[]
                {
                    (Name: "Lighting Control", Slug: "lighting"),
                    (Name: "Service Call / Chime", Slug: "service-call-chime"),
                    (Name: "Cabin Temperature", Slug: "temperature"),
                    (Name: "Water / Waste Status", Slug: "water-waste"),
                    (Name: "Passenger Address", Slug: "passenger-address"),
                    (Name: "Cabin Door Status", Slug: "door-status"),
                    (Name: "Display Controls", Slug: "display-controls"),
                    (Name: "Boarding Music", Slug: "boarding-music"),
                    (Name: "Special Functions", Slug: "special-functions")
                };

                foreach (var panelPage in panelPages)
                {
                    viewModel.CabinPanel.SelectPanelCommand.Execute(panelPage.Name);
                    if (viewModel.CabinPanel.SelectedPanel != panelPage.Name)
                    {
                        throw new InvalidOperationException($"Cabin panel did not navigate to {panelPage.Name}.");
                    }

                    window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Loaded);
                    Render(window, Path.Combine(outputDirectory, $"cabinpanel-{panelPage.Slug}.png"));
                }

                viewModel.CabinPanel.ExecuteActionCommand.Execute("PA:VolumeUp");
                viewModel.CabinPanel.ExecuteActionCommand.Execute("Display:BrightnessDown");
                viewModel.CabinPanel.ExecuteActionCommand.Execute("Music:Program2");
                if (viewModel.CabinPanel.PaVolumeLevel != 6 ||
                    viewModel.CabinPanel.DisplayBrightness != 60 ||
                    viewModel.CabinPanel.SelectedBoardingProgram != 2)
                {
                    throw new InvalidOperationException("Cabin panel controls did not update their local preview state.");
                }

                viewModel.CabinPanel.MainMenuCommand.Execute(null);
                if (viewModel.CabinPanel.SelectedPanel != "Main Menu")
                {
                    throw new InvalidOperationException("Cabin panel did not return to its main menu.");
                }
            }
        }

        window.Close();
        application.Shutdown();
        Console.WriteLine($"Rendered 15 visual checks to {outputDirectory}");
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
