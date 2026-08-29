using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
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
    private ApplicationUpdate? _availableUpdate;
    private string _changelog;
    private string _status = "Ready to check for updates.";
    private string _flightAdvisory = "No active flight was detected. You can install now or choose Later.";
    private string _installButtonLabel = "Install & Restart";
    private bool _isPreview;
    private bool _isFlightInProgress;
    private bool _isBusy;

    public UpdatesViewModel(
        AppSettings settings,
        ISettingsStore settingsStore,
        UpdateService service,
        Action? beforeInstall = null)
        : base("Updates & Changelog", "Keep FreeFlight Cabin Control current without losing your local profile")
    {
        _settings = settings;
        _settingsStore = settingsStore;
        _service = service;
        _beforeInstall = beforeInstall;
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
    public string ReleaseNotes => _availableUpdate?.ReleaseNotes ?? "Check for updates to load the latest release notes.";
    public string InstallButtonLabel { get => _installButtonLabel; private set => SetProperty(ref _installButtonLabel, value); }
    public string FlightAdvisory { get => _flightAdvisory; private set => SetProperty(ref _flightAdvisory, value); }
    public bool IsFlightInProgress { get => _isFlightInProgress; private set => SetProperty(ref _isFlightInProgress, value); }
    public bool HasUpdate => _availableUpdate is not null && _availableUpdate.Version > _service.CurrentVersion;
    public string? AvailableUpdateTag => HasUpdate ? _availableUpdate?.Tag : null;
    public bool CanInstall => !_isPreview && HasUpdate && _availableUpdate?.AssetDownload is not null && !IsBusy;
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
            ? "An active flight is in progress. Choose Later to keep flying; the update will not install automatically."
            : "No active flight was detected. You can install now or choose Later.";
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
        Status = "Preview mode — no update will be downloaded or installed.";
        InstallButtonLabel = "Preview Only";
        PrepareNotification(flightInProgress);
        OnPropertyChanged(nameof(AvailableVersion));
        OnPropertyChanged(nameof(ReleaseNotes));
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
            InstallButtonLabel = "Install & Restart";
            var result = await _service.CheckAsync();
            _availableUpdate = result.LatestRelease;
            Changelog = BuildChangelog(_availableUpdate);
            Status = HasUpdate
                ? $"FreeFlight {_availableUpdate!.Tag} is ready from GitHub."
                : _availableUpdate is null
                    ? result.FeedStatus
                    : $"You are running the latest published version. {result.FeedStatus}";
            OnPropertyChanged(nameof(AvailableVersion));
            OnPropertyChanged(nameof(ReleaseNotes));
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
        Status = "Downloading and staging the Windows update package…";
        try
        {
            _beforeInstall?.Invoke();
            await _settingsStore.SaveAsync(_settings);
            await _service.StageAndInstallAsync(_availableUpdate);
            Status = "Update staged. Cabin Control will restart to finish installation.";
            Application.Current.Shutdown();
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

    private void HandleError(Exception exception)
    {
        IsBusy = false;
        Status = $"Update check failed: {exception.Message}";
    }
}
