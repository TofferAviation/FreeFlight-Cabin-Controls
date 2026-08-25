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
using FreeFlight.CabinControl.Core.Operations;
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
            if (args.Length > 2 && File.Exists(boardingMusicPath))
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

        Console.WriteLine("Verifying missing-media behavior.");
        var missingMediaViewModel = new CabinControlPanelViewModel(
            new AppSettings(),
            new JsonSettingsStore(Path.Combine(outputDirectory, "missing-media-settings.json")),
            new SharedStatusViewModel(),
            Path.Combine(outputDirectory, "missing-BA_Safety_Video.mp4"),
            Path.Combine(outputDirectory, "missing-boarding-music"));
        Console.WriteLine("Missing-media view model created.");
        missingMediaViewModel.StartSafetyVideoCommand.Execute(null);
        Console.WriteLine("Missing-media command completed.");
        if (missingMediaViewModel.IsSafetyVideoInProgress || missingMediaViewModel.QueueDepth != 0 ||
            !missingMediaViewModel.SafetyVideoPreviewStatus.Contains("not installed", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Missing local safety media did not remain safely stopped.");
        }

        Console.WriteLine("Creating the application view model.");
        var fixedClockTime = new DateTimeOffset(
            new DateTime(2026, 8, 25, 17, 50, 0, DateTimeKind.Local));
        var printerService = new FakeBoardingPassPrinterService();
        var viewModel = new MainWindowViewModel(
            new AppSettings(),
            new JsonSettingsStore(settingsPath),
            Path.Combine(outputDirectory, "logs"),
            localSafetyVideoPath,
            boardingMusicDirectory,
            new FakeSimBriefClient(),
            new FixedOperationsClock(fixedClockTime),
            printerService);
        Console.WriteLine("Application view model created; constructing the window.");
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
        Console.WriteLine("Application window constructed.");

        Console.WriteLine("Opening the application window for visual verification.");
        window.Show();
        Console.WriteLine("Application window opened; rendering Overview.");
        if (viewModel.Operations.CurrentClockTime != "17:50:00" ||
            viewModel.Operations.ClockSourceLabel != "LOCAL TIME" ||
            viewModel.Operations.TurnaroundStartsAt != "17:30" ||
            viewModel.Operations.GateOpensAt != "17:35" ||
            viewModel.Operations.BoardingBeginsAt != "17:45")
        {
            throw new InvalidOperationException("The local operations clock or fallback turnaround schedule was not initialized correctly.");
        }

        Render(window, Path.Combine(outputDirectory, "dashboard.png"));

        var dashboardView = FindVisualChild<FreeFlight.CabinControl.App.Views.DashboardView>(window, _ => true);
        var overviewGateToggle = dashboardView is null
            ? null
            : FindVisualChild<Button>(dashboardView, button =>
                ReferenceEquals(button.Command, viewModel.Operations.ToggleGateCommand));
        var gateWorkspaceButton = dashboardView is null
            ? null
            : FindVisualChild<Button>(dashboardView, button => Equals(button.CommandParameter, "GateDesk"));
        var gateHeaderLabel = FindVisualChild<TextBlock>(window, textBlock => Equals(textBlock.Tag, "GateHeaderLabel"));
        if (dashboardView is null || overviewGateToggle is not null || gateWorkspaceButton is null ||
            gateHeaderLabel?.Text != "GATE")
        {
            throw new InvalidOperationException("Overview still exposes a gate-state action or the route gate header is incomplete.");
        }

        viewModel.NavigateCommand.Execute("GateDesk");
        viewModel.Operations.ToggleGateCommand.Execute(null);
        if (viewModel.ActivePage != "GateLogin" ||
            viewModel.CurrentPage != viewModel.GateLogin ||
            viewModel.GateLogin.IsAuthenticated ||
            viewModel.Operations.IsGateOpen)
        {
            throw new InvalidOperationException("The signed-out gate workspace was not locked behind Gate Login.");
        }

        viewModel.NavigateCommand.Execute("IportDcs");
        if (viewModel.ActivePage != "GateLogin" || viewModel.CurrentPage != viewModel.GateLogin)
        {
            throw new InvalidOperationException("Iport DCS was accessible without an authenticated gate session.");
        }

        window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Loaded);
        Render(window, Path.Combine(outputDirectory, "gate-login.png"));
        var gateLoginWordmark = FindVisualChild<Grid>(
            window,
            grid => Equals(grid.Tag, "GateLoginBritishAirwaysWordmark"));
        if (gateLoginWordmark is null)
        {
            throw new InvalidOperationException("The repaired British Airways gate-login wordmark was not rendered.");
        }

        viewModel.GateLogin.EmployeeId = "FF042";
        viewModel.GateLogin.Password = "preview";
        viewModel.GateLogin.SignInCommand.Execute(null);
        if (!viewModel.GateLogin.IsAuthenticated || viewModel.ActivePage != "GateDesk")
        {
            throw new InvalidOperationException("The dummy gate login did not unlock the gate workspace.");
        }

        if (viewModel.Passengers.BookedPassengerCount != 0 ||
            viewModel.Passengers.PassengerManifest.Count != 0 ||
            viewModel.Operations.PassengerRecords.Count != 0 ||
            viewModel.Operations.VisiblePassengers.Count != 0 ||
            viewModel.Operations.SelectedPassenger is not null ||
            !viewModel.Operations.IsPassengerListEmpty)
        {
            throw new InvalidOperationException(
                "Passenger identities were generated before SimBrief or a manual passenger load was supplied.");
        }

        foreach (var emptyPassengerPage in new[] { "GateDesk", "IportDcs", "PassengerManifest", "BoardingPasses" })
        {
            viewModel.NavigateCommand.Execute(emptyPassengerPage);
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.DataBind);
            Render(window, Path.Combine(outputDirectory, $"{emptyPassengerPage.ToLowerInvariant()}-no-passenger-list.png"));
        }

        viewModel.Passengers.BookedPassengerCount = 228;
        if (viewModel.Passengers.PassengerManifest.Count != 228 ||
            viewModel.Operations.PassengerRecords.Count != 228 ||
            viewModel.Operations.IsPassengerListEmpty)
        {
            throw new InvalidOperationException("A manual passenger count did not create the shared passenger manifest.");
        }

        viewModel.NavigateCommand.Execute("GateLogin");
        window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Loaded);
        Render(window, Path.Combine(outputDirectory, "gate-session.png"));

        foreach (var page in new[]
                 {
                     "GateDesk", "IportDcs", "PassengerManifest", "BoardingPasses", "Airliners", "Passengers", "CabinPanel",
                     "Audio", "Performance", "Settings"
                 })
        {
            viewModel.NavigateCommand.Execute(page);
            if (page == "GateDesk")
            {
                var passenger = viewModel.Operations.PassengerRecords.First();
                var boardedBefore = viewModel.Operations.BoardedPassengers;
                viewModel.Operations.ToggleGateCommand.Execute(null);
                viewModel.Operations.SelectPassengerCommand.Execute(passenger);
                viewModel.Operations.CheckInPassengerCommand.Execute(passenger);
                viewModel.Operations.PrintBoardingPassCommand.Execute(passenger);
                viewModel.Operations.BoardPassengerCommand.Execute(passenger);
                if (!viewModel.Operations.IsGateOpen ||
                    !passenger.IsCheckedIn ||
                    !passenger.IsBoarded ||
                    passenger.BoardingPassStatus != "Printed" ||
                    printerService.PrintCount != 1 ||
                    viewModel.Operations.AvailablePrinters.Count != 2 ||
                    viewModel.Operations.BoardedPassengers != boardedBefore + 1 ||
                    viewModel.Passengers.BoardedPassengerCount != boardedBefore + 1)
                {
                    throw new InvalidOperationException(
                        "Gate Desk check-in, print, and cabin boarding did not update their shared passenger state.");
                }
            }
            else if (page == "IportDcs")
            {
                if (!viewModel.IportDcs.IsAvailable ||
                    viewModel.CurrentPage != viewModel.IportDcs ||
                    viewModel.IportDcs.Flights.Count < 3 ||
                    viewModel.IportDcs.Operations != viewModel.Operations ||
                    viewModel.IportDcs.ServiceMenuEntries.Count != 15 ||
                    viewModel.IportDcs.ServiceMenuEntries.Count(entry => !entry.IsHeader) != 12)
                {
                    throw new InvalidOperationException("Iport DCS was not unlocked as a shared gate-session workspace.");
                }

                viewModel.IportDcs.ToggleServiceMenuCommand.Execute(null);
                var loadControlService = viewModel.IportDcs.ServiceMenuEntries
                    .Single(entry => entry.Module == IportDcsViewModel.LoadControlModule);
                viewModel.IportDcs.SelectServiceCommand.Execute(loadControlService);
                if (viewModel.IportDcs.IsServiceMenuOpen ||
                    viewModel.IportDcs.ActiveModule != IportDcsViewModel.LoadControlModule ||
                    viewModel.IportDcs.ActiveServiceLabel != "Load Control" ||
                    !viewModel.IportDcs.IsLoadControlService)
                {
                    throw new InvalidOperationException("The grouped Res2 services menu did not switch to Load Control.");
                }

                viewModel.IportDcs.SelectModuleCommand.Execute(IportDcsViewModel.CheckInModule);
                viewModel.IportDcs.SelectPassengerCommand.Execute(viewModel.Operations.PassengerRecords.Skip(2).First());
            }
            else if (page == "PassengerManifest")
            {
                var passenger = viewModel.Operations.PassengerRecords.Skip(5).First();
                viewModel.Operations.SelectPassengerCommand.Execute(passenger);
                viewModel.Operations.SearchText = passenger.BookingReference;
                if (viewModel.Operations.VisiblePassengers.Count != 1 ||
                    viewModel.Operations.SelectedPassenger != passenger ||
                    string.IsNullOrWhiteSpace(passenger.DocumentNumber) ||
                    string.IsNullOrWhiteSpace(passenger.Email))
                {
                    throw new InvalidOperationException("Passenger Manifest filtering or passenger detail selection failed.");
                }

                viewModel.Operations.SearchText = string.Empty;
            }
            else if (page == "BoardingPasses")
            {
                var tickets = viewModel.Operations.PassengerRecords;
                if (tickets.Select(passenger => passenger.TicketNumber).Distinct().Count() != tickets.Count ||
                    tickets.Select(passenger => passenger.BookingReference).Distinct().Count() != tickets.Count)
                {
                    throw new InvalidOperationException("Boarding passes did not receive unique ticket identities.");
                }

                var firstPassenger = tickets.First(passenger => passenger.CabinMarketingName == "First");
                var economyPassenger = tickets.First(passenger => passenger.CabinMarketingName == "World Traveller");
                if (firstPassenger.CabinMarketingName == economyPassenger.CabinMarketingName ||
                    firstPassenger.BoardingBarcodeCells.SequenceEqual(economyPassenger.BoardingBarcodeCells))
                {
                    throw new InvalidOperationException("Passenger cabin branding or ticket QR data was not passenger-specific.");
                }

                viewModel.Operations.SelectPassengerCommand.Execute(firstPassenger);
            }
            else if (page == "Airliners")
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
            else if (page == "Settings")
            {
                if (viewModel.Settings.CabinLayoutProfiles.Count != 3 ||
                    viewModel.Settings.CabinLayoutProfiles.Select(profile => profile.Id).Distinct().Count() != 3)
                {
                    throw new InvalidOperationException("The three stable 777 cabin layout profiles were not available.");
                }
            }

            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Loaded);
            if (page == "BoardingPasses")
            {
                var headerWordmark = FindVisualChild<Image>(
                    window,
                    image => Equals(image.Tag, "HeaderBritishAirwaysWordmark"));
                var ticketWordmark = FindVisualChild<Image>(
                    window,
                    image => Equals(image.Tag, "BoardingPassBritishAirwaysWordmark"));
                var oneworldBadge = FindVisualChild<Grid>(
                    window,
                    grid => Equals(grid.Tag, "OneworldBadge"));
                var stubBarcode = FindVisualChild<ItemsControl>(
                    window,
                    itemsControl => Equals(itemsControl.Tag, "BoardingPassStubBarcode"));
                if (headerWordmark?.Source?.ToString().EndsWith("/BAW.png", StringComparison.OrdinalIgnoreCase) != true ||
                    ticketWordmark?.Source?.ToString().EndsWith("/BAW.png", StringComparison.OrdinalIgnoreCase) != true ||
                    oneworldBadge is null ||
                    stubBarcode is null)
                {
                    throw new InvalidOperationException(
                        "The shared British Airways wordmark, clean oneworld badge, or retained ticket-stub barcode was not rendered.");
                }
            }

            Render(window, Path.Combine(outputDirectory, $"{page.ToLowerInvariant()}.png"));

            if (page == "GateDesk")
            {
                viewModel.Passengers.ResetCommand.Execute(null);
                if (viewModel.Operations.IsGateOpen)
                {
                    viewModel.Operations.ToggleGateCommand.Execute(null);
                }
            }
            else if (page == "IportDcs")
            {
                foreach (var module in new[]
                         {
                             IportDcsViewModel.BoardingModule,
                             IportDcsViewModel.SeatmapModule,
                             IportDcsViewModel.LoadControlModule,
                             IportDcsViewModel.FlightMonitorModule
                         })
                {
                    viewModel.IportDcs.SelectModuleCommand.Execute(module);
                    window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.DataBind);
                    Render(window, Path.Combine(outputDirectory, $"iportdcs-{module.Replace(" ", "-").ToLowerInvariant()}.png"));
                }

                viewModel.IportDcs.SelectModuleCommand.Execute(IportDcsViewModel.LoadControlModule);
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.DataBind);

                var originalDow = viewModel.IportDcs.DryOperatingWeightKg;
                var originalDoi = viewModel.IportDcs.DryOperatingIndex;
                var originalTakeoffFuel = viewModel.IportDcs.TakeoffFuelKg;
                var originalTripFuel = viewModel.IportDcs.TripFuelKg;
                var originalTaxiFuel = viewModel.IportDcs.TaxiFuelKg;
                var originalBoardingPoint = viewModel.IportDcs.BoardingPoint;
                var originalDestination = viewModel.IportDcs.Destination;
                var originalTakeoffWeight = viewModel.IportDcs.TakeoffWeightKg;
                var originalEnvelopeX = viewModel.IportDcs.EnvelopeIndexX;
                var originalTakeoffMarkerTop = viewModel.IportDcs.EnvelopeTakeoffMarkerTop;

                viewModel.IportDcs.DryOperatingWeightKg += 5_000;
                viewModel.IportDcs.DryOperatingIndex += 10d;
                viewModel.IportDcs.TakeoffFuelKg += 1_000;
                viewModel.IportDcs.TripFuelKg += 500;
                viewModel.IportDcs.TaxiFuelKg += 200;
                viewModel.IportDcs.AdditionalWeightKg = 300;
                viewModel.IportDcs.BoardingPoint = "FRA";
                viewModel.IportDcs.Destination = "OSL";

                if (viewModel.IportDcs.TakeoffWeightKg != originalTakeoffWeight + 6_300 ||
                    viewModel.IportDcs.EnvelopeIndexX <= originalEnvelopeX ||
                    viewModel.IportDcs.EnvelopeTakeoffMarkerTop >= originalTakeoffMarkerTop ||
                    viewModel.IportDcs.Flights.First().Destination != "OSL" ||
                    !viewModel.IportDcs.BoardingPointLabel.StartsWith("FRA", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Manual iPortflight load, route, or envelope inputs did not recalculate immediately.");
                }

                viewModel.IportDcs.DryOperatingWeightKg = originalDow;
                viewModel.IportDcs.DryOperatingIndex = originalDoi;
                viewModel.IportDcs.TakeoffFuelKg = originalTakeoffFuel;
                viewModel.IportDcs.TripFuelKg = originalTripFuel;
                viewModel.IportDcs.TaxiFuelKg = originalTaxiFuel;
                viewModel.IportDcs.AdditionalWeightKg = 0;
                viewModel.IportDcs.BoardingPoint = originalBoardingPoint;
                viewModel.IportDcs.Destination = originalDestination;

                var liveFlight = viewModel.IportDcs.Flights.First(flight => flight.IsLive);
                var dispatcherFlight = viewModel.IportDcs.Flights.First(flight => flight.FlightNumber == "BA281");
                var secondDispatcherFlight = viewModel.IportDcs.Flights.First(flight => flight.FlightNumber == "BA274");
                viewModel.IportDcs.SelectedFlight = dispatcherFlight;
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.DataBind);
                Render(window, Path.Combine(outputDirectory, "iportdcs-load-control-ba281.png"));
                var dispatcherOriginalDow = viewModel.IportDcs.DryOperatingWeightKg;
                var dispatcherOriginalDestination = viewModel.IportDcs.Destination;
                viewModel.IportDcs.DryOperatingWeightKg = dispatcherOriginalDow + 725;
                viewModel.IportDcs.Destination = "SFO";
                viewModel.IportDcs.SelectedFlight = secondDispatcherFlight;
                if (viewModel.IportDcs.Destination != "LAS" || viewModel.IportDcs.SelectedBookedPassengers != 231)
                {
                    throw new InvalidOperationException("The second dispatcher flight did not open its independent Load Control workspace.");
                }

                viewModel.IportDcs.SelectedFlight = dispatcherFlight;
                if (viewModel.IportDcs.DryOperatingWeightKg != dispatcherOriginalDow + 725 ||
                    viewModel.IportDcs.Destination != "SFO" ||
                    !viewModel.IportDcs.CommandStatus.Contains("dispatcher flight", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("Dispatcher flight load edits were not retained while switching flights.");
                }

                viewModel.IportDcs.DryOperatingWeightKg = dispatcherOriginalDow;
                viewModel.IportDcs.Destination = dispatcherOriginalDestination;
                viewModel.IportDcs.SelectedFlight = liveFlight;

                viewModel.IportDcs.LoadActionCommand.Execute("Load sheet finalized for the active flight.");
                if (!viewModel.IportDcs.CommandStatus.Contains("finalized", StringComparison.OrdinalIgnoreCase) ||
                    viewModel.IportDcs.MaxTakeoffWeightKg <= viewModel.IportDcs.TakeoffWeightKg ||
                    viewModel.IportDcs.LandingWeightKg >= viewModel.IportDcs.TakeoffWeightKg)
                {
                    throw new InvalidOperationException("The iPortflight load calculations or action controls are not operational.");
                }

                viewModel.IportDcs.SelectModuleCommand.Execute(IportDcsViewModel.LoadPassengerModule);
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.DataBind);
                if (!viewModel.IportDcs.IsLoadControlPlaceholder)
                {
                    throw new InvalidOperationException("The retained Load Control reference tabs did not open their placeholder workspace.");
                }

                Render(window, Path.Combine(outputDirectory, "iportdcs-load-control-passenger-placeholder.png"));

                viewModel.IportDcs.ToggleServiceMenuCommand.Execute(null);
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.DataBind);
                var servicesMenu = FindVisualChild<Border>(
                    window,
                    border => Equals(border.Tag, "IportServiceMenu"));
                if (servicesMenu?.Visibility != Visibility.Visible)
                {
                    throw new InvalidOperationException("The complete Res2 services dropdown was not rendered.");
                }

                Render(window, Path.Combine(outputDirectory, "iportdcs-service-menu.png"));
                viewModel.IportDcs.ToggleServiceMenuCommand.Execute(null);

                var iportWorkspace = FindVisualChild<Border>(
                    window,
                    border => Equals(border.Tag, "IportDcsWorkspace"));
                if (iportWorkspace is null)
                {
                    throw new InvalidOperationException("The coded Iport DCS workspace was not rendered.");
                }

                var loadWorkspace = FindVisualChild<Grid>(
                    window,
                    grid => Equals(grid.Tag, "IportLoadControlWorkspace"));
                var envelopeChart = FindVisualChild<Canvas>(
                    window,
                    canvas => Equals(canvas.Tag, "IportEnvelopeChart"));
                var systemFooter = FindVisualChild<Border>(
                    window,
                    border => Equals(border.Tag, "IportSystemFooter"));
                var printerControl = FindVisualChild<Button>(
                    window,
                    button => Equals(button.Tag, "IportPrinterControl"));
                var powerControl = FindVisualChild<Button>(
                    window,
                    button => Equals(button.Tag, "IportPowerControl"));
                var dispatcherFlights = FindVisualChild<ListBox>(
                    window,
                    listBox => Equals(listBox.Tag, "IportDispatcherFlights"));
                if (loadWorkspace is null || envelopeChart is null || systemFooter is null || printerControl is null || powerControl is null || dispatcherFlights?.Items.Count != 3)
                {
                    throw new InvalidOperationException("The authentic iPortflight load-control workspace, dispatcher flight list, and system footer were not rendered.");
                }
            }
            else if (page == "BoardingPasses")
            {
                var clubWorldPassenger = viewModel.Operations.PassengerRecords
                    .First(passenger => passenger.CabinMarketingName == "Club World");
                viewModel.Operations.SelectPassengerCommand.Execute(clubWorldPassenger);
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.DataBind);
                Render(window, Path.Combine(outputDirectory, "boardingpasses-club-world.png"));

                var economyPassenger = viewModel.Operations.PassengerRecords
                    .First(passenger => passenger.CabinMarketingName == "World Traveller");
                viewModel.Operations.SelectPassengerCommand.Execute(economyPassenger);
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.DataBind);
                Render(window, Path.Combine(outputDirectory, "boardingpasses-world-traveller.png"));
            }
            else if (page == "Passengers")
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
                    !ticketRoutedPassengers.Any(marker => marker.BoardingGroup == viewModel.Passengers.CurrentBoardingGroup) ||
                    ticketRoutedPassengers.Any(marker =>
                        marker.DoorLabel != (marker.CabinClassName == "First" ? "L1" : "L2")))
                {
                    throw new InvalidOperationException(
                        "Two-door Passenger Flow did not follow First-to-L1 and Business/Economy-to-L2 ticket routing.");
                }

                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.DataBind);
                Render(window, Path.Combine(outputDirectory, "passengers-ticket-and-aisle-routing.png"));
                viewModel.Passengers.StartPauseCommand.Execute(null);

                viewModel.Passengers.ResetCommand.Execute(null);
                viewModel.Passengers.BookedPassengerCount = 36;
                viewModel.Passengers.L1DoorOpen = true;
                viewModel.Passengers.L2DoorOpen = true;
                viewModel.Passengers.SelectedSpeedOption = viewModel.Passengers.SpeedOptions.Single(option => option.Multiplier == 4d);
                viewModel.Passengers.StartPauseCommand.Execute(null);
                for (var index = 0; index < 300 &&
                     viewModel.Passengers.BoardingState != FreeFlight.CabinControl.Core.Passengers.BoardingRunState.Complete; index++)
                {
                    viewModel.Passengers.AdvancePreview(TimeSpan.FromSeconds(0.5d));
                }

                if (viewModel.Passengers.BoardingState != FreeFlight.CabinControl.Core.Passengers.BoardingRunState.Complete ||
                    viewModel.Passengers.PassengerManifest.Count != 36)
                {
                    throw new InvalidOperationException("Passenger Flow did not complete boarding with a full 36-person manifest.");
                }

                var manifestOrder = viewModel.Passengers.PassengerManifest
                    .Select(passenger => (passenger.BoardingGroup, passenger.PassengerId))
                    .ToArray();
                if (!manifestOrder.SequenceEqual(manifestOrder
                        .OrderBy(passenger => passenger.BoardingGroup)
                        .ThenBy(passenger => passenger.PassengerId)))
                {
                    throw new InvalidOperationException("Passenger manifest was not sorted by boarding group and passenger number.");
                }

                viewModel.Passengers.OpenManifestCommand.Execute(null);
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.DataBind);
                Render(window, Path.Combine(outputDirectory, "passengers-manifest.png"));

                viewModel.Passengers.SelectPassengerCommand.Execute(viewModel.Passengers.PassengerManifest[0].PassengerId);
                if (!viewModel.Passengers.IsPassengerDetailsOpen ||
                    string.IsNullOrWhiteSpace(viewModel.Passengers.SelectedPassenger?.FullName))
                {
                    throw new InvalidOperationException("Passenger profile did not open from the manifest.");
                }

                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.DataBind);
                Render(window, Path.Combine(outputDirectory, "passengers-profile.png"));
                viewModel.Passengers.ClosePassengerDetailsCommand.Execute(null);
                viewModel.Passengers.CloseManifestCommand.Execute(null);

                viewModel.Passengers.StartPauseCommand.Execute(null);
                viewModel.Passengers.AdvancePreview(TimeSpan.FromSeconds(0.25d));

                if (viewModel.Passengers.BoardingState != FreeFlight.CabinControl.Core.Passengers.BoardingRunState.Deboarding ||
                    !viewModel.Passengers.PassengerMarkers.Any(marker => marker.IsWalking))
                {
                    throw new InvalidOperationException("Passenger Flow did not begin visible deboarding movement.");
                }

                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.DataBind);
                Render(window, Path.Combine(outputDirectory, "passengers-deboarding.png"));
                for (var index = 0; index < 300 &&
                     viewModel.Passengers.BoardingState != FreeFlight.CabinControl.Core.Passengers.BoardingRunState.DeboardingComplete; index++)
                {
                    viewModel.Passengers.AdvancePreview(TimeSpan.FromSeconds(0.5d));
                }

                if (viewModel.Passengers.BoardingState != FreeFlight.CabinControl.Core.Passengers.BoardingRunState.DeboardingComplete ||
                    viewModel.Passengers.DeboardedPassengerCount != 36 ||
                    viewModel.Passengers.PassengerMarkers.Count != 0)
                {
                    throw new InvalidOperationException("Passenger Flow did not finish with an empty cabin.");
                }

                viewModel.Passengers.SimBriefPilotId = "123456";
                viewModel.Passengers.SyncSimBriefAsync().GetAwaiter().GetResult();
                if (viewModel.Passengers.BookedPassengerCount != 302 ||
                    !viewModel.Passengers.SimBriefFlightSummary.Contains("BAW123", StringComparison.Ordinal) ||
                    !viewModel.Passengers.SimBriefFlightSummary.Contains("18:30", StringComparison.Ordinal) ||
                    viewModel.Passengers.MappedPassengerCount != 302 ||
                    viewModel.Passengers.UnmappedPassengerCount != 0 ||
                    viewModel.Passengers.PassengerManifest.Count != 302 ||
                    viewModel.Passengers.PassengerManifest.First().BoardingGroup != 1 ||
                    viewModel.Passengers.PassengerManifest.Last().BoardingGroup != 8 ||
                    viewModel.Passengers.CanAdjustPassengerLoad)
                {
                    throw new InvalidOperationException("SimBrief OFP did not retain priority over the mapped cabin capacity.");
                }

                viewModel.Operations.RefreshOperationalClock();
                var activeTimelineEvent = viewModel.Operations.TimelineEvents.Single(timelineEvent =>
                    timelineEvent.State == FlightTimelineEventState.Current);
                if (viewModel.Operations.DetectedAircraftIcao != "B77W" ||
                    !viewModel.Operations.GateAssignment.IsAutomatic ||
                    (!viewModel.Operations.GateNumber.StartsWith('B') &&
                     !viewModel.Operations.GateNumber.StartsWith('C')) ||
                    !viewModel.Operations.ArrivalGateAssignment.IsAutomatic ||
                    !int.TryParse(viewModel.Operations.ArrivalGateNumber, out var arrivalGate) ||
                    arrivalGate is < 12 or > 47 ||
                    !viewModel.Operations.GateHeader.Contains("DEP", StringComparison.Ordinal) ||
                    !viewModel.Operations.GateHeader.Contains("ARR", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("The SimBrief route did not receive automatic departure and arrival gates.");
                }

                if (viewModel.Operations.ScheduleSourceLabel != "SIMBRIEF DEPARTURE" ||
                    viewModel.Operations.ScheduledDeparture != "18:30" ||
                    viewModel.Operations.TurnaroundStartsAt != "17:30" ||
                    viewModel.Operations.GateOpensAt != "17:35" ||
                    viewModel.Operations.BoardingBeginsAt != "17:45" ||
                    viewModel.Operations.GateClosesAt != "18:28" ||
                    activeTimelineEvent.Label != "Boarding")
                {
                    throw new InvalidOperationException("The SimBrief departure did not drive the live turnaround timeline.");
                }

                viewModel.NavigateCommand.Execute("Dashboard");
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.DataBind);
                Render(window, Path.Combine(outputDirectory, "dashboard-simbrief-timeline.png"));
                viewModel.NavigateCommand.Execute("Passengers");

                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.DataBind);
                Render(window, Path.Combine(outputDirectory, "passengers-simbrief-synced.png"));

                viewModel.Passengers.StartPauseCommand.Execute(null);
                for (var index = 0; index < 500 &&
                     viewModel.Passengers.BoardingState != FreeFlight.CabinControl.Core.Passengers.BoardingRunState.Complete; index++)
                {
                    viewModel.Passengers.AdvancePreview(TimeSpan.FromSeconds(0.5d));
                }

                if (viewModel.Passengers.BoardingState != FreeFlight.CabinControl.Core.Passengers.BoardingRunState.Complete ||
                    viewModel.Passengers.BoardedPassengerCount != 302 ||
                    viewModel.Passengers.PassengerMarkers.Count != 302 ||
                    viewModel.Passengers.RemainingPassengerCount != 0)
                {
                    throw new InvalidOperationException("The 302-passenger SimBrief load did not fill 302 individual mapped seats.");
                }

                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.DataBind);
                Render(window, Path.Combine(outputDirectory, "passengers-simbrief-302-boarded.png"));

                foreach (var layoutCheck in new[]
                         {
                             (Id: "british-airways.777-200er", Capacity: 280),
                             (Id: "british-airways.777-300", Capacity: 266)
                         })
                {
                    var profile = viewModel.Passengers.CabinLayoutProfiles.Single(profile => profile.Id == layoutCheck.Id);
                    viewModel.Passengers.SelectedCabinLayoutProfile = profile;
                    if (!viewModel.Passengers.IsOperationalCabinLayout ||
                        viewModel.Passengers.IsReferenceCabinLayout ||
                        !viewModel.Passengers.IsAirlineCabinLayout ||
                        !profile.LivePreviewUri.Contains("Horizontal", StringComparison.Ordinal) ||
                        viewModel.Passengers.CabinCapacity != layoutCheck.Capacity ||
                        viewModel.Passengers.MappedPassengerCount != layoutCheck.Capacity)
                    {
                        throw new InvalidOperationException($"{profile.Name} did not activate its operational seat-coordinate profile.");
                    }

                    viewModel.Passengers.L1DoorOpen = true;
                    viewModel.Passengers.L2DoorOpen = true;
                    viewModel.Passengers.StartPauseCommand.Execute(null);
                    for (var index = 0; index < 8; index++)
                    {
                        viewModel.Passengers.AdvancePreview(TimeSpan.FromSeconds(0.5d));
                    }

                    var marker = viewModel.Passengers.PassengerMarkers.FirstOrDefault();
                    if (marker is null || viewModel.Passengers.BoardingState !=
                            FreeFlight.CabinControl.Core.Passengers.BoardingRunState.Boarding)
                    {
                        throw new InvalidOperationException($"{profile.Name} did not start visible passenger boarding.");
                    }

                    viewModel.Passengers.SelectPassengerCommand.Execute(marker);
                    var selectedPassenger = viewModel.Passengers.SelectedPassenger;
                    if (!viewModel.Passengers.IsSeatHighlightVisible ||
                        viewModel.Passengers.IsPassengerDetailsOpen ||
                        selectedPassenger is null ||
                        selectedPassenger.SeatNumber != marker.SeatNumber ||
                        Math.Abs(selectedPassenger.SeatX - marker.SeatX) > 0.001d ||
                        Math.Abs(selectedPassenger.SeatY - marker.SeatY) > 0.001d)
                    {
                        throw new InvalidOperationException($"{profile.Name} did not highlight the selected passenger's ticketed seat.");
                    }

                    window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.DataBind);
                    var slug = profile.Id.Replace('.', '-');
                    Render(window, Path.Combine(outputDirectory, $"passengers-live-layout-{slug}.png"));
                }

                viewModel.Passengers.SelectedCabinLayoutProfile =
                    viewModel.Passengers.CabinLayoutProfiles.Single(profile => profile.Id == "flightfactor.777v2");
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
            else if (page == "Settings")
            {
                viewModel.Settings.SelectSectionCommand.Execute("Aircraft");
                foreach (var profile in viewModel.Settings.CabinLayoutProfiles)
                {
                    viewModel.Settings.SelectedCabinLayoutProfile = profile;
                    window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.DataBind);
                    var slug = profile.Id.Replace('.', '-');
                    Render(window, Path.Combine(outputDirectory, $"settings-aircraft-{slug}.png"));
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
        var renderedChecks = Directory.EnumerateFiles(outputDirectory, "*.png", SearchOption.TopDirectoryOnly).Count();
        Console.WriteLine($"Rendered {renderedChecks} visual checks to {outputDirectory}");
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

    private sealed class FakeSimBriefClient : ISimBriefClient
    {
        public Task<SimBriefFlightSummary> FetchLatestOfpAsync(
            string pilotId,
            CancellationToken cancellationToken = default)
        {
            if (pilotId != "123456")
            {
                throw new InvalidOperationException("The visual-check SimBrief Pilot ID was unexpected.");
            }

            return Task.FromResult(new SimBriefFlightSummary(
                302,
                "BAW123",
                "EGLL",
                "KJFK",
                "B77W",
                new DateTimeOffset(new DateTime(2026, 8, 25, 18, 30, 0, DateTimeKind.Local)).ToUniversalTime(),
                DateTimeOffset.UtcNow));
        }
    }

    private sealed class FixedOperationsClock(DateTimeOffset now) : IOperationsClock
    {
        public DateTimeOffset Now { get; } = now;

        public string SourceLabel => "LOCAL TIME";
    }

    private sealed class FakeBoardingPassPrinterService : IBoardingPassPrinterService
    {
        public int PrintCount { get; private set; }

        public IReadOnlyList<PrinterDestination> GetPrinters() =>
        [
            new PrinterDestination("visual-default", "Visual Check Printer", true, false),
            new PrinterDestination("visual-pdf", "Visual PDF Printer", false, false)
        ];

        public BoardingPassPrintResult PrintBoardingPass(
            PrinterDestination destination,
            object boardingPassDataContext,
            string jobName)
        {
            PrintCount++;
            return new BoardingPassPrintResult(true, $"Sent to {destination.DisplayName}.");
        }
    }
}
