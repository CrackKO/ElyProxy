using ElyProxy.Models;
using System.Diagnostics;
using System.Net;

namespace ElyProxy.Core;

/// <summary>
/// Runs sing-box only for the full-system TUN mode. The regular SOCKS mode
/// continues to use Xray, preserving the existing client behaviour.
/// </summary>
public sealed class SingBoxTunManager : IDisposable
{
    private static readonly TimeSpan StartupProbeDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ConfigCheckTimeout = TimeSpan.FromSeconds(8);
    private readonly ProcessManager _processManager = new();
    private readonly SingBoxConfigBuilder _configBuilder = new();
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private readonly object _startCancellationLock = new();
    private string? _configPath;
    private TaskCompletionSource<bool>? _startCompletion;
    private CancellationTokenSource? _startCancellation;
    private bool _stopping;

    public bool IsRunning => _processManager.IsRunning;

    public event Action<string>? LogReceived;
    public event Action<bool>? StatusChanged;

    public SingBoxTunManager()
    {
        _processManager.OutputReceived += OnProcessLine;
        _processManager.ErrorReceived += OnProcessLine;
        _processManager.ProcessExited += OnProcessExited;
    }

    public string GetSingBoxPath()
    {
        var appDir = AppDomain.CurrentDomain.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(appDir, "sing-box", "sing-box.exe"),
            Path.Combine(appDir, "bin", "sing-box", "sing-box.exe"),
            Path.Combine(appDir, "sing-box.exe")
        };

        return candidates.FirstOrDefault(File.Exists) ?? candidates[0];
    }

    public async Task StartAsync(VlessNode node, int socksPort, XrayTunOptions tunOptions)
    {
        await _lifecycleLock.WaitAsync();
        try
        {
            var singBoxPath = GetSingBoxPath();
            if (!File.Exists(singBoxPath))
            {
                throw new FileNotFoundException(
                    $"sing-box не найден по пути: {singBoxPath}\n" +
                    "Добавьте sing-box.exe в папку sing-box рядом с ElyProxy.exe.",
                    singBoxPath);
            }

            if (tunOptions.RoutingPreset == RoutingPreset.RoscomVpnDefault)
            {
                var missingAssets = RoscomVpnDefaultRouting.GetMissingAssetPaths();
                if (missingAssets.Count > 0)
                {
                    throw new FileNotFoundException(
                        "Встроенный профиль RoscomVPN DEFAULT установлен не полностью. " +
                        "Переустановите сборку; отсутствует: " + string.Join(", ", missingAssets));
                }

                LogReceived?.Invoke("[sys] ElyTun routing: RoscomVPN DEFAULT (локальные rule-set'ы)");
            }

            await StopProcessAsync();
            CleanupConfig();

            var effectiveTunOptions = await AddServerRouteExclusionsAsync(node, tunOptions);
            var configJson = _configBuilder.BuildTun(node, socksPort, effectiveTunOptions);
            await WriteConfigAsync(configJson);

            using var startCancellation = new CancellationTokenSource();
            SetStartCancellation(startCancellation);

            try
            {
                LogReceived?.Invoke($"[sys] ElyTun: проверка конфигурации sing-box ({ConfigCheckTimeout.TotalSeconds:0} c)");
                await ValidateConfigurationAsync(singBoxPath, _configPath!, startCancellation.Token);
                PrepareStartWait();

                LogReceived?.Invoke($"[sys] ElyTun core: sing-box ({singBoxPath})");
                LogReceived?.Invoke($"[sys] ElyTun: auto route, {effectiveTunOptions.InterfaceName}, MTU {effectiveTunOptions.Mtu}");
                LogReceived?.Invoke(effectiveTunOptions.RoutingPreset == RoutingPreset.RoscomVpnDefault
                    ? "[sys] ElyTun DNS: RoscomVPN DEFAULT (Yandex direct, Google DoH через прокси)"
                    : $"[sys] ElyTun DNS: bypass → {effectiveTunOptions.DnsServers.FirstOrDefault() ?? "авто"}, остальное → DoH через прокси");

                var workingDirectory = Path.GetDirectoryName(singBoxPath)!;
                await _processManager.StartAsync(singBoxPath, $"run -c \"{_configPath}\"", workingDirectory, stopExisting: false);
                await EnsureStartedAsync(startCancellation.Token);
                StatusChanged?.Invoke(true);
            }
            catch
            {
                // A failed TUN startup may already have installed routes. Always
                // terminate the core before returning the error to the UI.
                await StopProcessAsync();
                CleanupConfig();
                StatusChanged?.Invoke(false);
                throw;
            }
            finally
            {
                ClearStartCancellation(startCancellation);
            }
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public async Task StopAsync()
    {
        CancelPendingStart();
        await _lifecycleLock.WaitAsync();
        try
        {
            await StopProcessAsync();
            CleanupConfig();
            StatusChanged?.Invoke(false);
            LogReceived?.Invoke("[sys] sing-box ElyTun остановлен");
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    private async Task WriteConfigAsync(string configJson)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ElyProxy");
        Directory.CreateDirectory(tempDir);
        _configPath = Path.Combine(tempDir, $"singbox_{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(_configPath, configJson);
    }

    /// <summary>
    /// Resolve the VLESS endpoint before the default route is changed and add
    /// its addresses to route exclusions. This is the same anti-loop pattern
    /// used by standalone TUN clients: the transport to the proxy server must
    /// stay on the physical interface, while user traffic enters the TUN.
    /// </summary>
    private async Task<XrayTunOptions> AddServerRouteExclusionsAsync(VlessNode node, XrayTunOptions options)
    {
        var addresses = new List<IPAddress>();
        if (IPAddress.TryParse(node.Address, out var literal))
        {
            addresses.Add(literal);
        }
        else if (!string.IsNullOrWhiteSpace(node.Address))
        {
            try
            {
                using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                var resolved = await Dns.GetHostAddressesAsync(node.Address, cancellation.Token);
                addresses.AddRange(resolved);
            }
            catch (OperationCanceledException)
            {
                LogReceived?.Invoke($"[warn] Не удалось вовремя определить IP VLESS-сервера: {node.Address}. Запуск продолжается.");
            }
            catch (Exception ex)
            {
                LogReceived?.Invoke($"[warn] Не удалось определить IP VLESS-сервера: {node.Address} ({ex.GetType().Name}). Запуск продолжается.");
            }
        }

        var endpointCidrs = addresses
            .Where(address => !IPAddress.IsLoopback(address))
            .Select(address => $"{address}/{(address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 32 : 128)}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (endpointCidrs.Length == 0)
            return options;

        var exclusions = options.DirectIpCidrs
            .Concat(endpointCidrs)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        LogReceived?.Invoke($"[sys] ElyTun: маршрут VLESS-сервера оставлен вне TUN: {string.Join(", ", endpointCidrs)}");
        return options with { DirectIpCidrs = exclusions };
    }

    private void PrepareStartWait()
    {
        _startCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private async Task EnsureStartedAsync(CancellationToken cancellationToken)
    {
        var startCompletion = _startCompletion
            ?? throw new InvalidOperationException("Ожидание запуска sing-box не было подготовлено.");

        // With the warning log level, a healthy sing-box no longer emits the
        // informational "sing-box started" line. Give it a short, cancellable
        // grace period while still surfacing FATAL/ERROR lines immediately.
        var completed = await Task.WhenAny(startCompletion.Task, Task.Delay(StartupProbeDelay, cancellationToken));
        if (completed == startCompletion.Task)
        {
            await startCompletion.Task;
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (!_processManager.IsRunning)
            throw new InvalidOperationException("sing-box завершился сразу после запуска. Проверьте лог выше.");

        _startCompletion = null;
    }

    private void OnProcessLine(string line)
    {
        LogReceived?.Invoke($"[sing-box] {line}");

        if (line.Contains("sing-box started", StringComparison.OrdinalIgnoreCase))
        {
            _startCompletion?.TrySetResult(true);
        }
        else if (line.Contains("FATAL", StringComparison.OrdinalIgnoreCase)
            || line.Contains("ERROR", StringComparison.OrdinalIgnoreCase)
            || line.Contains("panic:", StringComparison.OrdinalIgnoreCase))
        {
            _startCompletion?.TrySetException(new InvalidOperationException(line));
        }
    }

    private void OnProcessExited(int code)
    {
        if (_stopping)
            return;

        _startCompletion?.TrySetException(new InvalidOperationException($"sing-box завершился при запуске, код: {code}"));
        LogReceived?.Invoke($"[sys] sing-box завершён, код: {code}");
        CleanupConfig();
        StatusChanged?.Invoke(false);
    }

    private async Task StopProcessAsync()
    {
        _stopping = true;
        try
        {
            await _processManager.StopAsync();
        }
        finally
        {
            _stopping = false;
        }
    }

    private async Task ValidateConfigurationAsync(string executablePath, string configPath, CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                WorkingDirectory = Path.GetDirectoryName(executablePath) ?? string.Empty,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding = System.Text.Encoding.UTF8
            }
        };
        process.StartInfo.ArgumentList.Add("check");
        process.StartInfo.ArgumentList.Add("-c");
        process.StartInfo.ArgumentList.Add(configPath);

        if (!process.Start())
            throw new InvalidOperationException("Не удалось запустить проверку конфигурации sing-box.");

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ConfigCheckTimeout);

        try
        {
            await process.WaitForExitAsync(timeout.Token);
            var output = await outputTask;
            var error = await errorTask;
            if (process.ExitCode == 0)
                return;

            var details = string.Join(Environment.NewLine, new[] { output, error }
                .Where(value => !string.IsNullOrWhiteSpace(value))).Trim();
            throw new InvalidOperationException(
                $"sing-box отклонил конфигурацию TUN (код {process.ExitCode})" +
                (string.IsNullOrWhiteSpace(details) ? "." : $": {details}"));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Проверка конфигурации sing-box не завершилась за {ConfigCheckTimeout.TotalSeconds:0} секунд.");
        }
        finally
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch
            {
                // The validation process is best-effort cleanup only.
            }
        }
    }

    private void SetStartCancellation(CancellationTokenSource cancellation)
    {
        lock (_startCancellationLock)
            _startCancellation = cancellation;
    }

    private void ClearStartCancellation(CancellationTokenSource cancellation)
    {
        lock (_startCancellationLock)
        {
            if (ReferenceEquals(_startCancellation, cancellation))
                _startCancellation = null;
        }
    }

    private void CancelPendingStart()
    {
        lock (_startCancellationLock)
            _startCancellation?.Cancel();
    }

    private void CleanupConfig()
    {
        if (_configPath == null)
            return;

        try
        {
            if (File.Exists(_configPath))
                File.Delete(_configPath);
        }
        catch
        {
            // Temporary config cleanup must not hide a connection error.
        }

        _configPath = null;
    }

    public void Dispose()
    {
        CancelPendingStart();
        StopProcessAsync().GetAwaiter().GetResult();
        CleanupConfig();
        _processManager.Dispose();
        GC.SuppressFinalize(this);
    }
}
