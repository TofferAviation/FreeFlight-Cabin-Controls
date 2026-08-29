using System.Windows;
using FreeFlight.CabinControl.App.ViewModels;

namespace FreeFlight.CabinControl.App.Views;

public partial class UpdateNotificationWindow
{
    public UpdateNotificationWindow() => InitializeComponent();

    private void LaterButton_Click(object sender, RoutedEventArgs e) => Close();

    private void OpenChangelogButton_Click(object sender, RoutedEventArgs e)
    {
        var changelog = DataContext is UpdatesViewModel updates
            ? updates.Changelog
            : "No bundled changelog is available.";
        new ChangelogWindow(changelog) { Owner = this }.ShowDialog();
    }
}
