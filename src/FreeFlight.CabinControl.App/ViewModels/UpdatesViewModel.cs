using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using FreeFlight.CabinControl.App.Infrastructure;
using FreeFlight.CabinControl.App.Services;
using FreeFlight.CabinControl.Core.Configuration;
using FreeFlight.CabinControl.Core.Persistence;

namespace FreeFlight.CabinControl.App.ViewModels;

public sealed class UpdatesViewModel : PageViewModel
{
    private readonly AppSettings _settings;
    private readonly ISettingsStore _settingsStore;
    private readonly UpdateService _service;
    private readonly Action? _beforeInstall;
    private readonly Action? _installAborted;
    private ApplicationUpdate? _availableUpdate;
    private string _changelog;
    private string _status = "Ready to check for updates.";
    private string _flightAdvisory = "No active flight was detected. You can install now or choose Later.";
    private string _installButtonLabel = "Install & Restart";
    private IReadOnlyList<UpdateReleaseSection> _releaseSections = Array.Empty<UpdateReleaseSection>();
    private bool _isPreview;
    private bool _isFlightInProgress;
    private bool _isBusy;

    public UpdatesViewModel(
        AppSettings settings,
        ISettingsStore settingsStore,
        UpdateService service,
        Action? beforeInstall = null,
        Action? installAborted = null)
        : base("Updates & Changelog", "Keep Ember current without losing your local profile")
    {
        _settings = settings;
        _settingsStore = settingsStore;
        _service = service;
        _beforeInstall = beforeInstall;
        _installAborted = installAborted;
        _changelog = service.ReadBundledChangelog();
        CheckCommand = new AsyncRelayCommand(CheckAsync, HandleError);
        InstallCommand = new AsyncRelayCommand(InstallAsync, HandleError);
        OpenReleaseCommand = new RelayCommand(_ => OpenRelease());
    }

    public ICommand CheckCommand { get; }
    public ICommand InstallCommand { get; }
    public ICommand OpenReleaseCommand { get; }
    public string CurrentVersion => $"v{_service.CurrentVersion.ToString(3)}";
    public string Changelog { get => _changelog; private set => SetProperty(ref _changelog, value); }
    public string AvailableVersion => _availableUpdate is null ? "No GitHub release published" : $"{_availableUpdate.Tag} available";
    public string AvailableVersionNumber => _availableUpdate?.Tag ?? "—";
    public string ReleaseNotes => _availableUpdate?.ReleaseNotes ?? "Check for updates to load the latest release notes.";
    public string ReleaseBriefing => HasUpdate
        ? "A verified Ember release is ready with the improvements and fixes below."
        : "Check the stable Ember release channel for the latest improvements.";
    public IReadOnlyList<UpdateReleaseSection> ReleaseSections
    {
        get => _releaseSections;
        private set
        {
            _releaseSections = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasReleaseSections));
        }
    }
    public bool HasReleaseSections => ReleaseSections.Count > 0;
    public string InstallButtonLabel { get => _installButtonLabel; private set => SetProperty(ref _installButtonLabel, value); }
    public string FlightAdvisory { get => _flightAdvisory; private set => SetProperty(ref _flightAdvisory, value); }
    public bool IsFlightInProgress { get => _isFlightInProgress; private set => SetProperty(ref _isFlightInProgress, value); }
    public bool HasUpdate => _availableUpdate is not null && _availableUpdate.Version > _service.CurrentVersion;
    public string? AvailableUpdateTag => HasUpdate ? _availableUpdate?.Tag : null;
    // Never close Ember underneath a pilot who is in the middle of an ACARS
    // flight. The update is kept ready and can be applied after that flight
    // has been completed or safely ended.
    public bool CanInstall => !_isPreview &&
                              !IsFlightInProgress &&
                              HasUpdate &&
                              _availableUpdate?.AssetDownload is not null &&
                              !IsBusy;
    public bool IsBusy { get => _isBusy; private set { if (SetProperty(ref _isBusy, value)) OnPropertyChanged(nameof(CanInstall)); } }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }

    public bool AutomaticallyCheckForUpdates
    {
        get => _settings.AutomaticallyCheckForUpdates;
        set
        {
            if (_settings.AutomaticallyCheckForUpdates == value) return;
            _settings.AutomaticallyCheckForUpdates = value;
            OnPropertyChanged();
            _ = _settingsStore.SaveAsync(_settings);
        }
    }

    public async Task<bool> CheckForStartupUpdateAsync(bool flightInProgress)
    {
        if (!_settings.AutomaticallyCheckForUpdates)
        {
            return false;
        }

        try
        {
            await CheckAsync();
            if (!HasUpdate)
            {
                return false;
            }

            PrepareNotification(flightInProgress);
            return true;
        }
        catch (Exception exception)
        {
            HandleError(exception);
            return false;
        }
    }

    public void PrepareNotification(bool flightInProgress)
    {
        IsFlightInProgress = flightInProgress;
        FlightAdvisory = flightInProgress
            ? "An active flight is in progress. The hotfix is ready, but Ember will not close or update until your flight has safely finished."
            : "No active flight was detected. Ember can download, apply and restart with this update now — no installer is needed.";
        InstallButtonLabel = flightInProgress
            ? "Finish flight first"
            : "Install & restart Ember";
        OnPropertyChanged(nameof(CanInstall));
    }

    public void PreparePreviewNotification(bool flightInProgress)
    {
        var current = _service.CurrentVersion;
        var previewVersion = new Version(current.Major, current.Minor + 1, 0);
        _isPreview = true;
        _availableUpdate = new ApplicationUpdate(
            previewVersion,
            $"v{previewVersion.ToString(3)} preview",
            "This is a safe notification preview. It demonstrates the version summary, release notes, active-flight warning, changelog access, and Later choice without downloading or installing files.",
            new Uri("https://github.com/TofferAviation/FreeFlight-Cabin-Controls/releases"),
            null,
            null);
        ReleaseSections = BuildReleaseSections(_availableUpdate);
        Status = "Preview mode — no update will be downloaded or installed.";
        PrepareNotification(flightInProgress);
        InstallButtonLabel = "Preview only";
        OnPropertyChanged(nameof(AvailableVersion));
        OnPropertyChanged(nameof(ReleaseNotes));
        OnPropertyChanged(nameof(AvailableVersionNumber));
        OnPropertyChanged(nameof(ReleaseBriefing));
        OnPropertyChanged(nameof(HasUpdate));
        OnPropertyChanged(nameof(CanInstall));
    }

    public async Task CheckAsync()
    {
        IsBusy = true;
        Status = "Checking the stable release channel…";
        try
        {
            _isPreview = false;
            InstallButtonLabel = "Install & restart Ember";
            var result = await _service.CheckAsync();
            _availableUpdate = result.LatestRelease;
            Changelog = BuildChangelog(_availableUpdate);
            ReleaseSections = BuildReleaseSections(_availableUpdate);
            Status = HasUpdate
                ? $"Ember {_availableUpdate!.Tag} is ready to install in the app — no installer is needed."
                : _availableUpdate is null
                    ? result.FeedStatus
                    : $"You are running the latest published version. {result.FeedStatus}";
            OnPropertyChanged(nameof(AvailableVersion));
            OnPropertyChanged(nameof(ReleaseNotes));
            OnPropertyChanged(nameof(AvailableVersionNumber));
            OnPropertyChanged(nameof(ReleaseBriefing));
            OnPropertyChanged(nameof(HasUpdate));
            OnPropertyChanged(nameof(AvailableUpdateTag));
            OnPropertyChanged(nameof(CanInstall));
        }
        catch (Exception exception)
        {
            HandleError(exception);
        }
        finally { IsBusy = false; }
    }

    private async Task InstallAsync()
    {
        if (_availableUpdate is null || !CanInstall) return;
        IsBusy = true;
        Status = "Downloading and verifying the Ember hotfix…";
        try
        {
            _beforeInstall?.Invoke();
            await _settingsStore.SaveAsync(_settings);
            await _service.StageAndInstallAsync(_availableUpdate);
            Status = "Update verified. Ember will now restart to finish the update.";
            Application.Current.Shutdown();
        }
        catch
        {
            _installAborted?.Invoke();
            throw;
        }
        finally { IsBusy = false; }
    }

    private void OpenRelease()
    {
        var releasePage = _availableUpdate?.ReleasePage.AbsoluteUri ?? UpdateService.ReleasesPage;
        Process.Start(new ProcessStartInfo(releasePage) { UseShellExecute = true });
    }

    private string BuildChangelog(ApplicationUpdate? release)
    {
        var bundled = _service.ReadBundledChangelog();
        if (release is null)
        {
            return bundled;
        }

        return $"# GitHub Release {release.Tag}\n\n{release.ReleaseNotes}\n\n---\n\n# Installed/Bundled Changelog\n\n{bundled}";
    }

    private static IReadOnlyList<UpdateReleaseSection> BuildReleaseSections(ApplicationUpdate? release)
    {
        if (release is null)
        {
            return Array.Empty<UpdateReleaseSection>();
        }

        var notes = ExtractCurrentReleaseNotes(release);
        var sections = new Dictionary<string, List<UpdateReleaseItem>>(StringComparer.OrdinalIgnoreCase);
        var currentHeading = "Release highlights";

        foreach (var rawLine in notes.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.StartsWith("###", StringComparison.Ordinal))
            {
                currentHeading = CleanMarkdown(line.TrimStart('#', ' '));
                continue;
            }

            var bullet = Regex.Match(line, @"^(?:[-*•]|\d+\.)\s+(?<text>.+)$");
            if (!bullet.Success)
            {
                continue;
            }

            var item = CreateReleaseItem(CleanMarkdown(bullet.Groups["text"].Value));
            if (string.IsNullOrWhiteSpace(item.Title))
            {
                continue;
            }

            var category = CategoriseHeading(currentHeading, item.Title);
            if (!sections.TryGetValue(category, out var items))
            {
                items = [];
                sections.Add(category, items);
            }

            if (items.Count < 5)
            {
                items.Add(item);
            }
        }

        if (sections.Count == 0)
        {
            var fallback = CleanMarkdown(notes);
            if (!string.IsNullOrWhiteSpace(fallback))
            {
                sections["Release highlights"] = [CreateReleaseItem(fallback)];
            }
        }

        return sections
            .Take(4)
            .Select(pair => UpdateReleaseSection.Create(pair.Key, pair.Value))
            .ToArray();
    }

    private static string ExtractCurrentReleaseNotes(ApplicationUpdate release)
    {
        var version = Regex.Escape(release.Version.ToString(3));
        var versionBlock = Regex.Match(
            release.ReleaseNotes,
            $@"(?ms)^##\s*\[{version}\][^\r\n]*\r?\n(?<body>.*?)(?=^##\s*\[|\z)");
        return versionBlock.Success ? versionBlock.Groups["body"].Value : release.ReleaseNotes;
    }

    private static string CategoriseHeading(string heading, string itemTitle)
    {
        var value = $"{heading} {itemTitle}";
        if (value.Contains("fix", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("resolv", StringComparison.OrdinalIgnoreCase))
        {
            return "Fixes";
        }

        if (value.Contains("privacy", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("safety", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("security", StringComparison.OrdinalIgnoreCase))
        {
            return "Reliability & privacy";
        }

        if (value.Contains("change", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("improv", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("performance", StringComparison.OrdinalIgnoreCase))
        {
            return "Improvements";
        }

        return "New in this release";
    }

    private static UpdateReleaseItem CreateReleaseItem(string value)
    {
        var text = Regex.Replace(value, @"\s+", " ").Trim();
        var splitAt = text.IndexOf(". ", StringComparison.Ordinal);
        if (splitAt is > 20 and < 100)
        {
            return new UpdateReleaseItem(text[..(splitAt + 1)], text[(splitAt + 2)..]);
        }

        return new UpdateReleaseItem(text, string.Empty);
    }

    private static string CleanMarkdown(string value)
    {
        var clean = Regex.Replace(value, @"\[(?<label>[^\]]+)\]\([^)]+\)", "${label}");
        clean = clean.Replace("**", string.Empty, StringComparison.Ordinal)
            .Replace("__", string.Empty, StringComparison.Ordinal)
            .Replace("`", string.Empty, StringComparison.Ordinal)
            .Replace("#", string.Empty, StringComparison.Ordinal);
        return Regex.Replace(clean, @"\s+", " ").Trim();
    }

    private void HandleError(Exception exception)
    {
        IsBusy = false;
        Status = $"Update check failed: {exception.Message}";
    }
}

public sealed record UpdateReleaseItem(string Title, string Detail);

public sealed class UpdateReleaseSection
{
    private UpdateReleaseSection(string title, string icon, Brush accentBrush, Brush iconBackground, IReadOnlyList<UpdateReleaseItem> items)
    {
        Title = title;
        Icon = icon;
        AccentBrush = accentBrush;
        IconBackground = iconBackground;
        Items = items;
    }

    public string Title { get; }
    public string Icon { get; }
    public Brush AccentBrush { get; }
    public Brush IconBackground { get; }
    public IReadOnlyList<UpdateReleaseItem> Items { get; }

    public static UpdateReleaseSection Create(string title, IReadOnlyList<UpdateReleaseItem> items)
    {
        var (icon, accent, background) = title switch
        {
            "Fixes" => ("\uE73E", "#D75B70", "#1FD75B70"),
            "Reliability & privacy" => ("\uE72E", "#5CBB9F", "#1F5CBB9F"),
            "Improvements" => ("\uE9D2", "#6AA9E8", "#1F6AA9E8"),
            _ => ("\uE710", "#36A8F3", "#1F36A8F3")
        };

        return new UpdateReleaseSection(title, icon, CreateBrush(accent), CreateBrush(background), items);
    }

    private static Brush CreateBrush(string value)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(value)!;
        brush.Freeze();
        return brush;
    }
}
