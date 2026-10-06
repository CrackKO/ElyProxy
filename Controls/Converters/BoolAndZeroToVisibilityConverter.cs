using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ElyProxy.Controls.Converters;

/// <summary>Visible when a busy flag is true and a collection is still empty (initial load).</summary>
public sealed class BoolAndZeroToVisibilityConverter : IMultiValueConverter
{
    public object Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture) =>
        values.Length >= 2 && values[0] is true && values[1] is int count && count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
