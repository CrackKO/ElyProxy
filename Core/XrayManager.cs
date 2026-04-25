using ElyProxy.Models;

namespace ElyProxy.Core;

public class XrayManager : IDisposable
{
    private readonly ProcessManager _processManager;
    private readonly ConfigBuilder _configBuilder;
    private string? _configPath;

    public bool IsRunning => _processManager.IsRunning;

    public event Action<string>? LogReceived;
    public event Action<bool>? StatusChanged;

    public XrayManager()
    {
        _processManager = new ProcessManager();
        _configBuilder = new ConfigBuilder();

        _processManager.OutputReceived += line => LogReceived?.Invoke($"[xray] {line}");
        _processManager.ErrorReceived += line => LogReceived?.Invoke($"[xray] {line}");
        _processManager.ProcessExited += code =>
        {
            LogReceived?.Invoke($"[sys] Xray завершён, код: {code}");
            CleanupConfig();
            StatusChanged?.Invoke(false);
        };
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

    public async Task StartAsync(VlessNode node, int socksPort = 1080)
    {
        var xrayPath = GetXrayPath();
        if (!File.Exists(xrayPath))
        {
            throw new FileNotFoundException(
                $"Xray не найден по пути: {xrayPath}\n" +
                "Скачайте xray-core: github.com/XTLS/Xray-core/releases",
                xrayPath);
        }

        var configJson = _configBuilder.Build(node, socksPort);
        await WriteConfigAsync(configJson);

        LogReceived?.Invoke($"[sys] Конфиг: {_configPath}");
        LogReceived?.Invoke($"[sys] Запуск Xray: {xrayPath}");
        LogReceived?.Invoke($"[sys] Сервер: {node.DisplayName} ({node.Address}:{node.Port})");
        LogReceived?.Invoke($"[sys] SOCKS5: 127.0.0.1:{socksPort}");

        var xrayDir = Path.GetDirectoryName(xrayPath)!;
        await _processManager.StartAsync(xrayPath, $"run -config \"{_configPath}\"", xrayDir);
        StatusChanged?.Invoke(true);
    }

    public async Task StartSocksAsync(SocksNode node, int socksPort = 1080)
    {
        var xrayPath = GetXrayPath();
        if (!File.Exists(xrayPath))
            throw new FileNotFoundException("Xray не найден", xrayPath);

        var configJson = _configBuilder.BuildSocks(node, socksPort);
        await WriteConfigAsync(configJson);

        LogReceived?.Invoke($"[sys] SOCKS outbound: {node.Address}:{node.Port}");

        var xrayDir = Path.GetDirectoryName(xrayPath)!;
        await _processManager.StartAsync(xrayPath, $"run -config \"{_configPath}\"", xrayDir);
        StatusChanged?.Invoke(true);
    }

    public async Task StopAsync()
    {
        await _processManager.StopAsync();
        CleanupConfig();
        StatusChanged?.Invoke(false);
        LogReceived?.Invoke("[sys] Xray остановлен");
    }

    public async Task RestartAsync(VlessNode node, int socksPort = 1080)
    {
        await StopAsync();
        await StartAsync(node, socksPort);
    }

    private async Task WriteConfigAsync(string configJson)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ElyProxy");
        Directory.CreateDirectory(tempDir);
        _configPath = Path.Combine(tempDir, $"config_{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(_configPath, configJson);
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
