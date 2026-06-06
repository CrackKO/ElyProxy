using System.Diagnostics;
using System.Text;

namespace ElyProxy.Core;

public class ProcessManager : IDisposable
{
    private Process? _process;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public bool IsRunning
    {
        get
        {
            try { return _process is { HasExited: false }; }
            catch { return false; }
        }
    }

    public event Action<string>? OutputReceived;
    public event Action<string>? ErrorReceived;
    public event Action<int>? ProcessExited;

    public async Task StartAsync(string executablePath, string arguments, string? workingDirectory = null, bool stopExisting = true)
    {
        if (stopExisting)
            await StopAsync();

        var psi = new ProcessStartInfo
        {
            FileName = executablePath,
            Arguments = arguments,
            WorkingDirectory = workingDirectory ?? Path.GetDirectoryName(executablePath) ?? "",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        await _lock.WaitAsync();
        try
        {
            _process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            _process.OutputDataReceived += OnOutput;
            _process.ErrorDataReceived += OnError;
            _process.Exited += OnExited;
            _process.Start();
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task StopAsync()
    {
        await _lock.WaitAsync();
        try
        {
            if (_process == null) return;

            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                    await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
                }
            }
            catch (TimeoutException) { }
            catch { }
            finally
            {
                _process.OutputDataReceived -= OnOutput;
                _process.ErrorDataReceived -= OnError;
                _process.Exited -= OnExited;
                _process.Dispose();
                _process = null;
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    private void OnOutput(object sender, DataReceivedEventArgs e)
    {
        if (e.Data != null) OutputReceived?.Invoke(e.Data);
    }

    private void OnError(object sender, DataReceivedEventArgs e)
    {
        if (e.Data != null) ErrorReceived?.Invoke(e.Data);
    }

    private void OnExited(object? sender, EventArgs e)
    {
        int exitCode = -1;
        try { exitCode = _process?.ExitCode ?? -1; } catch { }
        ProcessExited?.Invoke(exitCode);
    }

    public void Dispose()
    {
        StopAsync().GetAwaiter().GetResult();
        _lock.Dispose();
        GC.SuppressFinalize(this);
    }
}
