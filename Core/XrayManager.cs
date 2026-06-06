using ElyProxy.Models;

namespace ElyProxy.Core;

public class XrayManager : IDisposable
{
    private readonly ProcessManager _processManager;
    private readonly ConfigBuilder _configBuilder;
    private string? _configPath;
    private TaskCompletionSource<bool>? _startCompletion;

    public bool IsRunning => _processManager.IsRunning;

    public event Action<string>? LogReceived;
    public event Action<bool>? StatusChanged;

    public XrayManager()
    {
        _processManager = new ProcessManager();
        _configBuilder = new ConfigBuilder();

        _processManager.OutputReceived += OnProcessLine;
        _processManager.ErrorReceived += OnProcessLine;
        _processManager.ProcessExited += OnProcessExited;
    }

    public string GetXrayPath()
    {
        var appDir = AppDomain.CurrentDomain.BaseDirectory;

        var candidates = new[]
        {
            Path.Combine(appDir, "xray", "xray.exe"),
            Path.Combine(appDir, "bin", "xray", "xray.exe"),
            Path.Combine(appDir, "xray.exe"),
        };

        return candidates.FirstOrDefault(File.Exists) ?? candidates[0];
    }

    public async Task StartAsync(VlessNode node, int socksPort = 1080, XrayTunOptions? tunOptions = null)
    {
        var xrayPath = GetXrayPath();
        if (!File.Exists(xrayPath))
        {
            throw new FileNotFoundException(
                $"Xray не найден по пути: {xrayPath}\n" +
                "Скачайте xray-core: github.com/XTLS/Xray-core/releases",
                xrayPath);
        }

        await StopBeforeStartAsync();

        if (tunOptions != null)
            await Task.Delay(TimeSpan.FromSeconds(1));

        var configJson = _configBuilder.Build(node, socksPort, tunOptions);
        await WriteConfigAsync(configJson);
        PrepareStartWait();

        LogReceived?.Invoke($"[sys] Конфиг: {_configPath}");
        LogReceived?.Invoke($"[sys] Запуск Xray: {xrayPath}");
        LogReceived?.Invoke($"[sys] Сервер: {node.DisplayName} ({node.Address}:{node.Port})");
        LogReceived?.Invoke($"[sys] SOCKS5: 127.0.0.1:{socksPort}");
        if (tunOptions != null)
        {
            LogReceived?.Invoke($"[sys] ElyTun: {tunOptions.InterfaceName}, MTU {tunOptions.Mtu}");
            LogReceived?.Invoke($"[sys] ElyTun DNS: {string.Join(", ", tunOptions.DnsServers)}");
        }

        var xrayDir = Path.GetDirectoryName(xrayPath)!;
        await _processManager.StartAsync(xrayPath, $"run -config \"{_configPath}\"", xrayDir, stopExisting: false);
        await EnsureStartedAsync(tunOptions != null ? TimeSpan.FromSeconds(25) : TimeSpan.FromSeconds(5));
        StatusChanged?.Invoke(true);
    }

    public async Task StartSocksAsync(SocksNode node, int socksPort = 1080)
    {
        var xrayPath = GetXrayPath();
        if (!File.Exists(xrayPath))
            throw new FileNotFoundException("Xray не найден", xrayPath);

        await StopBeforeStartAsync();

        var configJson = _configBuilder.BuildSocks(node, socksPort);
        await WriteConfigAsync(configJson);
        PrepareStartWait();

        LogReceived?.Invoke($"[sys] SOCKS outbound: {node.Address}:{node.Port}");

        var xrayDir = Path.GetDirectoryName(xrayPath)!;
        await _processManager.StartAsync(xrayPath, $"run -config \"{_configPath}\"", xrayDir, stopExisting: false);
        await EnsureStartedAsync(TimeSpan.FromSeconds(5));
        StatusChanged?.Invoke(true);
    }

    public async Task StopAsync()
    {
        await _processManager.StopAsync();
        CleanupConfig();
        StatusChanged?.Invoke(false);
        LogReceived?.Invoke("[sys] Xray остановлен");
    }

    public async Task RestartAsync(VlessNode node, int socksPort = 1080, XrayTunOptions? tunOptions = null)
    {
        await StopAsync();
        if (tunOptions != null)
            await Task.Delay(TimeSpan.FromSeconds(2));

        await StartAsync(node, socksPort, tunOptions);
    }

    private async Task StopBeforeStartAsync()
    {
        await _processManager.StopAsync();
        CleanupConfig();
    }

    private async Task WriteConfigAsync(string configJson)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ElyProxy");
        Directory.CreateDirectory(tempDir);
        _configPath = Path.Combine(tempDir, $"config_{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(_configPath, configJson);
    }

    private void PrepareStartWait()
    {
        _startCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private async Task EnsureStartedAsync(TimeSpan timeout)
    {
        var startCompletion = _startCompletion
            ?? throw new InvalidOperationException("Ожидание запуска Xray не было подготовлено.");

        var completed = await Task.WhenAny(startCompletion.Task, Task.Delay(timeout));
        if (completed != startCompletion.Task)
        {
            if (!_processManager.IsRunning)
                throw new InvalidOperationException("Xray завершился сразу после запуска. Проверьте лог выше.");

            throw new TimeoutException("Xray не подтвердил запуск вовремя. Проверьте лог выше.");
        }

        await startCompletion.Task;
    }

    private void OnProcessLine(string line)
    {
        LogReceived?.Invoke($"[xray] {line}");

        if (line.Contains("core:", StringComparison.OrdinalIgnoreCase)
            && line.Contains("started", StringComparison.OrdinalIgnoreCase))
        {
            _startCompletion?.TrySetResult(true);
        }
        else if (line.Contains("Failed to start:", StringComparison.OrdinalIgnoreCase))
        {
            _startCompletion?.TrySetException(new InvalidOperationException(line));
        }
    }

    private void OnProcessExited(int code)
    {
        _startCompletion?.TrySetException(new InvalidOperationException($"Xray завершился при запуске, код: {code}"));
        LogReceived?.Invoke($"[sys] Xray завершён, код: {code}");
        CleanupConfig();
        StatusChanged?.Invoke(false);
    }

    private void CleanupConfig()
    {
        if (_configPath == null) return;

        try
        {
            if (File.Exists(_configPath))
                File.Delete(_configPath);
        }
        catch { }

        _configPath = null;
    }

    public void Dispose()
    {
        _processManager.StopAsync().GetAwaiter().GetResult();
        CleanupConfig();
        _processManager.Dispose();
        GC.SuppressFinalize(this);
    }
}
