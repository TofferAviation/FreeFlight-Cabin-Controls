using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using FreeFlight.CabinControl.App.Infrastructure;
using FreeFlight.CabinControl.App.Services;
using FreeFlight.CabinControl.App.Views;
using FreeFlight.CabinControl.Core.Configuration;
using FreeFlight.CabinControl.Core.Persistence;
using Microsoft.Win32;

namespace FreeFlight.CabinControl.App.ViewModels;

public sealed class AirlinersViewModel : PageViewModel, IDisposable
{
    private const string AllFilter = "All";
    private readonly AppSettings _settings;
    private readonly ISettingsStore _settingsStore;
    private readonly IVamsysOAuthService _vamsysService;
    private readonly string _profileAssetsDirectory;
    private readonly DispatcherTimer _vamsysPollTimer;
    private readonly List<AirlineProfileViewModel> _catalog;
    private AirlineProfileViewModel _activeAirline;
    private string _searchText = string.Empty;
    private string _activeFilter = "Real-world";
    private string _selectedCatalogSource = "Real-world operators";
    private string _selectionStatus = "Airline selection is stored locally";
    private string _vamsysConnectionStatus = "Developer registration required";
    private VamsysPilotProfile? _vamsysProfile;
    private DateTimeOffset _vamsysPollDeadline;
    private bool _isVamsysBusy;

    public AirlinersViewModel(
        AppSettings settings,
        ISettingsStore settingsStore,
        SharedStatusViewModel status,
        IVamsysOAuthService vamsysService,
        string settingsDirectory)
        : base("Airliners", "Choose your airline experience")
    {
        _settings = settings;
        _settingsStore = settingsStore;
        _vamsysService = vamsysService;
        _profileAssetsDirectory = Path.Combine(settingsDirectory, "profile-assets");
        Status = status;
        _catalog = CreateCatalog(settings.CustomAirlineProfiles);
        _activeAirline = _catalog.FirstOrDefault(profile =>
            string.Equals(profile.Id, settings.ActiveAirlineId, StringComparison.OrdinalIgnoreCase))
            ?? _catalog.FirstOrDefault(profile => profile.Icao == "BAW")
            ?? _catalog[0];
        _activeAirline.IsActive = true;

        SelectAirlineCommand = new RelayCommand(SelectAirline);
        FilterCommand = new RelayCommand(SetFilter);
        ChangeAirlineCommand = new RelayCommand(_ => ResetCatalog());
        ConnectVamsysCommand = new AsyncRelayCommand(ConnectVamsysAsync, ShowVamsysError);
        DisconnectVamsysCommand = new AsyncRelayCommand(DisconnectVamsysAsync, ShowVamsysError);
        OpenAccountProfileCommand = new RelayCommand(_ => OpenAccountProfile());
        OpenVamsysAccountCommand = new RelayCommand(_ => OpenVamsysAccount());
        ChooseProfileImageCommand = new AsyncRelayCommand(ChooseProfileImageAsync, ShowVamsysError);
        ChooseBackgroundImageCommand = new AsyncRelayCommand(ChooseBackgroundImageAsync, ShowVamsysError);
        RemoveProfileImageCommand = new AsyncRelayCommand(RemoveProfileImageAsync, ShowVamsysError);
        RemoveBackgroundImageCommand = new AsyncRelayCommand(RemoveBackgroundImageAsync, ShowVamsysError);
        CreateProfileCommand = new RelayCommand(_ => CreateCustomProfile());
        _vamsysPollTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1.5)
        };
        _vamsysPollTimer.Tick += HandleVamsysPollTick;
        ApplyFilter();
        _ = RefreshVamsysProfileAsync();
    }

    public SharedStatusViewModel Status { get; }

    public ObservableCollection<AirlineProfileViewModel> VisibleAirlines { get; } = [];

    public ICommand SelectAirlineCommand { get; }

    public ICommand FilterCommand { get; }

    public ICommand ChangeAirlineCommand { get; }

    public ICommand ConnectVamsysCommand { get; }

    public ICommand DisconnectVamsysCommand { get; }

    public ICommand OpenAccountProfileCommand { get; }

    public ICommand OpenVamsysAccountCommand { get; }

    public ICommand ChooseProfileImageCommand { get; }

    public ICommand ChooseBackgroundImageCommand { get; }

    public ICommand RemoveProfileImageCommand { get; }

    public ICommand RemoveBackgroundImageCommand { get; }

    public ICommand CreateProfileCommand { get; }

    public AirlineProfileViewModel ActiveAirline
    {
        get => _activeAirline;
        private set => SetProperty(ref _activeAirline, value);
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                ApplyFilter();
            }
        }
    }

    public string ActiveFilter
    {
        get => _activeFilter;
        private set => SetProperty(ref _activeFilter, value);
    }

    public string SelectionStatus
    {
        get => _selectionStatus;
        private set => SetProperty(ref _selectionStatus, value);
    }

    public int AvailableProfileCount => _catalog.Count;

    public int RealWorldOperatorCount => _catalog.Count(profile => profile.Type == "Real-world");

    public int InstalledPackCount => _catalog.Count(profile => profile.IsInstalled);

    public IReadOnlyList<string> CatalogSources { get; } =
        ["Real-world operators", "vAMSYS virtual airline"];

    public string SelectedCatalogSource
    {
        get => _selectedCatalogSource;
        set
        {
            if (SetProperty(ref _selectedCatalogSource, value))
            {
                ActiveFilter = value == "Real-world operators" ? "Real-world" : "vAMSYS";
                ApplyFilter();
            }
        }
    }

    public bool IsVamsysConnected => _vamsysProfile is not null;

    public bool IsVamsysBusy
    {
        get => _isVamsysBusy;
        private set
        {
            if (SetProperty(ref _isVamsysBusy, value))
            {
                OnPropertyChanged(nameof(VamsysActionLabel));
            }
        }
    }

    public string VamsysConnectionStatus
    {
        get => _vamsysConnectionStatus;
        private set => SetProperty(ref _vamsysConnectionStatus, value);
    }

    public string VamsysActionLabel => IsVamsysBusy
        ? "Waiting for vAMSYS…"
        : IsVamsysConnected
            ? "Reconnect vAMSYS"
            : _vamsysService.IsConfigured
                ? "Connect vAMSYS"
                : "Configure vAMSYS";

    public string VamsysDisplayName => _vamsysProfile?.DisplayName ?? "vAMSYS pilot";

    public string VamsysPilotLabel => _vamsysProfile is null
        ? string.Empty
        : $"{_vamsysProfile.PilotUsername} · {_vamsysProfile.AirlineIcao}";

    public string VamsysEmail => _vamsysProfile?.Email ?? string.Empty;

    public string VamsysRank => string.IsNullOrWhiteSpace(_vamsysProfile?.RankName)
        ? "Pilot"
        : _vamsysProfile.RankName;

    public string VamsysAvatarInitials
    {
        get
        {
            var parts = VamsysDisplayName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length switch
            {
                0 => "VA",
                1 => parts[0][..1].ToUpperInvariant(),
                _ => $"{parts[0][0]}{parts[^1][0]}".ToUpperInvariant()
            };
        }
    }

    public Uri? AccountProfileImageUri => ToExistingFileUri(_settings.AccountProfileImagePath);

    public bool HasAccountProfileImage => AccountProfileImageUri is not null;

    public Uri? AccountBackgroundImageUri => ToExistingFileUri(_settings.AccountBackgroundImagePath);

    public Uri? ActiveBackgroundImageUri => ApplyAccountBackgroundAcrossPages
        ? AccountBackgroundImageUri
        : null;

    public bool HasActiveBackgroundImage => ActiveBackgroundImageUri is not null;

    public double AccountBackgroundOpacity => Math.Clamp(_settings.AccountBackgroundOpacityPercent, 10, 20) / 100d;

    public int AccountBackgroundOpacityPercent
    {
        get => Math.Clamp(_settings.AccountBackgroundOpacityPercent, 10, 20);
        set
        {
            var clamped = Math.Clamp(value, 10, 20);
            if (_settings.AccountBackgroundOpacityPercent == clamped)
            {
                return;
            }

            _settings.AccountBackgroundOpacityPercent = clamped;
            OnPropertyChanged();
            OnPropertyChanged(nameof(AccountBackgroundOpacity));
            _ = SaveAppearanceAsync();
        }
    }

    public double AccountBackgroundBlurRadius
    {
        get => Math.Clamp(_settings.AccountBackgroundBlurRadius, 10d, 20d);
        set
        {
            var clamped = Math.Clamp(value, 10d, 20d);
            if (Math.Abs(_settings.AccountBackgroundBlurRadius - clamped) < 0.01d)
            {
                return;
            }

            _settings.AccountBackgroundBlurRadius = clamped;
            OnPropertyChanged();
            _ = SaveAppearanceAsync();
        }
    }

    public bool ApplyAccountBackgroundAcrossPages
    {
        get => _settings.ApplyAccountBackgroundAcrossPages;
        set
        {
            if (_settings.ApplyAccountBackgroundAcrossPages == value)
            {
                return;
            }

            _settings.ApplyAccountBackgroundAcrossPages = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ActiveBackgroundImageUri));
            OnPropertyChanged(nameof(HasActiveBackgroundImage));
            _ = SaveAppearanceAsync();
        }
    }

    public string AirlineSourceStatus => IsVamsysConnected
        ? $"Showing {_vamsysProfile!.AirlineName}, authorized through vAMSYS"
        : "No vAMSYS airline connected · the virtual-airline catalog is empty";

    public bool HasVisibleAirlines => VisibleAirlines.Count > 0;

    public string EmptyCatalogMessage => ActiveFilter == "vAMSYS"
        ? "No virtual airline is available. Connect vAMSYS to display the airline authorized for your account."
        : "No scheduled real-world operator matches the current search.";

    private static List<AirlineProfileViewModel> CreateCatalog(
        IEnumerable<CustomAirlineProfileSettings> customProfiles)
    {
        var catalog = RealWorldOperatorCatalog.All
            .Select(entry => new AirlineProfileViewModel(
                entry.Id,
                entry.Name,
                entry.Icao,
                "Real-world",
                "Base branding package",
                false))
            .ToList();

        catalog.AddRange([
        new("freeflight.virtual", "FreeFlight Virtual", "FFV", "Virtual Airline", "FreeFlight Cabin Pack", true),
        new("nordic-air", "Nordic Air", "NDA", "Virtual Airline", "Nordic Air Cabin Pack", false),
        new("british-atlantic", "British Atlantic", "BAT", "Virtual Airline", "British Atlantic Pack", false),
        new("emirates-sky", "Emirates Sky", "EMS", "Virtual Airline", "Emirates Sky Pack", false),
        new("pacific-crown", "Pacific Crown", "PCR", "Virtual Airline", "Pacific Crown Pack", false),
        new("global-charter", "Global Charter", "GCR", "Virtual Airline", "Global Charter Pack", false)
        ]);

        catalog.AddRange(customProfiles
            .Where(profile => !string.IsNullOrWhiteSpace(profile.Id)
                              && !string.IsNullOrWhiteSpace(profile.Name)
                              && !string.IsNullOrWhiteSpace(profile.Icao))
            .Select(profile => new AirlineProfileViewModel(
                profile.Id,
                profile.Name,
                profile.Icao,
                "Virtual Airline",
                profile.SoundPackName,
                false)));
        return catalog;
    }

    private async void SelectAirline(object? parameter)
    {
        if (parameter is not AirlineProfileViewModel profile)
        {
            return;
        }

        ActiveAirline.IsActive = false;
        profile.IsActive = true;
        ActiveAirline = profile;
        _settings.ActiveAirlineId = profile.Id;
        _settings.ActiveAirlinePackId = profile.IsInstalled
            ? profile.Id
            : AppSettings.DefaultAirlinePackId;

        try
        {
            await _settingsStore.SaveAsync(_settings);
            SelectionStatus = $"{profile.Name} selected at {DateTime.Now:t}";
        }
        catch (Exception exception)
        {
            SelectionStatus = "Airline selection could not be saved";
            MessageBox.Show(exception.Message, "Save failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SetFilter(object? parameter)
    {
        ActiveFilter = parameter as string ?? AllFilter;
        ApplyFilter();
    }

    private void ResetCatalog()
    {
        SearchText = string.Empty;
        ActiveFilter = AllFilter;
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var search = SearchText.Trim();
        var matches = ActiveFilter == "vAMSYS" && !IsVamsysConnected
            ? Enumerable.Empty<AirlineProfileViewModel>()
            : _catalog.Where(profile =>
            (string.IsNullOrEmpty(search)
             || profile.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase)
             || profile.Icao.Contains(search, StringComparison.OrdinalIgnoreCase))
            && MatchesActiveFilter(profile));

        VisibleAirlines.Clear();
        foreach (var profile in matches)
        {
            VisibleAirlines.Add(profile);
        }

        OnPropertyChanged(nameof(HasVisibleAirlines));
        OnPropertyChanged(nameof(EmptyCatalogMessage));
    }

    private bool MatchesActiveFilter(AirlineProfileViewModel profile) => ActiveFilter switch
    {
        "Real-world" => profile.Type == "Real-world",
        "Virtual Airlines" => profile.Type == "Virtual Airline",
        "vAMSYS" => profile.Type == "vAMSYS",
        "Installed Packs" => profile.IsInstalled,
        _ => true
    };

    private async Task ConnectVamsysAsync()
    {
        if (!_vamsysService.IsConfigured)
        {
            var setup = new VamsysSetupWindow(
                _settings.VamsysClientId,
                _settings.VamsysAirlineName,
                _settings.VamsysAirlineIcao,
                _settings.VamsysRedirectUri)
            {
                Owner = Application.Current.MainWindow
            };
            if (setup.ShowDialog() != true)
            {
                return;
            }

            _settings.VamsysClientId = setup.ClientId;
            _settings.VamsysAirlineName = setup.AirlineName;
            _settings.VamsysAirlineIcao = setup.AirlineIcao;
            _settings.VamsysRedirectUri = VamsysOAuthService.DefaultRedirectUri;
            await _settingsStore.SaveAsync(_settings);
            OnPropertyChanged(nameof(VamsysActionLabel));
        }

        IsVamsysBusy = true;
        VamsysConnectionStatus = "Complete the secure authorization in your browser";
        await _vamsysService.BeginAuthorizationAsync();
        _vamsysPollDeadline = DateTimeOffset.UtcNow.AddMinutes(10);
        _vamsysPollTimer.Start();
    }

    private async Task DisconnectVamsysAsync()
    {
        _vamsysPollTimer.Stop();
        await _vamsysService.DisconnectAsync();
        _vamsysProfile = null;
        _catalog.RemoveAll(profile => profile.Type == "vAMSYS");
        var realWorldFallback = _catalog.FirstOrDefault(profile => profile.Type == "Real-world" && profile.Icao == "BAW")
                                ?? _catalog.First(profile => profile.Type == "Real-world");
        SelectAirline(realWorldFallback);
        SelectedCatalogSource = "Real-world operators";
        IsVamsysBusy = false;
        VamsysConnectionStatus = "Disconnected locally · consent can also be revoked in vAMSYS";
        ApplyFilter();

        NotifyVamsysProfileChanged();
    }

    private async void HandleVamsysPollTick(object? sender, EventArgs e)
    {
        if (DateTimeOffset.UtcNow >= _vamsysPollDeadline)
        {
            _vamsysPollTimer.Stop();
            IsVamsysBusy = false;
            VamsysConnectionStatus = "Authorization timed out · choose Connect vAMSYS to try again";
            return;
        }

        await RefreshVamsysProfileAsync();
        if (IsVamsysConnected)
        {
            _vamsysPollTimer.Stop();
            IsVamsysBusy = false;
        }
    }

    private async Task RefreshVamsysProfileAsync()
    {
        try
        {
            var profile = await _vamsysService.TryGetPilotProfileAsync();
            if (profile is null)
            {
                if (!IsVamsysBusy)
                {
                    VamsysConnectionStatus = _vamsysService.IsConfigured
                        ? "Ready for secure browser authorization"
                        : "Developer registration required";
                }
                return;
            }

            ApplyVamsysProfile(profile);
        }
        catch (Exception exception)
        {
            _vamsysPollTimer.Stop();
            IsVamsysBusy = false;
            VamsysConnectionStatus = exception.Message;
        }
    }

    private void ApplyVamsysProfile(VamsysPilotProfile profile)
    {
        _vamsysProfile = profile;
        _catalog.RemoveAll(entry => entry.Type == "vAMSYS");
        var airline = new AirlineProfileViewModel(
            $"vamsys.{profile.AirlineId}",
            profile.AirlineName,
            profile.AirlineIcao,
            "vAMSYS",
            "vAMSYS linked profile",
            false);
        _catalog.Add(airline);
        VamsysConnectionStatus = $"Connected as {profile.PilotUsername} · {profile.AirlineName}";
        IsVamsysBusy = false;
        SelectedCatalogSource = "vAMSYS virtual airline";
        ApplyFilter();
        SelectAirline(airline);
        NotifyVamsysProfileChanged();
    }

    private void NotifyVamsysProfileChanged()
    {
        OnPropertyChanged(nameof(IsVamsysConnected));
        OnPropertyChanged(nameof(VamsysActionLabel));
        OnPropertyChanged(nameof(VamsysDisplayName));
        OnPropertyChanged(nameof(VamsysPilotLabel));
        OnPropertyChanged(nameof(VamsysEmail));
        OnPropertyChanged(nameof(VamsysRank));
        OnPropertyChanged(nameof(VamsysAvatarInitials));
        OnPropertyChanged(nameof(AirlineSourceStatus));
        OnPropertyChanged(nameof(EmptyCatalogMessage));
    }

    private void OpenAccountProfile()
    {
        if (!IsVamsysConnected)
        {
            return;
        }

        new VamsysAccountWindow
        {
            Owner = Application.Current.MainWindow,
            DataContext = this
        }.ShowDialog();
    }

    private static void OpenVamsysAccount() => Process.Start(new ProcessStartInfo(VamsysOAuthService.AccountPortalUrl)
    {
        UseShellExecute = true
    });

    private async Task ChooseProfileImageAsync()
    {
        var selected = SelectImage("Choose a local FreeFlight profile picture");
        if (selected is null)
        {
            return;
        }

        _settings.AccountProfileImagePath = CopyProfileAsset(selected, "profile-picture");
        await _settingsStore.SaveAsync(_settings);
        OnPropertyChanged(nameof(AccountProfileImageUri));
        OnPropertyChanged(nameof(HasAccountProfileImage));
    }

    private async Task ChooseBackgroundImageAsync()
    {
        var selected = SelectImage("Choose a local FreeFlight background image");
        if (selected is null)
        {
            return;
        }

        _settings.AccountBackgroundImagePath = CopyProfileAsset(selected, "account-background");
        await _settingsStore.SaveAsync(_settings);
        OnPropertyChanged(nameof(AccountBackgroundImageUri));
        OnPropertyChanged(nameof(ActiveBackgroundImageUri));
        OnPropertyChanged(nameof(HasActiveBackgroundImage));
    }

    private async Task RemoveProfileImageAsync()
    {
        _settings.AccountProfileImagePath = string.Empty;
        await _settingsStore.SaveAsync(_settings);
        OnPropertyChanged(nameof(AccountProfileImageUri));
        OnPropertyChanged(nameof(HasAccountProfileImage));
    }

    private async Task RemoveBackgroundImageAsync()
    {
        _settings.AccountBackgroundImagePath = string.Empty;
        await _settingsStore.SaveAsync(_settings);
        OnPropertyChanged(nameof(AccountBackgroundImageUri));
        OnPropertyChanged(nameof(ActiveBackgroundImageUri));
        OnPropertyChanged(nameof(HasActiveBackgroundImage));
    }

    private async Task SaveAppearanceAsync()
    {
        try
        {
            await _settingsStore.SaveAsync(_settings);
        }
        catch (Exception exception)
        {
            SelectionStatus = $"Appearance setting could not be saved: {exception.Message}";
        }
    }

    private static string? SelectImage(string title)
    {
        var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = "Image files|*.png;*.jpg;*.jpeg;*.bmp|All files|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        return dialog.ShowDialog(Application.Current.MainWindow) == true ? dialog.FileName : null;
    }

    private string CopyProfileAsset(string sourcePath, string baseName)
    {
        Directory.CreateDirectory(_profileAssetsDirectory);
        var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
        if (extension is not (".png" or ".jpg" or ".jpeg" or ".bmp"))
        {
            throw new InvalidOperationException("Choose a PNG, JPG, or BMP image.");
        }

        var destination = Path.Combine(_profileAssetsDirectory, baseName + extension);
        if (string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
        {
            return destination;
        }

        if (new FileInfo(sourcePath).Length > 20 * 1024 * 1024)
        {
            throw new InvalidOperationException("Choose an image smaller than 20 MB.");
        }

        File.Copy(sourcePath, destination, true);
        return destination;
    }

    private static Uri? ToExistingFileUri(string path) =>
        !string.IsNullOrWhiteSpace(path) && File.Exists(path)
            ? new Uri(Path.GetFullPath(path), UriKind.Absolute)
            : null;

    private void ShowVamsysError(Exception exception)
    {
        IsVamsysBusy = false;
        VamsysConnectionStatus = exception.Message;
        MessageBox.Show(exception.Message, "vAMSYS", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    public void Dispose()
    {
        _vamsysPollTimer.Stop();
        _vamsysPollTimer.Tick -= HandleVamsysPollTick;
        if (_vamsysService is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    private void CreateCustomProfile()
    {
        var dialog = new CustomAirlineProfileWindow
        {
            Owner = Application.Current.MainWindow
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var id = $"custom.{dialog.Icao.ToLowerInvariant()}.{Guid.NewGuid():N}";
        var savedProfile = new CustomAirlineProfileSettings
        {
            Id = id,
            Name = dialog.AirlineName,
            Icao = dialog.Icao,
            SoundPackName = dialog.SoundPackName
        };
        _settings.CustomAirlineProfiles.Add(savedProfile);

        var profile = new AirlineProfileViewModel(
            savedProfile.Id,
            savedProfile.Name,
            savedProfile.Icao,
            "Virtual Airline",
            savedProfile.SoundPackName,
            false);
        _catalog.Add(profile);
        OnPropertyChanged(nameof(AvailableProfileCount));
        ActiveFilter = AllFilter;
        SearchText = string.Empty;
        ApplyFilter();
        SelectAirline(profile);
    }
}
