namespace ElyProxy.Services;

// Superseded operations never queue another restart behind the current one.
public sealed class LatestOperation : IDisposable
{
    private int _version;
    public int Request() => Interlocked.Increment(ref _version);
    public bool IsCurrent(int version) => Volatile.Read(ref _version) == version;
    public void Cancel() => Interlocked.Increment(ref _version);
    public void Dispose() => Cancel();
}
