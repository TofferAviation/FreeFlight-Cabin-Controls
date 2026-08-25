using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FreeFlight.CabinControl.App.Views;

namespace FreeFlight.CabinControl.App.Services;

public interface IBoardingPassPrinterService
{
    IReadOnlyList<PrinterDestination> GetPrinters();

    BoardingPassPrintResult PrintBoardingPass(
        PrinterDestination destination,
        object boardingPassDataContext,
        string jobName);
}

public sealed record PrinterDestination(
    string QueueId,
    string DisplayName,
    bool IsDefault,
    bool IsOffline)
{
    public string DisplayLabel => IsDefault ? $"{DisplayName} (Windows default)" : DisplayName;

    public string StatusLabel => IsOffline ? "Offline" : "Ready";

    public override string ToString() => DisplayLabel;
}

public sealed record BoardingPassPrintResult(bool IsSuccess, string Message);

public sealed class WindowsBoardingPassPrinterService : IBoardingPassPrinterService
{
    private static readonly EnumeratedPrintQueueTypes[] QueueTypes =
    [
        EnumeratedPrintQueueTypes.Local,
        EnumeratedPrintQueueTypes.Connections
    ];

    public IReadOnlyList<PrinterDestination> GetPrinters()
    {
        try
        {
            using var server = new LocalPrintServer();
            var defaultQueueId = GetDefaultQueueId(server);
            var destinations = new List<PrinterDestination>();
            foreach (var queue in server.GetPrintQueues(QueueTypes))
            {
                using (queue)
                {
                    try
                    {
                        var queueId = queue.FullName;
                        destinations.Add(new PrinterDestination(
                            queueId,
                            queue.Name,
                            string.Equals(queueId, defaultQueueId, StringComparison.OrdinalIgnoreCase),
                            (queue.QueueStatus & PrintQueueStatus.Offline) != 0));
                    }
                    catch (PrintSystemException)
                    {
                        // A single stale network queue must not prevent other printers from appearing.
                    }
                }
            }

            return destinations
                .OrderByDescending(destination => destination.IsDefault)
                .ThenBy(destination => destination.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
        }
        catch (Exception exception) when (exception is PrintSystemException or InvalidOperationException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    public BoardingPassPrintResult PrintBoardingPass(
        PrinterDestination destination,
        object boardingPassDataContext,
        string jobName)
    {
        try
        {
            using var server = new LocalPrintServer();
            using var queue = FindQueue(server, destination.QueueId);
            if (queue is null)
            {
                return new BoardingPassPrintResult(false, $"Windows printer '{destination.DisplayName}' is no longer available.");
            }

            queue.Refresh();
            if ((queue.QueueStatus & PrintQueueStatus.Offline) != 0)
            {
                return new BoardingPassPrintResult(false, $"Windows printer '{destination.DisplayName}' is offline.");
            }

            var printDialog = new PrintDialog
            {
                PrintQueue = queue,
                PrintTicket = new PrintTicket
                {
                    PageOrientation = PageOrientation.Landscape
                }
            };
            var capabilities = queue.GetPrintCapabilities(printDialog.PrintTicket);
            var imageableArea = capabilities.PageImageableArea;
            var width = Math.Max(760d, imageableArea?.ExtentWidth ?? 1056d);
            var height = Math.Max(320d, imageableArea?.ExtentHeight ?? 768d);

            var pass = new BoardingPassCardView
            {
                DataContext = boardingPassDataContext,
                Width = 1120,
                Height = 430
            };
            var printSurface = new Grid
            {
                Width = width,
                Height = height,
                Background = Brushes.White
            };
            printSurface.Children.Add(new Viewbox
            {
                Margin = new Thickness(22),
                Stretch = Stretch.Uniform,
                StretchDirection = StretchDirection.DownOnly,
                Child = pass
            });
            printSurface.Measure(new Size(width, height));
            printSurface.Arrange(new Rect(0, 0, width, height));
            printSurface.UpdateLayout();

            printDialog.PrintVisual(printSurface, jobName);
            return new BoardingPassPrintResult(true, $"Sent to {destination.DisplayName}.");
        }
        catch (Exception exception) when (exception is PrintSystemException or InvalidOperationException or UnauthorizedAccessException)
        {
            return new BoardingPassPrintResult(false, $"Windows could not print the boarding pass: {exception.Message}");
        }
    }

    private static string GetDefaultQueueId(LocalPrintServer server)
    {
        try
        {
            using var defaultQueue = server.DefaultPrintQueue;
            return defaultQueue?.FullName ?? string.Empty;
        }
        catch (PrintSystemException)
        {
            return string.Empty;
        }
    }

    private static PrintQueue? FindQueue(LocalPrintServer server, string queueId)
    {
        PrintQueue? selectedQueue = null;
        foreach (var queue in server.GetPrintQueues(QueueTypes))
        {
            if (selectedQueue is null &&
                (string.Equals(queue.FullName, queueId, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(queue.Name, queueId, StringComparison.OrdinalIgnoreCase)))
            {
                selectedQueue = queue;
            }
            else
            {
                queue.Dispose();
            }
        }

        return selectedQueue;
    }
}
