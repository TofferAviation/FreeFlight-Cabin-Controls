using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace FreeFlight.CabinControl.App.Infrastructure;

public sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is bool boolean && !boolean;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is bool boolean && !boolean;
}

public sealed class BooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is Visibility.Visible;
}

public sealed class StringEqualsToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is string text && parameter is string expected &&
        string.Equals(text, expected, StringComparison.OrdinalIgnoreCase)
            ? Visibility.Visible
            : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

public sealed class PercentageToPositionConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not IConvertible convertible ||
            parameter is not string range ||
            !double.TryParse(convertible.ToString(culture), NumberStyles.Float, culture, out var percentage))
        {
            return 0d;
        }

        var bounds = range.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (bounds.Length != 2 ||
            !double.TryParse(bounds[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var minimum) ||
            !double.TryParse(bounds[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var maximum))
        {
            return 0d;
        }

        return minimum + (Math.Clamp(percentage, 0d, 100d) / 100d * (maximum - minimum));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
