using System.ComponentModel;
using System.Windows.Media;
using FreeFlight.CabinControl.App.ViewModels;

namespace FreeFlight.CabinControl.App.Views;

public partial class CabinControlPanelView
{
    private CabinControlPanelViewModel? _attachedViewModel;
    private readonly MediaPlayer _boardingMusicPlayer = new();
    private Uri? _boardingMusicSource;
    private readonly MediaPlayer _passengerAmbiencePlayer = new();
    private Uri? _passengerAmbienceSource;

    public CabinControlPanelView()
    {
        InitializeComponent();
        Loaded += HandleLoaded;
        Unloaded += HandleUnloaded;
        DataContextChanged += HandleDataContextChanged;
        _boardingMusicPlayer.MediaEnded += HandleBoardingMusicEnded;
        _boardingMusicPlayer.MediaFailed += HandleBoardingMusicFailed;
        _passengerAmbiencePlayer.MediaEnded += HandlePassengerAmbienceEnded;
        _passengerAmbiencePlayer.MediaFailed += HandlePassengerAmbienceFailed;
    }

    private void HandleLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        AttachToViewModel(DataContext as CabinControlPanelViewModel);
    }

    private void HandleUnloaded(object sender, System.Windows.RoutedEventArgs e)
    {
        SafetyVideoMediaElement.Stop();
        SafetyVideoMediaElement.Source = null;
        _boardingMusicPlayer.Close();
        _boardingMusicSource = null;
        _passengerAmbiencePlayer.Close();
        _passengerAmbienceSource = null;
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
        UpdateLocalSafetyVideoPlayback();
        UpdateBoardingMusicPlayback();
        UpdatePassengerAmbiencePlayback();
    }

    private void HandleViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(CabinControlPanelViewModel.SafetyVideoLocalSource) or
            nameof(CabinControlPanelViewModel.IsUsingLocalSafetyVideo))
        {
            UpdateLocalSafetyVideoPlayback();
        }

        if (e.PropertyName is nameof(CabinControlPanelViewModel.BoardingMusicLocalSource) or
            nameof(CabinControlPanelViewModel.IsBoardingMusicPlaying) or
            nameof(CabinControlPanelViewModel.BoardingMusicOutputVolume))
        {
            UpdateBoardingMusicPlayback();
        }

        if (e.PropertyName is nameof(CabinControlPanelViewModel.PassengerAmbienceLocalSource) or
            nameof(CabinControlPanelViewModel.IsPassengerAmbiencePlaying) or
            nameof(CabinControlPanelViewModel.PassengerAmbienceOutputVolume))
        {
            UpdatePassengerAmbiencePlayback();
        }
    }

    private void UpdateBoardingMusicPlayback()
    {
        var viewModel = _attachedViewModel;
        _boardingMusicPlayer.Volume = viewModel?.BoardingMusicOutputVolume ?? 0d;
        if (viewModel?.IsBoardingMusicPlaying == true && viewModel.BoardingMusicLocalSource is not null)
        {
            if (_boardingMusicSource != viewModel.BoardingMusicLocalSource)
            {
                _boardingMusicSource = viewModel.BoardingMusicLocalSource;
                _boardingMusicPlayer.Open(_boardingMusicSource);
            }

            _boardingMusicPlayer.Play();
            return;
        }

        _boardingMusicPlayer.Stop();
    }

    private void HandleBoardingMusicEnded(object? sender, EventArgs e)
    {
        if (_attachedViewModel?.IsBoardingMusicPlaying != true)
        {
            return;
        }

        _boardingMusicPlayer.Position = TimeSpan.Zero;
        _boardingMusicPlayer.Play();
    }

    private void HandleBoardingMusicFailed(object? sender, ExceptionEventArgs e)
    {
        _boardingMusicPlayer.Close();
        _boardingMusicSource = null;
        _attachedViewModel?.ReportBoardingMusicPlaybackFailure(e.ErrorException?.Message);
    }

    private void UpdatePassengerAmbiencePlayback()
    {
        var viewModel = _attachedViewModel;
        _passengerAmbiencePlayer.Volume = viewModel?.PassengerAmbienceOutputVolume ?? 0d;
        if (viewModel?.IsPassengerAmbiencePlaying == true && viewModel.PassengerAmbienceLocalSource is not null)
        {
            if (_passengerAmbienceSource != viewModel.PassengerAmbienceLocalSource)
            {
                _passengerAmbienceSource = viewModel.PassengerAmbienceLocalSource;
                _passengerAmbiencePlayer.Open(_passengerAmbienceSource);
            }

            _passengerAmbiencePlayer.Play();
            return;
        }

        _passengerAmbiencePlayer.Stop();
    }

    private void HandlePassengerAmbienceEnded(object? sender, EventArgs e)
    {
        if (_attachedViewModel?.IsPassengerAmbiencePlaying != true)
        {
            return;
        }

        _passengerAmbiencePlayer.Position = TimeSpan.Zero;
        _passengerAmbiencePlayer.Play();
    }

    private void HandlePassengerAmbienceFailed(object? sender, ExceptionEventArgs e)
    {
        _passengerAmbiencePlayer.Close();
        _passengerAmbienceSource = null;
        _attachedViewModel?.ReportPassengerAmbiencePlaybackFailure(e.ErrorException?.Message);
    }

    private void UpdateLocalSafetyVideoPlayback()
    {
        var viewModel = _attachedViewModel;
        if (viewModel?.IsUsingLocalSafetyVideo == true && viewModel.SafetyVideoLocalSource is not null)
        {
            if (SafetyVideoMediaElement.Source != viewModel.SafetyVideoLocalSource)
            {
                SafetyVideoMediaElement.Source = viewModel.SafetyVideoLocalSource;
                SafetyVideoMediaElement.Position = TimeSpan.Zero;
            }

            SafetyVideoMediaElement.Play();
            return;
        }

        SafetyVideoMediaElement.Stop();
        SafetyVideoMediaElement.Source = null;
    }

    private void HandleLocalSafetyVideoEnded(object sender, System.Windows.RoutedEventArgs e)
    {
        _attachedViewModel?.StopSafetyVideoCommand.Execute(null);
    }

    private void HandleLocalSafetyVideoFailed(object sender, System.Windows.ExceptionRoutedEventArgs e)
    {
        _attachedViewModel?.ReportSafetyVideoPlaybackFailure(e.ErrorException?.Message);
    }
}
