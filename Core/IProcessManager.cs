namespace ElyProxy.Core;

public interface IProcessManager : IDisposable
{
    bool IsRunning { get; }

    event Action<string>? OutputReceived;

    event Action<string>? ErrorReceived;

    event Action<int>? ProcessExited;

    event Action<int>? ProcessStartedForProcess;

    event Action<int, string>? OutputReceivedForProcess;

    event Action<int, string>? ErrorReceivedForProcess;

    event Action<int, int>? ProcessExitedForProcess;

    Task StartAsync(string executablePath, string arguments, string? workingDirectory = null, bool stopExisting = true);

    Task StopAsync();
}
