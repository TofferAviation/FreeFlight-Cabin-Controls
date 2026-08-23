using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using FreeFlight.CabinControl.App.Infrastructure;
using FreeFlight.CabinControl.App.Views;
using FreeFlight.CabinControl.Core.Configuration;
using FreeFlight.CabinControl.Core.Persistence;

namespace FreeFlight.CabinControl.App.ViewModels;

public sealed class AirlinersViewModel : PageViewModel
{
    private const string AllFilter = "All";
    private readonly AppSettings _settings;
    private readonly ISettingsStore _settingsStore;
    private readonly List<AirlineProfileViewModel> _catalog;
    private AirlineProfileViewModel _activeAirline;
    private string _searchText = string.Empty;
    private string _activeFilter = AllFilter;
    private string _selectionStatus = "Airline selection is stored locally";

    public AirlinersViewModel(
        AppSettings settings,
        ISettingsStore settingsStore,
        SharedStatusViewModel status)
        : base("Airliners", "Choose your airline experience")
    {
        _settings = settings;
        _settingsStore = settingsStore;
        Status = status;
        _catalog = CreateCatalog(settings.CustomAirlineProfiles);
        _activeAirline = _catalog.FirstOrDefault(profile =>
            string.Equals(profile.Id, settings.ActiveAirlineId, StringComparison.OrdinalIgnoreCase))
            ?? _catalog[0];
        _activeAirline.IsActive = true;

        SelectAirlineCommand = new RelayCommand(SelectAirline);
        FilterCommand = new RelayCommand(SetFilter);
        ChangeAirlineCommand = new RelayCommand(_ => ResetCatalog());
        ConnectVamsysCommand = new RelayCommand(_ => OpenVamsysSetup());
        CreateProfileCommand = new RelayCommand(_ => CreateCustomProfile());
        ApplyFilter();
    }

    public SharedStatusViewModel Status { get; }

    public ObservableCollection<AirlineProfileViewModel> VisibleAirlines { get; } = [];

    public ICommand SelectAirlineCommand { get; }

    public ICommand FilterCommand { get; }

    public ICommand ChangeAirlineCommand { get; }

    public ICommand ConnectVamsysCommand { get; }

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

    public int InstalledPackCount => _catalog.Count(profile => profile.IsInstalled);

    private static List<AirlineProfileViewModel> CreateCatalog(
        IEnumerable<CustomAirlineProfileSettings> customProfiles)
    {
        var catalog = new List<AirlineProfileViewModel>
        {
        new("freeflight.virtual", "FreeFlight Virtual", "FFV", "Virtual Airline", "FreeFlight Cabin Pack", true),
        new("british-airways", "British Airways", "BAW", "Real-world", "British Airways 777 pack", false),
        new("nordic-air", "Nordic Air", "NDA", "Real-world", "Nordic Air Cabin Pack", false),
        new("british-atlantic", "British Atlantic", "BAT", "Real-world", "British Atlantic Pack", false),
        new("emirates-sky", "Emirates Sky", "EMS", "Real-world", "Emirates Sky Pack", false),
        new("pacific-crown", "Pacific Crown", "PCR", "Real-world", "Pacific Crown Pack", false),
        new("global-charter", "Global Charter", "GCR", "Virtual Airline", "Global Charter Pack", false)
        };

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
        var matches = _catalog.Where(profile =>
            (string.IsNullOrEmpty(search)
             || profile.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase)
             || profile.Icao.Contains(search, StringComparison.OrdinalIgnoreCase))
            && MatchesActiveFilter(profile));

        VisibleAirlines.Clear();
        foreach (var profile in matches)
        {
            VisibleAirlines.Add(profile);
        }
    }

    private bool MatchesActiveFilter(AirlineProfileViewModel profile) => ActiveFilter switch
    {
        "Real-world" => profile.Type == "Real-world",
        "Virtual Airlines" => profile.Type == "Virtual Airline",
        "Installed Packs" => profile.IsInstalled,
        _ => true
    };

    private static void OpenVamsysSetup()
    {
        var choice = MessageBox.Show(
            "FreeFlight will use vAMSYS browser authorization and will never ask for your vAMSYS password. " +
            "An approved vAMSYS Pilot API client registration is required before sign-in can be enabled.\n\n" +
            "Open the official vAMSYS developer documentation?",
            "vAMSYS connection setup",
            MessageBoxButton.YesNo,
            MessageBoxImage.Information);
        if (choice != MessageBoxResult.Yes)
        {
            return;
        }

        Process.Start(new ProcessStartInfo("https://vamsys.io/docs/pilot")
        {
            UseShellExecute = true
        });
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
