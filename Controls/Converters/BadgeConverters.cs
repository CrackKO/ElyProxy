using System.Globalization;
using System.Windows.Data;
using ElyProxy.Services;

namespace ElyProxy.Controls.Converters;

/// <summary>
/// Shared logic for server badges. WPF cannot render colour emoji, so a flag
/// emoji (🇸🇪) is converted into its country code ("SE") and, in Flag mode,
/// into a bundled flag image; names that already start with a two-letter code
/// keep it, everything else falls back to a two-letter monogram.
/// </summary>
internal static class BadgeText
{
    public const string FlagMode = "Flag";
    public const string MonogramMode = "Monogram";
    public const string NoneMode = "None";

    public static string For(string? name, string? mode)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "?";
        }

        if (string.Equals(mode, MonogramMode, StringComparison.OrdinalIgnoreCase))
        {
            return Monogram(name);
        }

        return FlagCatalog.ParseCountryCode(name) ?? Monogram(name);
    }

    public static string Monogram(string text)
    {
        var letters = new List<char>(2);
        var started = false;

        foreach (var character in text)
        {
            // Skip leading emoji, punctuation and spaces ("🇸🇪 Швеция" → "ШВ").
            if (char.IsSurrogate(character) || !char.IsLetterOrDigit(character))
            {
                if (started && char.IsWhiteSpace(character))
                {
                    break;
                }

                continue;
            }

            started = true;
            letters.Add(character);
            if (letters.Count == 2)
            {
                break;
            }
        }

        return letters.Count > 0 ? new string(letters.ToArray()).ToUpperInvariant() : "?";
    }
}

public sealed class BadgeTextConverter : IMultiValueConverter
{
    public object Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture) =>
        BadgeText.For(values.Length > 0 ? values[0] as string : null, values.Length > 1 ? values[1] as string : null);

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// MultiBinding (server name, badge mode) → flag image for the round badge.
/// Returns null outside Flag mode or when no flag asset matches, so the
/// monogram/code text underneath stays visible.
/// </summary>
public sealed class FlagImageConverter : IMultiValueConverter
{
    public object? Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Length < 2 || !string.Equals(values[1] as string, BadgeText.FlagMode, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return FlagCatalog.GetFlagImage(FlagCatalog.ParseCountryCode(values[0] as string));
    }

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class BadgeVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.Equals(value as string, BadgeText.NoneMode, StringComparison.OrdinalIgnoreCase)
            ? System.Windows.Visibility.Collapsed
            : System.Windows.Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
