using System.Diagnostics;

namespace ElyProxy.Services;

/// <summary>Closes recognized standalone torrent clients while the opt-in guard is enabled.</summary>
public sealed class TorrentBlockerService : IDisposable
{
    private static readonly HashSet<string> ClientNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "qbittorrent", "qbittorrent-nox", "utorrent", "bittorrent", "transmission-qt",
        "transmission-daemon", "transmission-gtk", "deluge", "deluged", "deluge-gtk",
        "tixati", "bitcomet", "bitcomet_x64", "biglybt", "vuze", "azureus",
        "picotorrent", "picotorrent-x64", "frostwire", "tribler", "halite",
        "webtorrent", "webtorrent-desktop", "aria2c", "motrix", "mediaget", "mediaget2"
    };
    private readonly object _gate = new();
    private readonly Func<IEnumerable<ITorrentProcess>> _processes;
    private readonly Action<string> _log;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly SemaphoreSlim _scan = new(1, 1);
    private readonly Dictionary<string, DateTime> _lastWarnings = new(StringComparer.OrdinalIgnoreCase);
    private bool _enabled;
    private bool _disposed;

    public event Action<bool>? ScanCompleted;

    public TorrentBlockerService(Action<string> log, Func<IEnumerable<ITorrentProcess>>? processes = null)
    {
        _log = log;
        _processes = processes ?? (() => Process.GetProcesses().Select(p => new TorrentProcess(p)));
        _ = MonitorAsync();
    }

    public static bool IsTorrentClient(string name) => ClientNames.Contains(name);

    public void SetEnabled(bool enabled)
    {
        lock (_gate)
        {
            if (_disposed) return;
            _enabled = enabled;
        }
        if (enabled) _ = ScanAsync();
    }

    /// <returns>False if a recognized client could not be stopped or the scan failed.</returns>
    public Task<bool> ScanAsync() => Task.Run(async () =>
    {
        await _scan.WaitAsync().ConfigureAwait(false);
        bool scanSucceeded = true;
        try
        {
            bool success = true;
            lock (_gate)
            {
                if (!_enabled || _disposed) return true;
            }
            foreach (var process in _processes())
            {
                using (process)
                {
                    string name = "процесс";
                    try
                    {
                        name = process.Name;
                        if (!IsTorrentClient(name)) continue;
                        lock (_gate)
                        {
                            if (!_enabled || _disposed) continue;
                            process.Kill();
                        }
                        if (!process.WaitForExit(2000))
                            throw new InvalidOperationException("Процесс не завершился за 2 секунды");
                        _log($"[sys] Блокировка Torrent: закрыт {name}");
                    }
                    catch (InvalidOperationException) when (HasExitedSafely(process)) { }
                    catch (Exception ex)
                    {
                        // Unrelated protected processes must not make the guard fail.
                        if (!IsTorrentClient(name)) continue;
                        success = false;
                        scanSucceeded = false;
                        Warn(name, $"Не удалось закрыть {name}: {ex.Message}. Закройте клиент вручную или запустите ElyProxy от администратора.");
                    }
                }
            }
            return success;
        }
        catch (Exception ex)
        {
            scanSucceeded = false;
            Warn("scan", "Не удалось проверить торрент-клиенты: " + ex.Message);
            return false;
        }
        finally
        {
            try { ScanCompleted?.Invoke(scanSucceeded); }
            finally { _scan.Release(); }
        }
    });

    private void Warn(string key, string message)
    {
        if (_lastWarnings.TryGetValue(key, out var last) && DateTime.UtcNow - last < TimeSpan.FromMinutes(1)) return;
        _lastWarnings[key] = DateTime.UtcNow;
        _log("[err] Блокировка Torrent: " + message);
    }

    private static bool HasExitedSafely(ITorrentProcess process)
    {
        try { return process.HasExited; }
        catch { return false; }
    }

    private async Task MonitorAsync()
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            while (await timer.WaitForNextTickAsync(_shutdown.Token).ConfigureAwait(false))
                await ScanAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        finally { _shutdown.Dispose(); }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _enabled = false;
            _shutdown.Cancel();
        }
    }

    public interface ITorrentProcess : IDisposable
    {
        string Name { get; }
        bool HasExited { get; }
        void Kill();
        bool WaitForExit(int milliseconds);
    }

    private sealed class TorrentProcess(Process process) : ITorrentProcess
    {
        public string Name => process.ProcessName;
        public bool HasExited => process.HasExited;
        public void Kill() => process.Kill();
        public bool WaitForExit(int milliseconds) => process.WaitForExit(milliseconds);
        public void Dispose() => process.Dispose();
    }
}
