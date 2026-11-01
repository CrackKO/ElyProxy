using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ElyProxy.Controls.Converters;

/// <summary>Visible when all bound values are equal (used to mark the connected node row).</summary>
public sealed class EqualsToVisibilityConverter : IMultiValueConverter
{
    public object Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Length < 2 || values[0] is null || values[1] is null)
        {
            return Visibility.Collapsed;
        }

        return string.Equals(values[0] as string, values[1] as string, StringComparison.Ordinal)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
