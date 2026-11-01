namespace ElyProxy.Services;

public enum ConnectionState { Disconnected, Connecting, Connected, Switching, Disconnecting, Recovering, CleanupFailed }

/// <summary>Serializes network ownership transitions; invalidation also cancels queued work.</summary>
public sealed class ConnectionCoordinator
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _generation;
    public int Generation => Volatile.Read(ref _generation);
    private ConnectionState _state;
    public ConnectionState State
    {
        get => _state;
        set { LifecycleLog.Write("connection.state", $"{_state} -> {value} generation={Generation}"); _state = value; }
    }
    public bool ShuttingDown { get; private set; }
    public int Invalidate()
    {
        int generation = Interlocked.Increment(ref _generation);
        LifecycleLog.Write("connection.invalidate", $"generation={generation} shutdown={ShuttingDown}");
        return generation;
    }
    public bool IsCurrent(int generation) => !ShuttingDown && Generation == generation;
    public Task WaitAsync() => _gate.WaitAsync();
    public void Release() => _gate.Release();
    public void ResumeAfterFailedHandoff() { ShuttingDown = false; Invalidate(); }
    public void BeginShutdown() { ShuttingDown = true; Invalidate(); }
}
