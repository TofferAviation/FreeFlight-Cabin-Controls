using System.IO;
using System.ComponentModel;
using FreeFlight.CabinControl.App.ViewModels;
using Microsoft.Web.WebView2.Wpf;

namespace FreeFlight.CabinControl.App.Views;

public partial class CabinControlPanelView
{
    private CabinControlPanelViewModel? _attachedViewModel;

    public CabinControlPanelView()
    {
        InitializeComponent();
        SafetyVideoWebView.CreationProperties = new CoreWebView2CreationProperties
        {
            UserDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FreeFlight",
                "CabinControl",
                "WebView2")
        };
        Loaded += HandleLoaded;
        Unloaded += HandleUnloaded;
        DataContextChanged += HandleDataContextChanged;
    }

    private void HandleLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        AttachToViewModel(DataContext as CabinControlPanelViewModel);
    }

    private void HandleUnloaded(object sender, System.Windows.RoutedEventArgs e)
    {
        AttachToViewModel(null);
    }

    private void HandleDataContextChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
    {
        if (IsLoaded)
        {
            AttachToViewModel(e.NewValue as CabinControlPanelViewModel);
        }
    }

    private void AttachToViewModel(CabinControlPanelViewModel? viewModel)
    {
        if (ReferenceEquals(_attachedViewModel, viewModel))
        {
            return;
        }

        if (_attachedViewModel is not null)
        {
            _attachedViewModel.PropertyChanged -= HandleViewModelPropertyChanged;
        }

        _attachedViewModel = viewModel;
        if (_attachedViewModel is null)
        {
            return;
        }

        _attachedViewModel.PropertyChanged += HandleViewModelPropertyChanged;
        NavigateToSafetyVideoSource();
    }

    private void HandleViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CabinControlPanelViewModel.SafetyVideoEmbedSource))
        {
            NavigateToSafetyVideoSource();
        }
    }

    private void NavigateToSafetyVideoSource()
    {
        var source = _attachedViewModel?.SafetyVideoEmbedSource;
        if (source is not null)
        {
            SafetyVideoWebView.Source = source;
        }
    }
}
