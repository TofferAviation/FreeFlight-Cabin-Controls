using FreeFlight.CabinControl.App.Infrastructure;

namespace FreeFlight.CabinControl.App.ViewModels;

public sealed class SharedStatusViewModel : ObservableObject
{
    private bool _isConnected;
    private string _connectionLabel = "PLUGIN DISCONNECTED";
    private string _connectionDetail = "Application preview mode";

    public bool IsConnected
    {
        get => _isConnected;
        set => SetProperty(ref _isConnected, value);
    }

    public string ConnectionLabel
    {
        get => _connectionLabel;
        set => SetProperty(ref _connectionLabel, value);
    }

    public string ConnectionDetail
    {
        get => _connectionDetail;
        set => SetProperty(ref _connectionDetail, value);
    }
}
