using System.Globalization;
using System.Windows.Data;

namespace ElyProxy.Controls.Converters;

/// <summary>
/// Shared logic for server badges. WPF cannot render colour emoji, so a flag
/// emoji (🇸🇪) is converted into its country code ("SE"); names that already
/// start with a two-letter code keep it, everything else falls back to a
/// two-letter monogram.
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

        return CountryCode(name) ?? Monogram(name);
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

    private static string? CountryCode(string text)
    {
        var trimmed = text.TrimStart();

        if (TryReadFlagEmoji(trimmed, out var flagCode))
        {
            return flagCode;
        }

        if (trimmed.Length >= 2 && IsAsciiLetter(trimmed[0]) && IsAsciiLetter(trimmed[1]))
        {
            return new string(new[] { char.ToUpperInvariant(trimmed[0]), char.ToUpperInvariant(trimmed[1]) });
        }

        return null;
    }

    private static bool TryReadFlagEmoji(string text, out string code)
    {
        code = string.Empty;

        if (text.Length < 4 || !char.IsSurrogatePair(text[0], text[1]) || !char.IsSurrogatePair(text[2], text[3]))
        {
            return false;
        }

        var first = char.ConvertToUtf32(text, 0);
        var second = char.ConvertToUtf32(text, 2);

        if (first is < 0x1F1E6 or > 0x1F1FF || second is < 0x1F1E6 or > 0x1F1FF)
        {
            return false;
        }

        code = new string(new[] { (char)('A' + first - 0x1F1E6), (char)('A' + second - 0x1F1E6) });
        return true;
    }

    private static bool IsAsciiLetter(char character) =>
        character is >= 'a' and <= 'z' or >= 'A' and <= 'Z';
}

public sealed class BadgeTextConverter : IMultiValueConverter
{
    public object Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture) =>
        BadgeText.For(values.Length > 0 ? values[0] as string : null, values.Length > 1 ? values[1] as string : null);

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
