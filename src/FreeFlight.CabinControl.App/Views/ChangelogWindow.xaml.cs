using System.Windows;

namespace FreeFlight.CabinControl.App.Views;

public partial class ChangelogWindow
{
    public ChangelogWindow(string changelog)
    {
        InitializeComponent();
        ChangelogText.Text = string.IsNullOrWhiteSpace(changelog)
            ? "No bundled changelog is available."
            : changelog;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
