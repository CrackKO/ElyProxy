namespace ElyProxy.Core;
public static class ProcessManagerCancellation
{
    public static Task StartCancellableAsync(this IProcessManager manager, string executablePath,
        string arguments, string? workingDirectory = null, bool stopExisting = true, CancellationToken cancellationToken = default)
        => CoreStartupOperation.RunAsync(() => manager.StartAsync(executablePath, arguments, workingDirectory, stopExisting),
            manager.StopAsync, cancellationToken, TimeSpan.FromSeconds(60));
}
