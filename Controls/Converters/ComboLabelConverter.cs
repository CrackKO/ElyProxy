using System.Globalization;
using System.Windows.Controls;
using System.Windows.Data;

namespace ElyProxy.Controls.Converters;

/// <summary>
/// Renders the closed ComboBox selection as its display label. The built-in
/// <c>SelectionBoxItem</c>/<c>SelectionBoxItemTemplate</c> pair shows the raw
/// item (for example "[Dark, Тёмная]"), so the label is resolved here:
/// KeyValuePair → Value, ComboBoxItem → Content, otherwise ToString().
/// </summary>
public sealed class ComboLabelConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        null => string.Empty,
        string text => text,
        ComboBoxItem item => item.Content?.ToString() ?? string.Empty,
        KeyValuePair<string, string> pair => pair.Value,
        KeyValuePair<int, string> pair => pair.Value,
        _ => value.ToString() ?? string.Empty
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
