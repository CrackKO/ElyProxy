namespace ElyProxy.Core;
public enum CoreFailureCategory { Cancelled, UserDisconnect, CoreUnavailable, CoreStartupFailure, CoreExited, ConfigRejected, Unsupported, AuthenticationFailure, NetworkFailure, ProxyEndpointFailure, ConnectionValidationFailure, Timeout, Unknown }
public sealed class ConnectionAttempt : IDisposable
{
    private readonly CancellationTokenSource _cancellation = new();
    public Guid Id { get; } = Guid.NewGuid();
    public string Fingerprint { get; }
    public string Mode { get; }
    public DateTimeOffset StartedAt { get; } = DateTimeOffset.UtcNow;
    public IReadOnlyList<CoreCandidate> Candidates { get; }
    public HashSet<ProxyCoreKind> AttemptedCores { get; } = new();
    public ProxyCoreKind? CurrentCore { get; private set; }
    public CancellationToken Token => _cancellation.Token;
    public ConnectionAttempt(string fingerprint, string mode, IReadOnlyList<CoreCandidate> candidates) { Fingerprint = fingerprint; Mode = mode; Candidates = candidates; }
    public void Cancel() => _cancellation.Cancel();
    public async Task RunAsync(bool auto, Func<CoreCandidate, CancellationToken, Task> start,
        Func<Task> cleanup, Action<CoreCandidate, bool, double, CoreFailureCategory> record, Action? ensureCurrent = null)
    {
        Exception? failure = null;
        foreach (var candidate in Candidates.Where(c => c.Compatible)) {
            Token.ThrowIfCancellationRequested(); ensureCurrent?.Invoke();
            if (!AttemptedCores.Add(candidate.Core)) continue;
            CurrentCore = candidate.Core;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            try {
                await start(candidate, Token);
                Token.ThrowIfCancellationRequested(); ensureCurrent?.Invoke();
                record(candidate, true, watch.Elapsed.TotalMilliseconds, CoreFailureCategory.Unknown);
                return;
            }
            catch (Exception ex) {
                await cleanup();
                Token.ThrowIfCancellationRequested(); ensureCurrent?.Invoke();
                if (ex is OperationCanceledException) throw;
                var category = Classify(ex);
                record(candidate, false, watch.Elapsed.TotalMilliseconds, category);
                failure = ex;
                if (!auto || category is CoreFailureCategory.AuthenticationFailure or CoreFailureCategory.Unknown) throw;
            }
        }
        throw failure ?? new NotSupportedException("Нет совместимых ядер");
    }
    public static CoreFailureCategory Classify(Exception ex) => ex switch {
        OperationCanceledException => CoreFailureCategory.Cancelled,
        FileNotFoundException => CoreFailureCategory.CoreUnavailable,
        TimeoutException => CoreFailureCategory.Timeout,
        IOException => CoreFailureCategory.CoreStartupFailure,
        NotSupportedException => CoreFailureCategory.Unsupported,
        System.Net.Http.HttpRequestException or System.Net.Sockets.SocketException => CoreFailureCategory.NetworkFailure,
        InvalidOperationException => CoreFailureCategory.CoreStartupFailure,
        _ => CoreFailureCategory.Unknown };
    public void Dispose() { _cancellation.Cancel(); _cancellation.Dispose(); }
}


