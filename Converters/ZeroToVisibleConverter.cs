using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ColonyTracker.Converters;

/// <summary>Visible when the bound int is 0 (used for "everything delivered" empty-state text), Collapsed otherwise.
/// Pass ConverterParameter="Invert" for the opposite (visible only while something's still outstanding).</summary>
public sealed class ZeroToVisibleConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isZero = value is int i && i == 0;
        var invert = string.Equals(parameter as string, "Invert", StringComparison.OrdinalIgnoreCase);
        return isZero != invert ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
