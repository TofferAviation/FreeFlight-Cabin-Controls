using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace FreeFlight.CabinControl.App.Infrastructure;

/// <summary>
/// Draws the live service percentage as a smooth clockwise arc for the Overview.
/// The source remains the existing catering progress value; this only gives it a
/// more immediately readable visual treatment.
/// </summary>
public sealed class ProgressArcGeometryConverter : IValueConverter
{
    private const double Centre = 42d;
    private const double Radius = 35d;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not IConvertible convertible ||
            !double.TryParse(convertible.ToString(culture), NumberStyles.Float, culture, out var percentage))
        {
            return Geometry.Empty;
        }

        var normalized = Math.Clamp(percentage, 0d, 100d);
        if (normalized <= 0d)
        {
            return Geometry.Empty;
        }

        if (normalized >= 100d)
        {
            return new EllipseGeometry(new Point(Centre, Centre), Radius, Radius);
        }

        var sweepAngle = normalized / 100d * 360d;
        var endAngle = (-90d + sweepAngle) * Math.PI / 180d;
        var start = new Point(Centre, Centre - Radius);
        var end = new Point(
            Centre + Radius * Math.Cos(endAngle),
            Centre + Radius * Math.Sin(endAngle));

        var figure = new PathFigure { StartPoint = start, IsClosed = false, IsFilled = false };
        figure.Segments.Add(new ArcSegment(
            end,
            new Size(Radius, Radius),
            0d,
            sweepAngle > 180d,
            SweepDirection.Clockwise,
            true));

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return geometry;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
