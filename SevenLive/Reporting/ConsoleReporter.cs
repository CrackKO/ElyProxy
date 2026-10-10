using SevenLive.Cli;
using SevenLive.Models;

namespace SevenLive.Reporting;

public static class ConsoleReporter
{
    private const int MaxTreeLines = 60;

    public static void PrintBanner(AppOptions options, bool watch)
    {
        Console.WriteLine("SevenLive — локальный журнал окон (UI Automation + SQLite)");
        Console.WriteLine($"База данных: {options.DatabasePath}");
        if (watch)
        {
            Console.WriteLine($"Период опроса: {options.IntervalSeconds:0.#} с; остановка — Ctrl+C");
        }

        Console.WriteLine();
    }

    public static void PrintWindowList(IReadOnlyList<WindowInfo> windows)
    {
        Console.WriteLine($"Открытые окна ({windows.Count}):");
        foreach (var window in windows)
        {
            var marker = window.IsActive ? "*" : " ";
            Console.WriteLine(
                $"{marker} {window.ProcessName,-24} PID {window.ProcessId,-7} {window.HandleHex}  \"{TextFormat.SingleLine(window.Title, 90)}\"");
        }
    }

    public static void PrintActiveWindow(WindowInfo window, string label = "Активное окно")
    {
        Console.WriteLine();
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {label}: {window.ProcessName} (PID {window.ProcessId}) — \"{TextFormat.SingleLine(window.Title, 90)}\"");
    }

    public static void PrintSnapshot(WindowInfo window, WindowSnapshot snapshot, long snapshotId, TimeSpan elapsed)
    {
        Console.WriteLine(
            $"[{snapshot.CapturedUtc.ToLocalTime():HH:mm:ss}] Снимок #{snapshotId}: {window.ProcessName} (PID {window.ProcessId}) — элементов: {snapshot.Elements.Count}, чтение: {elapsed.TotalMilliseconds:F0} мс, hash: {TextFormat.ShortHash(snapshot.ContentHash)}");

        if (snapshot.Elements.Count == 0)
        {
            Console.WriteLine("    Содержимое недоступно через UI Automation (окно не отдаёт дерево элементов).");
            return;
        }

        foreach (var element in snapshot.Elements.Take(MaxTreeLines))
        {
            Console.WriteLine("    " + FormatElement(element));
        }

        if (snapshot.Elements.Count > MaxTreeLines)
        {
            Console.WriteLine($"    ... ещё {snapshot.Elements.Count - MaxTreeLines} элементов (полностью сохранены в SQLite)");
        }
    }

    public static void PrintAdapterResult(WindowInfo window, AdapterResult result, int inserted, int total)
    {
        Console.WriteLine(
            $"    [{result.Diagnostics.Adapter}] сообщений: {result.Diagnostics.MessagesFound} (новых: {inserted}, всего: {total}), полезных текстов: {result.Diagnostics.UsefulTextsFound}, метод: {result.Diagnostics.ExtractionMethod}");
    }

    public static void PrintHeartbeat(WindowInfo? window, int elementCount, DateTime utcNow)
    {
        var target = window == null
            ? "нет активного окна"
            : $"{window.ProcessName} (PID {window.ProcessId})";
        Console.WriteLine($"[{utcNow.ToLocalTime():HH:mm:ss}] работаю: {target}, элементов в последнем снимке: {elementCount}");
    }

    public static void PrintError(string message)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Ошибка: {message}");
    }

    public static void PrintStopped()
    {
        Console.WriteLine();
        Console.WriteLine("Остановлено.");
    }

    private static string FormatElement(UiElementInfo element)
    {
        var indent = new string(' ', Math.Min(element.Depth, 10) * 2);
        var name = string.IsNullOrEmpty(element.Name) ? "(без имени)" : TextFormat.Quote(element.Name);
        var value = string.IsNullOrEmpty(element.Value) ? string.Empty : $" value={TextFormat.Quote(element.Value)}";
        var automationId = string.IsNullOrEmpty(element.AutomationId) ? string.Empty : $" #{element.AutomationId}";
        var disabled = element.IsEnabled ? string.Empty : " (disabled)";
        return $"{indent}[{element.ControlType}] {name}{value}{automationId}{disabled}";
    }
}
