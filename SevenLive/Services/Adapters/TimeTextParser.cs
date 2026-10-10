using System.Globalization;
using System.Text.RegularExpressions;

namespace SevenLive.Services.Adapters;

public static class TimeTextParser
{
    private static readonly Regex TimeRegex = new(@"(\d{1,2}):(\d{2})", RegexOptions.Compiled);
    private static readonly Regex DateRegex = new(@"(\d{1,2})\.(\d{1,2})\.(\d{4})", RegexOptions.Compiled);
    private static readonly CultureInfo Russian = CultureInfo.GetCultureInfo("ru-RU");

    private static readonly string[] DateTimeFormats =
    {
        "d.M.yyyy H:mm",
        "dd.MM.yyyy H:mm",
        "d.M.yyyy HH:mm",
        "dd.MM.yyyy HH:mm",
        "d.M.yyyy H:mm:ss",
        "dd.MM.yyyy HH:mm:ss",
    };

    public static TimeSpan? ParseTime(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var match = TimeRegex.Match(text);
        if (!match.Success)
        {
            return null;
        }

        var hours = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        var minutes = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
        if (hours > 23 || minutes > 59)
        {
            return null;
        }

        return new TimeSpan(hours, minutes, 0);
    }

    public static DateTime? ParseDate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var match = DateRegex.Match(text);
        if (match.Success)
        {
            var day = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            var month = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
            var year = int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
            try
            {
                return new DateTime(year, month, day);
            }
            catch (ArgumentOutOfRangeException)
            {
                return null;
            }
        }

        if (DateTime.TryParse(text, Russian, DateTimeStyles.None, out var parsed))
        {
            return parsed.Date;
        }

        return null;
    }

    public static DateTime? ParseDateTime(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var trimmed = text.Trim();
        if (DateTime.TryParseExact(trimmed, DateTimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var exact))
        {
            return ToUtc(exact);
        }

        if (DateTime.TryParse(trimmed, Russian, DateTimeStyles.None, out var parsed))
        {
            return ToUtc(parsed);
        }

        return null;
    }

    public static DateTime? CombineWithDate(TimeSpan? time, DateTime? date, DateTime nowUtc)
    {
        if (time == null)
        {
            return null;
        }

        var localNow = nowUtc.ToLocalTime();
        var candidate = (date?.Date ?? localNow.Date).Add(time.Value);
        if (date == null && candidate > localNow.AddHours(1))
        {
            candidate = candidate.AddDays(-1);
        }

        return ToUtc(candidate);
    }

    private static DateTime ToUtc(DateTime value)
    {
        var local = DateTime.SpecifyKind(value, DateTimeKind.Local);
        return local.ToUniversalTime();
    }
}
