using System.Text.RegularExpressions;
using System.Windows;

namespace FreeFlight.CabinControl.App.Views;

public partial class CustomAirlineProfileWindow
{
    private static readonly Regex IcaoPattern = new("^[A-Z0-9]{2,4}$", RegexOptions.CultureInvariant);

    public CustomAirlineProfileWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => AirlineNameTextBox.Focus();
    }

    public string AirlineName => AirlineNameTextBox.Text.Trim();

    public string Icao => IcaoTextBox.Text.Trim().ToUpperInvariant();

    public string SoundPackName => string.IsNullOrWhiteSpace(SoundPackTextBox.Text)
        ? "Custom cabin pack"
        : SoundPackTextBox.Text.Trim();

    private void CreateButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(AirlineName))
        {
            MessageBox.Show("Enter an airline name.", "Custom airline", MessageBoxButton.OK, MessageBoxImage.Warning);
            AirlineNameTextBox.Focus();
            return;
        }

        if (!IcaoPattern.IsMatch(Icao))
        {
            MessageBox.Show(
                "Enter a 2-4 character ICAO code using letters or numbers.",
                "Custom airline",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            IcaoTextBox.Focus();
            return;
        }

        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
