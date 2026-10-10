using SevenLive.Models;

namespace SevenLive.Reporting;

public static class ViewReporter
{
    public static void PrintSnapshots(string databasePath, IReadOnlyList<SnapshotSummary> snapshots)
    {
        Console.WriteLine($"База данных: {databasePath}");
        if (snapshots.Count == 0)
        {
            Console.WriteLine("Снимков нет.");
            return;
        }

        Console.WriteLine($"Последние снимки ({snapshots.Count}):");
        foreach (var snapshot in snapshots)
        {
            var when = snapshot.CapturedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
            Console.WriteLine(
                $"  #{snapshot.Id,-6} {when}  {snapshot.ProcessName,-20} элементов: {snapshot.ElementCount,-5} hash: {TextFormat.ShortHash(snapshot.ContentHash)}  \"{TextFormat.SingleLine(snapshot.Title, 60)}\"");
        }
    }

    public static void PrintSnapshotTree(SnapshotSummary snapshot, IReadOnlyList<StoredElement> elements, int limit)
    {
        Console.WriteLine($"Снимок #{snapshot.Id} — {snapshot.ProcessName}, {snapshot.CapturedUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}");
        Console.WriteLine($"Заголовок: \"{TextFormat.SingleLine(snapshot.Title, 90)}\"");
        Console.WriteLine($"Элементов в снимке: {snapshot.ElementCount}, показано: {elements.Count}");
        Console.WriteLine();

        if (elements.Count == 0)
        {
            Console.WriteLine("  (элементов нет — содержимое было недоступно через UI Automation)");
            return;
        }

        var minDepth = elements.Min(element => element.Depth);
        foreach (var element in elements)
        {
            var indent = new string(' ', Math.Min(element.Depth - minDepth, 12) * 2);
            Console.WriteLine("  " + indent + FormatElement(element));
        }

        if (elements.Count >= limit)
        {
            Console.WriteLine($"  ... достигнут лимит вывода ({limit}), увеличьте через --limit");
        }
    }

    public static void PrintSearch(string query, IReadOnlyList<ElementSearchHit> hits)
    {
        Console.WriteLine($"Поиск \"{query}\": совпадений {hits.Count}");
        foreach (var hit in hits)
        {
            var when = hit.CapturedUtc.ToLocalTime().ToString("MM-dd HH:mm:ss");
            var name = string.IsNullOrEmpty(hit.Name) ? "(без имени)" : TextFormat.SingleLine(hit.Name, 70);
            var value = string.IsNullOrEmpty(hit.Value) ? string.Empty : $" value={TextFormat.Quote(hit.Value, 70)}";
            Console.WriteLine($"  #{hit.SnapshotId,-6} {when}  {hit.ProcessName,-16} [{hit.ControlType}] {name}{value}");
        }
    }

    public static void PrintStatistics(string databasePath, StoreStatistics statistics)
    {
        Console.WriteLine($"База данных: {databasePath}");
        Console.WriteLine($"Размер файла: {TextFormat.FormatSize(statistics.DatabaseSizeBytes)}");
        Console.WriteLine($"Окон: {statistics.Windows}, снимков: {statistics.Snapshots}, элементов: {statistics.Elements}");

        if (statistics.FirstSnapshotUtc != null && statistics.LastSnapshotUtc != null)
        {
            Console.WriteLine(
                $"Период: {statistics.FirstSnapshotUtc.Value.ToLocalTime():yyyy-MM-dd HH:mm} → {statistics.LastSnapshotUtc.Value.ToLocalTime():yyyy-MM-dd HH:mm}");
            if (statistics.Snapshots > 0)
            {
                Console.WriteLine($"Среднее элементов на снимок: {statistics.Elements / statistics.Snapshots}");
            }
        }

        if (statistics.TopProcesses.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Топ процессов по числу снимков:");
            foreach (var item in statistics.TopProcesses)
            {
                Console.WriteLine($"  {item.ProcessName,-24} {item.SnapshotCount}");
            }
        }
    }

    public static void PrintClean(long deletedSnapshots, long deletedWindows, int keepDays, bool vacuum, string databasePath)
    {
        Console.WriteLine($"Удалено снимков старше {keepDays} дн.: {deletedSnapshots}");
        Console.WriteLine($"Удалено окон без снимков: {deletedWindows}");
        if (vacuum)
        {
            Console.WriteLine("VACUUM выполнен, файл базы сжат.");
        }

        Console.WriteLine($"База данных: {databasePath}");
    }

    public static void PrintExport(int elements, string format, string? outputPath)
    {
        if (outputPath != null)
        {
            Console.WriteLine($"Выгружено элементов: {elements} ({format}) → {outputPath}");
        }
    }

    private static string FormatElement(StoredElement element)
    {
        var name = string.IsNullOrEmpty(element.Name) ? "(без имени)" : TextFormat.Quote(element.Name);
        var value = string.IsNullOrEmpty(element.Value) ? string.Empty : $" value={TextFormat.Quote(element.Value)}";
        var automationId = string.IsNullOrEmpty(element.AutomationId) ? string.Empty : $" #{element.AutomationId}";
        var disabled = element.IsEnabled ? string.Empty : " (disabled)";
        return $"[{element.ControlType}] {name}{value}{automationId}{disabled}";
    }
}
