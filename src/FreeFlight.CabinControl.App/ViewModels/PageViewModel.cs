using FreeFlight.CabinControl.App.Infrastructure;

namespace FreeFlight.CabinControl.App.ViewModels;

public abstract class PageViewModel(string title, string subtitle) : ObservableObject
{
    public string Title { get; } = title;

    public string Subtitle { get; } = subtitle;
}
