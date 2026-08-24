using System.IO;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
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
        var localSafetyVideoPath = args.Length > 1
            ? Path.GetFullPath(args[1])
            : Path.Combine(outputDirectory, "BA_Safety_Video.mp4");
        if (args.Length > 1)
        {
            VerifyLocalVideoCanOpen(localSafetyVideoPath);
        }
        else
        {
            File.WriteAllBytes(localSafetyVideoPath, []);
        }

        var boardingMusicDirectory = args.Length > 2
            ? Path.GetFullPath(args[2])
            : Path.Combine(AppContext.BaseDirectory, "content-packs", "british-airways", "audio", "boarding");
        var boardingMusicPaths = new[]
        {
            Path.Combine(boardingMusicDirectory, "BA_Boarding_Program_01_Dvorak.mp3"),
            Path.Combine(boardingMusicDirectory, "BA_Boarding_Program_02_Brahms.mp3"),
            Path.Combine(boardingMusicDirectory, "BA_Boarding_Program_03_Tchaikovsky.mp3"),
            Path.Combine(boardingMusicDirectory, "BA_Boarding_Program_04_Flower_Duet.mp3")
        };
        foreach (var boardingMusicPath in boardingMusicPaths)
        {
            if (File.Exists(boardingMusicPath))
            {
                VerifyLocalAudioCanOpen(boardingMusicPath);
            }
            else if (args.Length > 2)
            {
                throw new FileNotFoundException("Expected boarding-music program was not installed.", boardingMusicPath);
            }
        }

        var tchaikovskyPath = boardingMusicPaths[2];
        var flowerDuetPath = boardingMusicPaths[3];

        var missingMediaViewModel = new CabinControlPanelViewModel(
            new AppSettings(),
            new JsonSettingsStore(Path.Combine(outputDirectory, "missing-media-settings.json")),
            new SharedStatusViewModel(),
            Path.Combine(outputDirectory, "missing-BA_Safety_Video.mp4"),
            Path.Combine(outputDirectory, "missing-boarding-music"));
        missingMediaViewModel.StartSafetyVideoCommand.Execute(null);
        if (missingMediaViewModel.IsSafetyVideoInProgress || missingMediaViewModel.QueueDepth != 0 ||
            !missingMediaViewModel.SafetyVideoPreviewStatus.Contains("not installed", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Missing local safety media did not remain safely stopped.");
        }

        var viewModel = new MainWindowViewModel(
            new AppSettings(),
            new JsonSettingsStore(settingsPath),
            Path.Combine(outputDirectory, "logs"),
            localSafetyVideoPath,
            boardingMusicDirectory);
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

        foreach (var page in new[] { "Airliners", "Passengers", "CabinPanel", "Audio", "Performance", "Settings" })
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

            if (page == "Passengers")
            {
                if (viewModel.Passengers.L1DoorOpen || !viewModel.Passengers.L2DoorOpen)
                {
                    throw new InvalidOperationException("Passenger Flow did not initialize with the L2-only preview scenario.");
                }

                var realOperationsSpeed = viewModel.Passengers.SpeedOptions.Single(option => option.Multiplier == 0.06d);
                viewModel.Passengers.SelectedSpeedOption = realOperationsSpeed;
                var etaParts = viewModel.Passengers.BoardingEta.Split(':');
                if (etaParts.Length != 2 ||
                    !int.TryParse(etaParts[0], out var etaMinutes) ||
                    !int.TryParse(etaParts[1], out var etaSeconds) ||
                    etaMinutes + (etaSeconds / 60d) is < 30d or > 45d)
                {
                    throw new InvalidOperationException(
                        $"Real Operations ETA was outside 30–45 minutes: {viewModel.Passengers.BoardingEta}.");
                }

                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.DataBind);
                Render(window, Path.Combine(outputDirectory, "passengers-real-operations.png"));
                viewModel.Passengers.SelectedSpeedOption = viewModel.Passengers.SpeedOptions.Single(option => option.Multiplier == 2d);

                viewModel.Passengers.StartPauseCommand.Execute(null);
                for (var index = 0; index < 12; index++)
                {
                    viewModel.Passengers.AdvancePreview(TimeSpan.FromSeconds(0.5d));
                }

                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.DataBind);
                if (viewModel.Passengers.PassengerMarkers.Count == 0 ||
                    viewModel.Passengers.BoardedPassengerCount + viewModel.Passengers.InCabinPassengerCount == 0 ||
                    viewModel.Passengers.PassengerMarkers.Any(marker => marker.DoorLabel != "L2"))
                {
                    throw new InvalidOperationException("L2-only Passenger Flow did not route every visible passenger through L2.");
                }


                if (viewModel.Passengers.PassengerMarkers
                        .Where(marker => !marker.IsWalking)
                        .Any(marker => Math.Abs(marker.X - marker.SeatX) > 0.001d ||
                                       Math.Abs(marker.Y - marker.SeatY) > 0.001d))
                {
                    throw new InvalidOperationException("A passenger marker was not centred on its assigned seat.");
                }

                if (!viewModel.Passengers.PassengerMarkers.Any(marker => marker.IsOccupyingSeat) ||
                    !viewModel.Passengers.PassengerMarkers.Any(marker => marker.IsSecured))
                {
                    throw new InvalidOperationException("Passenger Flow did not display both orange occupied and green secured seat states.");
                }

                Render(window, Path.Combine(outputDirectory, "passengers-l2-boarding.png"));
                viewModel.Passengers.L2DoorOpen = false;
                if (viewModel.Passengers.BoardingState != FreeFlight.CabinControl.Core.Passengers.BoardingRunState.WaitingForDoor)
                {
                    throw new InvalidOperationException("Passenger Flow did not hold boarding when every passenger door closed.");
                }

                viewModel.Passengers.L1DoorOpen = true;
                for (var index = 0; index < 6; index++)
                {
                    viewModel.Passengers.AdvancePreview(TimeSpan.FromSeconds(0.5d));
                }

                if (viewModel.Passengers.L1PassengerCount == 0)
                {
                    throw new InvalidOperationException("Passenger Flow did not reroute new passengers through L1.");
                }

                viewModel.Passengers.StartPauseCommand.Execute(null);
                viewModel.Passengers.ResetCommand.Execute(null);
                viewModel.Passengers.L1DoorOpen = true;
                viewModel.Passengers.L2DoorOpen = true;
                viewModel.Passengers.StartPauseCommand.Execute(null);
                for (var index = 0; index < 10; index++)
                {
                    viewModel.Passengers.AdvancePreview(TimeSpan.FromSeconds(0.5d));
                }

                var ticketRoutedPassengers = viewModel.Passengers.PassengerMarkers
                    .Where(marker => !string.IsNullOrWhiteSpace(marker.DoorLabel))
                    .ToArray();
                if (!ticketRoutedPassengers.Any(marker => marker.CabinClassName == "First") ||
                    !ticketRoutedPassengers.Any(marker => marker.CabinClassName != "First") ||
                    ticketRoutedPassengers.Any(marker =>
                        marker.DoorLabel != (marker.CabinClassName == "First" ? "L1" : "L2")))
                {
                    throw new InvalidOperationException(
                        "Two-door Passenger Flow did not follow First-to-L1 and Business/Economy-to-L2 ticket routing.");
                }

                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.DataBind);
                Render(window, Path.Combine(outputDirectory, "passengers-ticket-and-aisle-routing.png"));
                viewModel.Passengers.StartPauseCommand.Execute(null);
            }
            else if (page == "CabinPanel")
            {
                viewModel.CabinPanel.SelectPanelCommand.Execute("Main Menu");
                var displayControlsButton = FindVisualChild<Button>(
                    window,
                    button => Equals(button.CommandParameter, "Display Controls"));
                if (displayControlsButton is null)
                {
                    throw new InvalidOperationException("Could not resolve the immersive CACP Display Controls hit zone.");
                }

                var peer = new ButtonAutomationPeer(displayControlsButton);
                ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)).Invoke();
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                if (viewModel.CabinPanel.SelectedPanel != "Display Controls")
                {
                    throw new InvalidOperationException("The immersive CACP hit zone did not navigate to Display Controls.");
                }

                viewModel.CabinPanel.MainMenuCommand.Execute(null);
                var panelPages = new[]
                {
                    (Name: "CSCP Main Menu", Slug: "cscp-main-menu"),
                    (Name: "Main Menu", Slug: "cabin-controls-main-menu"),
                    (Name: "Lighting Control", Slug: "lighting"),
                    (Name: "Cabin Lighting", Slug: "cabin-lighting"),
                    (Name: "Entry Way Lights", Slug: "entry-way-lights"),
                    (Name: "Reading Lights", Slug: "reading-lights"),
                    (Name: "Service Call / Chime", Slug: "service-call-chime"),
                    (Name: "Cabin Temperature", Slug: "temperature"),
                    (Name: "Water / Waste Status", Slug: "water-waste"),
                    (Name: "Passenger Address", Slug: "passenger-address"),
                    (Name: "Cabin Door Status", Slug: "door-status"),
                    (Name: "Display Controls", Slug: "display-controls"),
                    (Name: "Boarding Music", Slug: "boarding-music"),
                    (Name: "Special Functions", Slug: "special-functions"),
                    (Name: "System Pop-up Windows", Slug: "system-pop-up-windows")
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

                var brightnessPointer = FindVisualChild<System.Windows.Shapes.Path>(
                    window,
                    path => Equals(path.Tag, "BrightnessPointer"));
                if (brightnessPointer is null)
                {
                    throw new InvalidOperationException("Could not resolve the Display Controls brightness pointer.");
                }

                var pointerAtSeventy = Canvas.GetLeft(brightnessPointer);
                viewModel.CabinPanel.ExecuteActionCommand.Execute("Display:BrightnessDown");
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.DataBind);
                var pointerAtSixty = Canvas.GetLeft(brightnessPointer);
                if (double.IsNaN(pointerAtSeventy) || double.IsNaN(pointerAtSixty) || pointerAtSixty >= pointerAtSeventy)
                {
                    throw new InvalidOperationException("The Display Controls pointer did not follow the brightness value.");
                }

                viewModel.CabinPanel.ExecuteActionCommand.Execute("PA:VolumeUp");
                viewModel.CabinPanel.ExecuteActionCommand.Execute("Music:Program3");
                if (viewModel.CabinPanel.PaVolumeLevel != 6 ||
                    viewModel.CabinPanel.DisplayBrightness != 60 ||
                    viewModel.CabinPanel.SelectedBoardingProgram != 3 ||
                    viewModel.CabinPanel.HasSelectedBoardingMusic != File.Exists(tchaikovskyPath))
                {
                    throw new InvalidOperationException("Cabin panel controls did not update their local preview state.");
                }

                if (File.Exists(tchaikovskyPath) &&
                    !viewModel.CabinPanel.SelectedBoardingProgramCredit.Contains("CC BY 4.0", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("The licensed Tchaikovsky boarding program was not resolved.");
                }

                viewModel.CabinPanel.ExecuteActionCommand.Execute("Music:Program4");
                if (File.Exists(flowerDuetPath))
                {
                    if (viewModel.CabinPanel.SelectedBoardingProgram != 4 ||
                        !viewModel.CabinPanel.HasSelectedBoardingMusic ||
                        !viewModel.CabinPanel.SelectedBoardingProgramCredit.Contains("CC BY 3.0", StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException("The licensed Flower Duet boarding program was not resolved.");
                    }

                    viewModel.CabinPanel.ExecuteActionCommand.Execute("Music:On");
                    window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                    if (!viewModel.CabinPanel.IsBoardingMusicPlaying)
                    {
                        throw new InvalidOperationException("Boarding Music Program 4 did not enter its playing state.");
                    }

                    viewModel.CabinPanel.ExecuteActionCommand.Execute("Music:Off");
                }

                viewModel.CabinPanel.MainMenuCommand.Execute(null);
                if (viewModel.CabinPanel.SelectedPanel != "CSCP Main Menu")
                {
                    throw new InvalidOperationException("Cabin panel did not return to its main menu.");
                }

                viewModel.CabinPanel.StartSafetyVideoCommand.Execute(null);
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Loaded);
                if (!viewModel.CabinPanel.IsSafetyVideoInProgress)
                {
                    throw new InvalidOperationException("The safety video test did not enter its in-progress state.");
                }

                if (!viewModel.CabinPanel.HasLocalSafetyVideo ||
                    !viewModel.CabinPanel.IsUsingLocalSafetyVideo ||
                    viewModel.CabinPanel.SafetyVideoLocalSource is null)
                {
                    throw new InvalidOperationException("The installed local BA safety video was not selected for playback.");
                }

                var announcementOverlay = FindVisualChild<Border>(
                    window,
                    border => Equals(border.Tag, "AnnouncementOverlay"));
                var announcementLabel = FindVisualChild<TextBlock>(
                    window,
                    textBlock => textBlock.Text == "Announcement in progress");
                if (announcementOverlay?.Background is not SolidColorBrush overlayBrush ||
                    overlayBrush.Color != Color.FromArgb(0xB3, 0x00, 0x00, 0x00) ||
                    announcementLabel?.Foreground is not SolidColorBrush labelBrush ||
                    labelBrush.Color != Colors.White)
                {
                    throw new InvalidOperationException("The dark Announcement in progress overlay does not match the approved treatment.");
                }

                var externalVideoButton = FindVisualChild<Button>(
                    window,
                    button => Equals(button.Content, "Open externally"));
                if (externalVideoButton is not null)
                {
                    throw new InvalidOperationException("The removed online-video action is still present in the Cabin Panel UI.");
                }

                var inlineVideoPlayer = FindVisualChild<MediaElement>(
                    window,
                    mediaElement => Equals(mediaElement.Tag, "InlineSafetyVideoPlayer"));
                var inlinePreview = FindVisualChild<Border>(
                    window,
                    border => Equals(border.Tag, "InlineSafetyVideoPreview"));
                var floatingPreviewLabel = FindVisualChild<TextBlock>(
                    window,
                    textBlock => textBlock.Text == "Safety video preview");
                var stopSafetyVideoButton = FindVisualChild<Button>(
                    window,
                    button => Equals(button.Content, "Stop Safety Video"));
                if (inlineVideoPlayer is null || inlineVideoPlayer.Visibility != Visibility.Visible ||
                    inlinePreview is null || Math.Abs(inlinePreview.ActualHeight - 148d) > 1d ||
                    floatingPreviewLabel is not null || stopSafetyVideoButton?.Visibility != Visibility.Visible)
                {
                    throw new InvalidOperationException("Safety video playback did not replace the LOCAL MP4 card preview in place.");
                }

                if (args.Length > 1)
                {
                    var sourceBeforeNavigation = inlineVideoPlayer.Source;
                    viewModel.NavigateCommand.Execute("Audio");
                    window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                    if (!viewModel.CabinPanel.IsSafetyVideoInProgress ||
                        !inlineVideoPlayer.IsLoaded ||
                        inlineVideoPlayer.Source != sourceBeforeNavigation)
                    {
                        throw new InvalidOperationException("Safety-video playback was interrupted by application navigation.");
                    }

                    viewModel.NavigateCommand.Execute("CabinPanel");
                    window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                    if (!viewModel.CabinPanel.IsSafetyVideoInProgress || inlineVideoPlayer.Source != sourceBeforeNavigation)
                    {
                        throw new InvalidOperationException("Safety-video playback was not preserved when returning to Cabin Panel.");
                    }
                }

                Render(window, Path.Combine(outputDirectory, "cabinpanel-safety-video-in-progress.png"));
                viewModel.CabinPanel.StopSafetyVideoCommand.Execute(null);
                if (viewModel.CabinPanel.IsSafetyVideoInProgress)
                {
                    throw new InvalidOperationException("The safety video test did not leave its in-progress state.");
                }
            }
            else if (page == "Audio")
            {
                var masterVolume = viewModel.Audio.MasterVolume;
                viewModel.Audio.BoardingMusicEnabled = true;
                viewModel.Audio.BoardingMusicVolume = 37;
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.DataBind);
                var expectedBoardingOutput = 0.37d * (masterVolume / 100d);
                if (Math.Abs(viewModel.CabinPanel.BoardingMusicOutputVolume - expectedBoardingOutput) > 0.001d)
                {
                    throw new InvalidOperationException("Master Audio did not scale the boarding-music output volume.");
                }

                viewModel.Audio.BoardingMusicCommand.Execute(null);
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                var firstRandomProgram = viewModel.CabinPanel.SelectedBoardingProgram;
                if (!viewModel.Audio.IsBoardingMusicInProgress ||
                    !viewModel.CabinPanel.IsBoardingMusicPlaying ||
                    !viewModel.CabinPanel.HasSelectedBoardingMusic ||
                    !viewModel.Audio.NowPlaying.Contains($"Program {firstRandomProgram}", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("The Audio boarding-music action did not start an installed random program.");
                }

                for (var index = 0; index < 8; index++)
                {
                    viewModel.Audio.AdvanceVuMeters();
                }

                if (viewModel.Audio.LeftMeterLevel <= 0d ||
                    viewModel.Audio.RightMeterLevel <= 0d ||
                    Math.Abs(viewModel.Audio.LeftMeterLevel - viewModel.Audio.RightMeterLevel) < 0.01d)
                {
                    throw new InvalidOperationException("The Master Audio left/right VU meters did not respond to playback.");
                }

                viewModel.Audio.MasterVolume = 0;
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.DataBind);
                if (Math.Abs(viewModel.CabinPanel.BoardingMusicOutputVolume) > 0.001d ||
                    viewModel.Audio.LeftMeterLevel > 0.001d ||
                    viewModel.Audio.RightMeterLevel > 0.001d)
                {
                    throw new InvalidOperationException("Muting Master Audio did not mute playback and clear both VU meters.");
                }

                viewModel.Audio.MasterVolume = masterVolume;
                for (var index = 0; index < 8; index++)
                {
                    viewModel.Audio.AdvanceVuMeters();
                }

                viewModel.Audio.BoardingMusicEnabled = false;
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.DataBind);
                if (Math.Abs(viewModel.CabinPanel.BoardingMusicOutputVolume) > 0.001d ||
                    !viewModel.Audio.NowPlayingDescription.Contains("muted", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("Disabling Boarding Music did not mute active playback.");
                }

                viewModel.Audio.BoardingMusicEnabled = true;
                Render(window, Path.Combine(outputDirectory, "audio-boarding-music-in-progress.png"));
                viewModel.Audio.BoardingMusicCommand.Execute(null);
                viewModel.Audio.BoardingMusicCommand.Execute(null);
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                if (!viewModel.Audio.IsBoardingMusicInProgress ||
                    viewModel.CabinPanel.SelectedBoardingProgram == firstRandomProgram)
                {
                    throw new InvalidOperationException("Consecutive Audio boarding sessions did not choose a new random program.");
                }

                viewModel.Audio.NowPlayingCommand.Execute(null);
                if (viewModel.Audio.IsBoardingMusicInProgress)
                {
                    throw new InvalidOperationException("The Audio Now Playing control did not stop boarding music.");
                }

                viewModel.Audio.SafetyDemonstrationEnabled = true;
                viewModel.Audio.SafetyDemonstrationVolume = 32;
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.DataBind);
                var expectedSafetyOutput = 0.32d * (masterVolume / 100d);
                if (Math.Abs(viewModel.CabinPanel.SafetyVideoVolume - expectedSafetyOutput) > 0.001d)
                {
                    throw new InvalidOperationException("Master Audio did not scale the safety-demonstration MP4 volume.");
                }

                viewModel.Audio.SafetyDemonstrationCommand.Execute(null);
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                if (!viewModel.Audio.IsSafetyDemonstrationInProgress ||
                    !viewModel.CabinPanel.IsSafetyVideoInProgress ||
                    viewModel.Audio.NowPlaying != viewModel.CabinPanel.SafetyVideoTitle)
                {
                    throw new InvalidOperationException("The Audio page play action did not start the shared safety demonstration.");
                }

                var audioAnnouncementBanner = FindVisualChild<Border>(
                    window,
                    border => Equals(border.Tag, "AudioAnnouncementBanner"));
                var audioAnnouncementLabel = audioAnnouncementBanner is null
                    ? null
                    : FindVisualChild<TextBlock>(
                        audioAnnouncementBanner,
                        textBlock => textBlock.Text == "Announcement in progress");
                var safetyMediaElement = FindVisualChild<MediaElement>(
                    window,
                    mediaElement => Equals(mediaElement.Tag, "InlineSafetyVideoPlayer"));
                if (audioAnnouncementBanner?.Visibility != Visibility.Visible ||
                    audioAnnouncementBanner.Background is not SolidColorBrush bannerBrush ||
                    bannerBrush.Color != Color.FromRgb(0xE7, 0xB8, 0x3D) ||
                    audioAnnouncementBanner.ActualWidth < 1000d ||
                    audioAnnouncementLabel is null ||
                    safetyMediaElement is null ||
                    Math.Abs(safetyMediaElement.Volume - expectedSafetyOutput) > 0.001d)
                {
                    throw new InvalidOperationException("The Audio announcement banner or live MP4 volume binding is incorrect.");
                }

                viewModel.Audio.SafetyDemonstrationEnabled = false;
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.DataBind);
                if (Math.Abs(safetyMediaElement.Volume) > 0.001d ||
                    !viewModel.Audio.NowPlayingDescription.Contains("muted", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("Disabling Safety Demonstration did not mute the active MP4.");
                }

                viewModel.Audio.SafetyDemonstrationEnabled = true;
                Render(window, Path.Combine(outputDirectory, "audio-safety-announcement-in-progress.png"));
                viewModel.Audio.SafetyDemonstrationCommand.Execute(null);
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.DataBind);
                if (viewModel.Audio.IsSafetyDemonstrationInProgress ||
                    audioAnnouncementBanner.Visibility != Visibility.Collapsed)
                {
                    throw new InvalidOperationException("The Audio page stop action did not end the safety demonstration.");
                }
            }
        }

        window.Close();
        application.Shutdown();
        Console.WriteLine($"Rendered 28 visual checks to {outputDirectory}");
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

    private static void VerifyLocalVideoCanOpen(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("The requested local safety-video test file was not found.", path);
        }

        var opened = false;
        string? failure = null;
        var frame = new System.Windows.Threading.DispatcherFrame();
        var timeout = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(10)
        };
        var player = new MediaPlayer { Volume = 0 };
        player.MediaOpened += (_, _) =>
        {
            opened = player.NaturalVideoWidth > 0 && player.NaturalVideoHeight > 0;
            if (!opened)
            {
                failure = "Windows opened the file but did not detect a video stream.";
            }

            frame.Continue = false;
        };
        player.MediaFailed += (_, eventArgs) =>
        {
            failure = eventArgs.ErrorException?.Message ?? "Windows media playback rejected the file.";
            frame.Continue = false;
        };
        timeout.Tick += (_, _) =>
        {
            failure = "Timed out while Windows opened the local MP4.";
            frame.Continue = false;
        };

        player.Open(new Uri(path, UriKind.Absolute));
        timeout.Start();
        System.Windows.Threading.Dispatcher.PushFrame(frame);
        timeout.Stop();
        player.Close();

        if (!opened)
        {
            throw new InvalidOperationException($"Local safety-video playback validation failed: {failure}");
        }

        Console.WriteLine($"Validated local MP4 playback: {path}");
    }

    private static void VerifyLocalAudioCanOpen(string path)
    {
        var opened = false;
        string? failure = null;
        var frame = new System.Windows.Threading.DispatcherFrame();
        var timeout = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(10)
        };
        var player = new MediaPlayer { Volume = 0 };
        player.MediaOpened += (_, _) =>
        {
            opened = player.NaturalDuration.HasTimeSpan && player.NaturalDuration.TimeSpan > TimeSpan.Zero;
            if (!opened)
            {
                failure = "Windows opened the file but did not detect an audio duration.";
            }

            frame.Continue = false;
        };
        player.MediaFailed += (_, eventArgs) =>
        {
            failure = eventArgs.ErrorException?.Message ?? "Windows media playback rejected the file.";
            frame.Continue = false;
        };
        timeout.Tick += (_, _) =>
        {
            failure = "Timed out while Windows opened the local boarding-music file.";
            frame.Continue = false;
        };

        player.Open(new Uri(path, UriKind.Absolute));
        timeout.Start();
        System.Windows.Threading.Dispatcher.PushFrame(frame);
        timeout.Stop();
        player.Close();

        if (!opened)
        {
            throw new InvalidOperationException($"Local boarding-music validation failed: {failure}");
        }

        Console.WriteLine($"Validated local boarding-music playback: {path}");
    }

    private static T? FindVisualChild<T>(DependencyObject parent, Predicate<T> predicate)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match && predicate(match))
            {
                return match;
            }

            var descendant = FindVisualChild(child, predicate);
            if (descendant is not null)
            {
                return descendant;
            }
        }

        return null;
    }
}
