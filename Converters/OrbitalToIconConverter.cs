using System.Globalization;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace ColonyTracker.Converters;

/// <summary>Picks the orbital or surface-settlement station icon from a bool? (true = orbital,
/// false = planetary, null = no best source yet - no icon).</summary>
public sealed class OrbitalToIconConverter : IValueConverter
{
    private static readonly BitmapImage Orbital = new(new Uri("pack://application:,,,/Assets/Orbital.png"));
    private static readonly BitmapImage Planetary = new(new Uri("pack://application:,,,/Assets/Planetary.png"));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value switch
        {
            true => Orbital,
            false => Planetary,
            _ => null
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
