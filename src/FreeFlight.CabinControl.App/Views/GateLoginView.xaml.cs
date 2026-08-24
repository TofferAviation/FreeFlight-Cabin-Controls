using System.Windows;
using System.Windows.Controls;
using FreeFlight.CabinControl.App.ViewModels;

namespace FreeFlight.CabinControl.App.Views;

public partial class GateLoginView
{
    private GateLoginViewModel? _viewModel;

    public GateLoginView()
    {
        InitializeComponent();
        Loaded += HandleLoaded;
        Unloaded += HandleUnloaded;
    }

    private void PasswordInput_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is GateLoginViewModel viewModel && sender is PasswordBox passwordBox)
        {
            viewModel.Password = passwordBox.Password;
        }
    }

    private void HandleLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is not GateLoginViewModel viewModel || ReferenceEquals(_viewModel, viewModel))
        {
            return;
        }

        HandleUnloaded(sender, e);
        _viewModel = viewModel;
        _viewModel.SignedIn += HandleSignedIn;
    }

    private void HandleUnloaded(object sender, RoutedEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.SignedIn -= HandleSignedIn;
            _viewModel = null;
        }
    }

    private void HandleSignedIn(object? sender, EventArgs e) => PasswordInput.Clear();
}
