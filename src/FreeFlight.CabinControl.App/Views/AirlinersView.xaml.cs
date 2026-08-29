namespace FreeFlight.CabinControl.App.Views;

public partial class AirlinersView
{
    public AirlinersView()
    {
        InitializeComponent();
    }

    private void SearchTextBox_OnTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) =>
        CatalogScrollViewer?.ScrollToTop();

    private void CatalogSource_OnSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) =>
        CatalogScrollViewer?.ScrollToTop();
}
