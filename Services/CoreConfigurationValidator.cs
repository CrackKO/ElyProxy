using System.Diagnostics;

namespace ElyProxy.Services;

public static class CoreConfigurationValidator
{
    public static async Task ValidateAsync(string executable, string json, bool singBox)
    {
        string path = Path.Combine(Path.GetTempPath(), "elyproxy-validate-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            await File.WriteAllTextAsync(path, json).ConfigureAwait(false);
            var start = new ProcessStartInfo(executable)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                WorkingDirectory = Path.GetDirectoryName(executable)!
            };
            foreach (var argument in singBox ? new[] { "check", "-c", path } : new[] { "run", "-test", "-config", path })
                start.ArgumentList.Add(argument);
            using var process = Process.Start(start) ?? throw new IOException("Не удалось проверить конфигурацию");
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false); }
            catch { if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync().ConfigureAwait(false); } throw; }
            string details = await error.ConfigureAwait(false) + await output.ConfigureAwait(false);
            if (process.ExitCode != 0) throw new InvalidDataException("Ядро отклонило конфигурацию: " + LifecycleLog.Redact(details));
        }
        finally { File.Delete(path); }
    }
}
