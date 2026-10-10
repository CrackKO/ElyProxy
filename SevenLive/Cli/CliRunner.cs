using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using FlaUI.UIA3;
using SevenLive.Data;
using SevenLive.Models;
using SevenLive.Reporting;
using SevenLive.Services;

namespace SevenLive.Cli;

public static class CliRunner
{
    public static int Run(string[] args)
    {
        try
        {
            return Dispatch(args);
        }
        catch (ArgumentException exception)
        {
            Console.WriteLine(exception.Message);
            Console.WriteLine();
            PrintUsage();
            return 2;
        }
    }

    public static void PrintUsage()
    {
        Console.WriteLine("""
            SevenLive — локальный журнал содержимого окон Windows (UI Automation + SQLite).

            Использование:
              SevenLive [команда] [параметры]

            Команды:
              watch                    следить за активным окном и писать снимки (по умолчанию)
              once                     один проход: список окон + снимок активного окна
              windows                  список открытых окон
              read                     история чатов и сообщений (Telegram/Discord)
              read <номер>             история чата №N из списка (см. read --list)
              debug                    глубокий дамп UI Automation (Raw View, все patterns)
              snapshots                последние raw-снимки из базы
              show <id>                дерево элементов raw-снимка
              search <текст>           поиск текста по сохранённым снимкам
              stats                    статистика базы и адаптеров
              clean                    удалить старые снимки
              export <id>              выгрузить снимок в json/csv
              help                     эта справка

            Параметры:
              --db <путь>              файл SQLite (по умолчанию %LOCALAPPDATA%\SevenLive\sevenlive.db)
              --window <s>             окно: подстрока title/процесса, PID или HWND (0x...) — для once/debug
              --view raw|control       дерево UI Automation для снимков (по умолчанию raw)
              --interval <сек>         период опроса в watch (по умолчанию 3)
              --max-depth <n>          глубина дерева UI Automation (по умолчанию 32)
              --max-elements <n>       элементов на снимок (снимки 1500, debug 4000)
              --limit <n>              сколько строк выводить (snapshots 20, show 1000, read 200, search 50)
              --list                   для read: только список чатов, без меню
              --all                    для windows: показывать и окна без заголовка
              --keep-days <n>          для clean: хранить снимки N дней (по умолчанию 30)
              --vacuum                 для clean: сжать файл базы после удаления
              --format json|csv        для export (по умолчанию json)
              --out <файл>             для export: писать в файл (по умолчанию в stdout)
              -h, --help               эта справка

            Примеры:
              SevenLive watch --interval 2
              SevenLive once --window AyuGram
              SevenLive read                       # меню чатов
              SevenLive read --list                # только список чатов
              SevenLive read 2 --limit 100         # история чата №2
              SevenLive debug --window AyuGram > ayugram.txt
              SevenLive snapshots --limit 30
              SevenLive search "роутинг"
              SevenLive export 12 --format json --out snapshot12.json
              SevenLive clean --keep-days 7 --vacuum
            """);
    }

    private static int Dispatch(string[] args)
    {
        if (args.Length == 0)
        {
            return Watch(Array.Empty<string>());
        }

        var command = args[0];
        var rest = args[1..];

        switch (command)
        {
            case "watch":
            case "run":
                return Watch(rest);
            case "once":
            case "--once":
                return Once(rest);
            case "windows":
            case "list":
                return Windows(rest);
            case "debug":
            case "--debug":
                return Debug(rest);
            case "snapshots":
            case "log":
            case "--dump":
                return Snapshots(rest);
            case "read":
            case "history":
                return Read(rest);
            case "show":
                return Show(rest);
            case "search":
            case "find":
                return Search(rest);
            case "stats":
                return Stats(rest);
            case "clean":
            case "prune":
                return Clean(rest);
            case "export":
                return Export(rest);
            case "help":
            case "--help":
            case "-h":
            case "/?":
                PrintUsage();
                return 0;
            default:
                if (command.StartsWith('-'))
                {
                    return Watch(args);
                }

                Console.WriteLine($"Неизвестная команда: {command}");
                Console.WriteLine();
                PrintUsage();
                return 2;
        }
    }

    private static int Watch(string[] args)
    {
        var (options, _) = CliParser.Parse(args);
        using var store = new SnapshotStore(options.DatabasePath);
        using var automation = new UIA3Automation();
        automation.ConnectionTimeout = TimeSpan.FromSeconds(2);

        var watcher = new WindowWatcher(automation, store, options);
        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };

        watcher.Run(cancellation.Token);
        return 0;
    }

    private static int Once(string[] args)
    {
        var (options, _) = CliParser.Parse(args);
        using var store = new SnapshotStore(options.DatabasePath);
        using var automation = new UIA3Automation();
        automation.ConnectionTimeout = TimeSpan.FromSeconds(2);

        var watcher = new WindowWatcher(automation, store, options);
        watcher.RunOnce(options.WindowSelector);
        return 0;
    }

    private static int Windows(string[] args)
    {
        var (options, _) = CliParser.Parse(args);
        var enumerator = new WindowEnumerator();
        ConsoleReporter.PrintWindowList(enumerator.GetOpenWindows(options.IncludeAll));
        return 0;
    }

    private static int Debug(string[] args)
    {
        var (options, _) = CliParser.Parse(args);
        using var automation = new UIA3Automation();
        automation.ConnectionTimeout = TimeSpan.FromSeconds(5);

        var enumerator = new WindowEnumerator();
        var target = string.IsNullOrWhiteSpace(options.WindowSelector)
            ? enumerator.GetForegroundWindowInfo()
            : enumerator.FindWindow(options.WindowSelector!);

        if (target == null)
        {
            Console.WriteLine(options.WindowSelector == null
                ? "Активное окно не найдено."
                : $"Окно по запросу \"{options.WindowSelector}\" не найдено.");
            return 3;
        }

        var dumper = new UiDebugDumper(automation, Console.Out, options.DebugMaxDepth, options.DebugMaxElements);
        dumper.Dump(target);
        return 0;
    }

    private static int Snapshots(string[] args)
    {
        var (options, _) = CliParser.Parse(args);
        using var store = new SnapshotStore(options.DatabasePath);
        ViewReporter.PrintSnapshots(options.DatabasePath, store.ReadRecentSnapshots(options.Limit ?? 20));
        return 0;
    }

    private static int Show(string[] args)
    {
        var (options, positionals) = CliParser.Parse(args);
        if (positionals.Count == 0)
        {
            Console.WriteLine("Укажите id снимка: SevenLive show <id>");
            return 2;
        }

        var snapshotId = CliParser.ParseInt(positionals[0], "show <id>");
        var limit = options.Limit ?? 1000;

        using var store = new SnapshotStore(options.DatabasePath);
        var snapshot = store.ReadSnapshot(snapshotId);
        if (snapshot == null)
        {
            Console.WriteLine($"Снимок #{snapshotId} не найден в {options.DatabasePath}.");
            return 3;
        }

        ViewReporter.PrintSnapshotTree(snapshot, store.ReadSnapshotElements(snapshotId, limit), limit);
        return 0;
    }

    private static int Search(string[] args)
    {
        var (options, positionals) = CliParser.Parse(args);
        if (positionals.Count == 0)
        {
            Console.WriteLine("Укажите текст для поиска: SevenLive search <текст>");
            return 2;
        }

        var query = string.Join(' ', positionals);
        using var store = new SnapshotStore(options.DatabasePath);
        ViewReporter.PrintSearch(query, store.SearchElements(query, options.Limit ?? 50));
        return 0;
    }

    private static int Stats(string[] args)
    {
        var (options, _) = CliParser.Parse(args);
        using var store = new SnapshotStore(options.DatabasePath);
        ViewReporter.PrintStatistics(options.DatabasePath, store.ReadStatistics());
        JournalReporter.PrintAdapterRuns(store.ReadRecentAdapterRuns(10));
        return 0;
    }

    private static int Read(string[] args)
    {
        var (options, positionals) = CliParser.Parse(args);
        var limit = options.Limit ?? 200;

        using var store = new SnapshotStore(options.DatabasePath);

        if (options.ListConversations)
        {
            JournalReporter.PrintConversations(store.ReadConversations());
            return 0;
        }

        if (positionals.Count > 0)
        {
            var index = CliParser.ParseInt(positionals[0], "read <номер>");
            var conversations = store.ReadConversations();
            if (index > conversations.Count)
            {
                Console.WriteLine($"В списке только {conversations.Count} чатов, а запрошен №{index}.");
                return 3;
            }

            var conversation = conversations[index - 1];
            JournalReporter.PrintMessages(conversation, store.ReadMessages(conversation.Id, limit));
            return 0;
        }

        return InteractiveRead(store, limit);
    }

    private static int InteractiveRead(SnapshotStore store, int limit)
    {
        var conversations = store.ReadConversations();
        JournalReporter.PrintConversations(conversations);
        if (conversations.Count == 0)
        {
            return 0;
        }

        if (Console.IsInputRedirected)
        {
            Console.WriteLine();
            Console.WriteLine("Неинтерактивный ввод: SevenLive read <номер чата> [--limit N]");
            return 0;
        }

        while (true)
        {
            Console.WriteLine();
            Console.Write("Номер чата (q — выход): ");
            var input = Console.ReadLine();
            if (input == null)
            {
                break;
            }

            input = input.Trim();
            if (input.Length == 0)
            {
                continue;
            }

            if (input is "q" or "quit" or "exit")
            {
                break;
            }

            if (!int.TryParse(input, out var index) || index < 1 || index > conversations.Count)
            {
                Console.WriteLine("Нет такого номера.");
                continue;
            }

            var conversation = conversations[index - 1];
            JournalReporter.PrintMessages(conversation, store.ReadMessages(conversation.Id, limit));
        }

        return 0;
    }

    private static int Clean(string[] args)
    {
        var (options, _) = CliParser.Parse(args);
        using var store = new SnapshotStore(options.DatabasePath);

        var cutoff = DateTime.UtcNow.AddDays(-options.KeepDaysValue);
        var deletedSnapshots = store.DeleteSnapshotsOlderThan(cutoff);
        var deletedWindows = store.DeleteOrphanWindows();
        if (options.Vacuum)
        {
            store.Vacuum();
        }

        ViewReporter.PrintClean(deletedSnapshots, deletedWindows, options.KeepDaysValue, options.Vacuum, options.DatabasePath);
        return 0;
    }

    private static int Export(string[] args)
    {
        var (options, positionals) = CliParser.Parse(args);
        if (positionals.Count == 0)
        {
            Console.WriteLine("Укажите id снимка: SevenLive export <id> [--format json|csv] [--out файл]");
            return 2;
        }

        var snapshotId = CliParser.ParseInt(positionals[0], "export <id>");
        var format = options.ExportFormat ?? "json";
        if (format is not ("json" or "csv"))
        {
            throw new ArgumentException($"Некорректное значение для --format: {format} (ожидается json или csv)");
        }

        using var store = new SnapshotStore(options.DatabasePath);
        var snapshot = store.ReadSnapshot(snapshotId);
        if (snapshot == null)
        {
            Console.WriteLine($"Снимок #{snapshotId} не найден в {options.DatabasePath}.");
            return 3;
        }

        var elements = store.ReadSnapshotElements(snapshotId, int.MaxValue);
        var text = format == "json" ? BuildJson(snapshot, elements) : BuildCsv(elements);

        if (options.OutputPath == null)
        {
            Console.Write(text);
            return 0;
        }

        File.WriteAllText(options.OutputPath, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: format == "csv"));
        ViewReporter.PrintExport(elements.Count, format, options.OutputPath);
        return 0;
    }

    private static string BuildJson(SnapshotSummary snapshot, IReadOnlyList<StoredElement> elements)
    {
        var payload = new
        {
            snapshot = new
            {
                id = snapshot.Id,
                capturedUtc = snapshot.CapturedUtc.ToString("o"),
                processName = snapshot.ProcessName,
                title = snapshot.Title,
                contentHash = snapshot.ContentHash,
                elementCount = snapshot.ElementCount,
                isActive = snapshot.IsActive,
            },
            elements = elements.Select(element => new
            {
                id = element.Id,
                parentElementId = element.ParentElementId,
                depth = element.Depth,
                controlType = element.ControlType,
                name = element.Name,
                automationId = element.AutomationId,
                className = element.ClassName,
                value = element.Value,
                bounds = new { x = element.X, y = element.Y, width = element.Width, height = element.Height },
                isEnabled = element.IsEnabled,
            }),
        };

        return JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });
    }

    private static string BuildCsv(IReadOnlyList<StoredElement> elements)
    {
        var builder = new StringBuilder();
        builder.AppendLine("id,parent_id,depth,control_type,name,automation_id,class_name,value,x,y,width,height,is_enabled");
        foreach (var element in elements)
        {
            builder.Append(element.Id).Append(',')
                .Append(element.ParentElementId?.ToString() ?? string.Empty).Append(',')
                .Append(element.Depth).Append(',')
                .Append(CsvField(element.ControlType)).Append(',')
                .Append(CsvField(element.Name)).Append(',')
                .Append(CsvField(element.AutomationId)).Append(',')
                .Append(CsvField(element.ClassName)).Append(',')
                .Append(CsvField(element.Value)).Append(',')
                .Append(element.X).Append(',')
                .Append(element.Y).Append(',')
                .Append(element.Width).Append(',')
                .Append(element.Height).Append(',')
                .Append(element.IsEnabled ? 1 : 0)
                .AppendLine();
        }

        return builder.ToString();
    }

    private static string CsvField(string? value)
    {
        return string.IsNullOrEmpty(value) ? string.Empty : "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
