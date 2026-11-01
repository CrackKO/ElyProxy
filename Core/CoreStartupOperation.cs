namespace ElyProxy.Core;
/// <summary>Never releases ownership while a non-cancellable startup can still create a process.</summary>
public static class CoreStartupOperation
{
    public static async Task RunAsync(Func<Task> start, Func<Task> stop, CancellationToken token, TimeSpan timeout)
    {
        token.ThrowIfCancellationRequested();
        Task pending = start();
        try { await pending.WaitAsync(timeout, token).ConfigureAwait(false); }
        catch {
            // Drain before stop: otherwise late helper/local startup could outlive cleanup.
            try { await pending.ConfigureAwait(false); } catch { }
            await stop().ConfigureAwait(false);
            throw;
        }
    }
}
