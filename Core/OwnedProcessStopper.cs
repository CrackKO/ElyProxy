using System.Diagnostics;

#if ELYPROXY_HELPER
namespace ElyProxy.Helper.Runtime;
#else
namespace ElyProxy.Core;
#endif

public static class OwnedProcessStopper
{
    public static async Task StopAsync(Process process)
    {
        if (process.HasExited) { CheckHostResult(process); return; }
        // GUI cores may provide a close handler; console cores use the isolated host.
        if (process.StartInfo.RedirectStandardInput)
        {
            try { await process.StandardInput.WriteLineAsync("stop").WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false); }
            catch (IOException) { }
            try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(25)).ConfigureAwait(false); CheckHostResult(process); return; }
            catch (TimeoutException) { }
        }
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(8)).ConfigureAwait(false);
        }
        if (!process.HasExited) throw new IOException($"Ядро PID {process.Id} не остановлено.");
        CheckHostResult(process);
    }
    private static void CheckHostResult(Process process)
    {
        if (process.StartInfo.RedirectStandardInput && process.ExitCode == 90)
            throw new IOException("Ядро завершено, но очистка его сетевой сессии не подтверждена. Проверьте журнал восстановления TUN.");
    }
}
