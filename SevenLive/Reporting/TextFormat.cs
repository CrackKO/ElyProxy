namespace SevenLive.Reporting;

public static class TextFormat
{
    public const int DefaultMaxTextLength = 120;

    public static string Truncate(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
        {
            return text;
        }

        return text[..maxLength] + $"…(+{text.Length - maxLength})";
    }

    public static string SingleLine(string text, int maxLength = DefaultMaxTextLength)
    {
        return Truncate(text.Replace("\r", string.Empty).Replace("\n", "\\n"), maxLength);
    }

    public static string ShortHash(string hash)
    {
        return hash.Length <= 12 ? hash : hash[..12];
    }

    public static string Quote(string? text, int maxLength = DefaultMaxTextLength)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "\"\"";
        }

        return "\"" + SingleLine(text, maxLength) + "\"";
    }

    public static string FormatSize(long bytes)
    {
        string[] units = { "Б", "КБ", "МБ", "ГБ" };
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.#} {units[unit]}";
    }
}
