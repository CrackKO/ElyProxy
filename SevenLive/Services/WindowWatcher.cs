using System.Diagnostics;
using FlaUI.UIA3;
using SevenLive.Cli;
using SevenLive.Data;
using SevenLive.Models;
using SevenLive.Reporting;
using SevenLive.Services.Adapters;

namespace SevenLive.Services;

public sealed class WindowWatcher
{
    private static readonly TimeSpan WindowRefreshInterval = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(60);

    private readonly WindowEnumerator _enumerator = new();
    private readonly AdapterRegistry _adapters = new();
    private readonly UiAutomationReader _reader;
    private readonly SnapshotStore _store;
    private readonly AppOptions _options;
    private readonly Dictionary<WindowKey, WindowState> _windows = new();

    private IntPtr _lastActiveHandle;
    private DateTime _lastHeartbeatUtc = DateTime.UtcNow;

    public WindowWatcher(UIA3Automation automation, SnapshotStore store, AppOptions options)
    {
        _store = store;
        _options = options;
        _reader = new UiAutomationReader(automation, options.MaxDepthValue, options.MaxElementsValue, options.UseRawView);
    }

    public void RunOnce(string? windowSelector)
    {
        ConsoleReporter.PrintBanner(_options, watch: false);
        var windows = _enumerator.GetOpenWindows();
        ConsoleReporter.PrintWindowList(windows);
        Console.WriteLine();

        var bySelector = !string.IsNullOrWhiteSpace(windowSelector);
        var target = bySelector
            ? _enumerator.FindWindow(windowSelector!)
            : _enumerator.GetForegroundWindowInfo();

        if (target == null)
        {
            Console.WriteLine(bySelector
                ? $"Окно по запросу \"{windowSelector}\" не найдено."
                : "Активное окно не найдено.");
            return;
        }

        ConsoleReporter.PrintActiveWindow(target, bySelector ? "Окно по запросу" : "Активное окно");
        Capture(target, force: true);
    }

    public void Run(CancellationToken cancellationToken)
    {
        ConsoleReporter.PrintBanner(_options, watch: true);
        var windows = _enumerator.GetOpenWindows();
        ConsoleReporter.PrintWindowList(windows);
        Console.WriteLine();

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                Poll();
            }
            catch (Exception exception)
            {
                ConsoleReporter.PrintError(exception.Message);
            }

            if (cancellationToken.WaitHandle.WaitOne(TimeSpan.FromSeconds(_options.IntervalSeconds)))
            {
                break;
            }
        }

        ConsoleReporter.PrintStopped();
    }

    private void Poll()
    {
        var now = DateTime.UtcNow;
        var seen = new HashSet<WindowKey>();

        foreach (var window in _enumerator.GetOpenWindows())
        {
            var key = new WindowKey(window.Handle, window.ProcessId);
            seen.Add(key);
            Track(key, window, now);
        }

        var active = _enumerator.GetForegroundWindowInfo();
        if (active != null)
        {
            var key = new WindowKey(active.Handle, active.ProcessId);
            seen.Add(key);
            Track(key, active, now);

            if (active.Handle != _lastActiveHandle)
            {
                _lastActiveHandle = active.Handle;
                ConsoleReporter.PrintActiveWindow(active);
            }

            Capture(active, force: false);
        }

        if (now - _lastHeartbeatUtc >= HeartbeatInterval)
        {
            _lastHeartbeatUtc = now;
            ConsoleReporter.PrintHeartbeat(active, GetElementCount(active), now);
        }

        foreach (var key in _windows.Keys.Where(key => !seen.Contains(key)).ToArray())
        {
            _windows.Remove(key);
        }
    }

    private void Track(WindowKey key, WindowInfo window, DateTime now)
    {
        if (_windows.TryGetValue(key, out var state))
        {
            var changed = state.Title != window.Title || state.IsActive != window.IsActive;
            if (!changed && now - state.LastWriteUtc < WindowRefreshInterval)
            {
                return;
            }

            var id = _store.UpsertWindow(window, now);
            _windows[key] = state with
            {
                Id = id,
                Title = window.Title,
                IsActive = window.IsActive,
                LastWriteUtc = now,
            };
            return;
        }

        var newId = _store.UpsertWindow(window, now);
        _windows[key] = new WindowState(newId, window.Title, window.IsActive, now, null, 0);
    }

    private void Capture(WindowInfo window, bool force)
    {
        var key = new WindowKey(window.Handle, window.ProcessId);
        if (!_windows.TryGetValue(key, out var state))
        {
            var id = _store.UpsertWindow(window, DateTime.UtcNow);
            state = new WindowState(id, window.Title, window.IsActive, DateTime.UtcNow, null, 0);
            _windows[key] = state;
        }

        var stopwatch = Stopwatch.StartNew();
        var elements = _reader.ReadVisibleElements(window.Handle);
        var elapsed = stopwatch.Elapsed;

        var hash = SnapshotHasher.Compute(elements);
        if (!force && hash == state.LastHash)
        {
            return;
        }

        var snapshot = new WindowSnapshot(hash, DateTime.UtcNow, window.IsActive, elements);
        var snapshotId = _store.SaveSnapshot(state.Id, snapshot);
        _windows[key] = state with
        {
            LastHash = hash,
            LastElementCount = elements.Count,
            LastWriteUtc = DateTime.UtcNow,
        };

        ConsoleReporter.PrintSnapshot(window, snapshot, snapshotId, elapsed);
        SaveJournal(window, state.Id, snapshotId, elements);
    }

    private void SaveJournal(WindowInfo window, long windowId, long snapshotId, IReadOnlyList<UiElementInfo> elements)
    {
        var result = _adapters.Extract(window, elements);
        if (result == null)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var applicationName = ResolveApplicationName(window, result.Diagnostics.Adapter);
        var applicationId = _store.GetOrCreateApplication(applicationName, window.ProcessName, now);
        var conversationId = _store.GetOrCreateConversation(applicationId, window, windowId, result.Conversation, now);
        var saved = _store.SaveMessages(conversationId, snapshotId, result.Diagnostics.Adapter, result.Messages, now);
        _store.SaveAdapterRun(result.Diagnostics, window, conversationId, now);

        ConsoleReporter.PrintAdapterResult(window, result, saved.Inserted, saved.Total);
    }

    private static string ResolveApplicationName(WindowInfo window, string adapter)
    {
        return adapter switch
        {
            "telegram" => "Telegram",
            "discord" => "Discord",
            _ => window.ProcessName,
        };
    }

    private int GetElementCount(WindowInfo? window)
    {
        if (window == null)
        {
            return 0;
        }

        return _windows.TryGetValue(new WindowKey(window.Handle, window.ProcessId), out var state)
            ? state.LastElementCount
            : 0;
    }

    private readonly record struct WindowKey(nint Handle, int ProcessId);

    private sealed record WindowState(
        long Id,
        string Title,
        bool IsActive,
        DateTime LastWriteUtc,
        string? LastHash,
        int LastElementCount);
}
