using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ElyProxy.Services;

/// <summary>
/// Country flags for server badges. WPF cannot render colour emoji, so a flag
/// emoji (🇸🇪) or a leading two-letter code in a server name is mapped to a
/// bundled PNG flag (Assets/Flags, public domain via flagcdn.com).
/// </summary>
public static class FlagCatalog
{
    private static readonly object Gate = new object();
    private static readonly Dictionary<string, ImageSource?> Cache = new Dictionary<string, ImageSource?>(StringComparer.Ordinal);
    private static readonly Dictionary<string, string> Aliases = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["UK"] = "GB",
        ["EL"] = "GR"
    };

    public static string? ParseCountryCode(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var trimmed = name.TrimStart();

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

    public static string? Normalize(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        var trimmed = code.Trim().ToUpperInvariant();

        if (trimmed.Length != 2 || !IsAsciiLetter(trimmed[0]) || !IsAsciiLetter(trimmed[1]))
        {
            return null;
        }

        if (Aliases.TryGetValue(trimmed, out var alias))
        {
            return alias;
        }

        return trimmed;
    }

    public static ImageSource? GetFlagImage(string? code)
    {
        var normalized = Normalize(code);

        if (normalized == null)
        {
            return null;
        }

        lock (Gate)
        {
            if (Cache.TryGetValue(normalized, out var cached))
            {
                return cached;
            }
        }

        var image = LoadFlag(normalized);

        lock (Gate)
        {
            Cache[normalized] = image;
        }

        return image;
    }

    private static ImageSource? LoadFlag(string code)
    {
        try
        {
            var uri = new Uri($"pack://application:,,,/ElyProxy;component/Assets/Flags/{code.ToLowerInvariant()}.png", UriKind.Absolute);
            var resource = Application.GetResourceStream(uri);

            if (resource == null)
            {
                return null;
            }

            var image = new BitmapImage();
            image.BeginInit();
            image.StreamSource = resource.Stream;
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 60;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
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
