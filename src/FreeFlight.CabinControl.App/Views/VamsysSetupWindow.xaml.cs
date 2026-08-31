using System.Diagnostics;
using System.Windows;
using FreeFlight.CabinControl.App.Services;

namespace FreeFlight.CabinControl.App.Views;

public partial class VamsysSetupWindow
{
    public VamsysSetupWindow(string clientId, string airlineName, string airlineIcao, string redirectUri)
    {
        InitializeComponent();
        ClientIdBox.Text = clientId;
        AirlineNameBox.Text = airlineName;
        AirlineIcaoBox.Text = airlineIcao;
        RedirectUriText.Text = string.IsNullOrWhiteSpace(redirectUri)
            ? VamsysOAuthService.DefaultRedirectUri
            : redirectUri;
    }

    public string ClientId => ClientIdBox.Text.Trim();

    public string AirlineName => AirlineNameBox.Text.Trim();

    public string AirlineIcao => AirlineIcaoBox.Text.Trim().ToUpperInvariant();

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!long.TryParse(ClientId, out var clientId) || clientId <= 0)
        {
            MessageBox.Show("Enter the numeric public client ID issued by vAMSYS.", "vAMSYS setup", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(AirlineName) || AirlineIcao.Length != 3 || !AirlineIcao.All(char.IsLetter))
        {
            MessageBox.Show("Enter the Virtual Airline name and its three-letter ICAO code.", "vAMSYS setup", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
    }

    private void OpenDocumentation_Click(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo("https://vamsys.io/docs/pilot") { UseShellExecute = true });
}
