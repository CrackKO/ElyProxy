using System.Globalization;

namespace SevenLive.Cli;

public static class CliParser
{
    public static (AppOptions Options, List<string> Positionals) Parse(string[] args)
    {
        var options = new AppOptions();
        var positionals = new List<string>();
        var index = 0;

        while (index < args.Length)
        {
            var argument = args[index];
            index++;

            switch (argument)
            {
                case "--db":
                    options = options with { DatabasePath = Value(args, ref index, argument) };
                    break;
                case "--window":
                    options = options with { WindowSelector = Value(args, ref index, argument) };
                    break;
                case "--interval":
                    options = options with { IntervalSeconds = ParseDouble(Value(args, ref index, argument), argument) };
                    break;
                case "--max-depth":
                    options = options with { MaxDepth = ParseInt(Value(args, ref index, argument), argument) };
                    break;
                case "--max-elements":
                    options = options with { MaxElements = ParseInt(Value(args, ref index, argument), argument) };
                    break;
                case "--view":
                    options = options with { UseRawView = ParseView(Value(args, ref index, argument)) };
                    break;
                case "--limit":
                    options = options with { Limit = ParseInt(Value(args, ref index, argument), argument) };
                    break;
                case "--keep-days":
                    options = options with { KeepDays = ParseInt(Value(args, ref index, argument), argument) };
                    break;
                case "--format":
                    options = options with { ExportFormat = Value(args, ref index, argument).ToLowerInvariant() };
                    break;
                case "--out":
                    options = options with { OutputPath = Value(args, ref index, argument) };
                    break;
                case "--vacuum":
                    options = options with { Vacuum = true };
                    break;
                case "--all":
                    options = options with { IncludeAll = true };
                    break;
                case "--list":
                    options = options with { ListConversations = true };
                    break;
                default:
                    if (argument.StartsWith('-'))
                    {
                        throw new ArgumentException($"Неизвестный параметр: {argument}");
                    }

                    positionals.Add(argument);
                    break;
            }
        }

        return (options, positionals);
    }

    public static int ParseInt(string value, string option)
    {
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) && result > 0)
        {
            return result;
        }

        throw new ArgumentException($"Некорректное значение для {option}: {value}");
    }

    public static double ParseDouble(string value, string option)
    {
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) && result > 0)
        {
            return result;
        }

        throw new ArgumentException($"Некорректное значение для {option}: {value}");
    }

    private static string Value(string[] args, ref int index, string option)
    {
        if (index >= args.Length)
        {
            throw new ArgumentException($"Не указано значение для {option}");
        }

        return args[index++];
    }

    private static bool ParseView(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "raw" => true,
            "control" => false,
            _ => throw new ArgumentException($"Некорректное значение для --view: {value} (ожидается raw или control)"),
        };
    }
}
