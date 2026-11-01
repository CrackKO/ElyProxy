using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace ElyProxy.Services;

public static class TunStartupRecovery
{
    public static async Task RecoverAsync(bool useHelper)
    {
        if (useHelper && HelperServiceInstaller.IsServiceRunning())
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            using var pipe = new NamedPipeClientStream(".", "ElyProxyHelper", PipeDirection.InOut, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(2000, timeout.Token).ConfigureAwait(false);
            using var reader = new StreamReader(pipe, Encoding.UTF8);
            using var writer = new StreamWriter(pipe, new UTF8Encoding(false)) { AutoFlush = true };
            string? hello;
            using var helloTimeout = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
            helloTimeout.CancelAfter(TimeSpan.FromSeconds(2));
            try { hello = await reader.ReadLineAsync(helloTimeout.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { throw new IOException("Для восстановления TUN обновите службу-помощник в настройках."); }
            using var greeting = JsonDocument.Parse(hello ?? "{}");
            if (!greeting.RootElement.TryGetProperty("message", out var version) || version.GetString() != "stop-ack-v2")
                throw new IOException("Установлена старая служба-помощник. Обновите её в настройках ElyTun.");
            await writer.WriteLineAsync("{\"op\":\"recover\",\"id\":\"startup-recovery\"}").ConfigureAwait(false);
            var line = await reader.ReadLineAsync(timeout.Token).ConfigureAwait(false);
            using var reply = JsonDocument.Parse(line ?? "{}");
            if (!reply.RootElement.TryGetProperty("type", out var type) || type.GetString() != "recovered")
                throw new IOException("Восстановление прежней TUN-сессии службой не подтверждено.");
        }
        var journal = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ElyProxy", "tun-sessions");
        if (!Directory.Exists(journal) || !Directory.EnumerateFiles(journal, "*.json").Any()) return;
        string helper = HelperServiceInstaller.GetHelperPath();
        if (!File.Exists(helper)) throw new FileNotFoundException("Не найден компонент восстановления TUN.");
        using var p = Process.Start(new ProcessStartInfo(helper, "recover-tun")
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true })!;
        var output = p.StandardOutput.ReadToEndAsync(); var error = p.StandardError.ReadToEndAsync();
        try { await p.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false); }
        catch { if (!p.HasExited) p.Kill(true); throw; }
        if (p.ExitCode != 0) throw new IOException("Не удалось восстановить локальную TUN-сессию: " + await error.ConfigureAwait(false));
        await output.ConfigureAwait(false);
    }
}
