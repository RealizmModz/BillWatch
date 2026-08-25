using System.Globalization;

namespace BillWatch.Converters;

public sealed class InvertedBoolConverter : IValueConverter
{
    public object Convert(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture)
    {
        return value is bool boolean && !boolean;
    }

    public object ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture)
    {
        return value is bool boolean && !boolean;
    }
}