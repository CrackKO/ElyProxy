using ElyProxy.Core;
using ElyProxy.Models;
using ElyProxy.Services;
namespace ElyProxy.ViewModels;
public partial class MainViewModel
{
    private readonly CorePerformanceHistoryService _coreHistory = new();
    private ConnectionAttempt? _engineAttempt;
    private IReadOnlyList<CoreCandidate> _engineCandidates = Array.Empty<CoreCandidate>();
    private bool EngineAuto => ElyTunEnabled ? TunCore == TunCoreKind.Auto : _proxyCoreSelection == ProxyCoreKind.Auto;
    private string EngineMode => ElyTunEnabled ? "TUN:" + TunCore : "Proxy";
    private void CancelEngineAttempt() => _engineAttempt?.Cancel();
    private async Task StartSelectedCoreAsync(VlessNode node)
    {
        CancelEngineAttempt();
        int generation = Volatile.Read(ref _connectionGeneration);
        void EnsureCurrent() {
            if (_isDisposed || _coreLifecycleLock.ShuttingDown || generation != Volatile.Read(ref _connectionGeneration))
                throw new OperationCanceledException();
        }
        var candidates = _engineCandidates;
        if (!EngineAuto || candidates.Count == 0)
            candidates = new[] { new CoreCandidate(_resolvedPlan?.Core ?? ProxyCoreKind.Xray, 0, null) };
        using var attempt = new ConnectionAttempt(CorePerformanceHistoryService.Fingerprint(node, EngineMode, "attempt"), EngineMode, candidates);
        _engineAttempt = attempt;
        AppendLog($"[EngineManager] Attempt {attempt.Id}; fingerprint={attempt.Fingerprint[..12]}; mode={attempt.Mode}");
        bool? validated = null; double validationMs = 0; double startupMs = 0;
        try {
            await attempt.RunAsync(EngineAuto, async (candidate, token) => {
                EnsureCurrent(); token.ThrowIfCancellationRequested();
                _resolvedPlan = new(candidate.Core, $"Ядро: {candidate.Core}" + (EngineAuto ? " (Авто)" : ""));
                OnPropertyChanged(nameof(ResolvedCoreDescription));
                AppendLog($"[EngineManager] Selected {candidate.Core}; score={candidate.Score}; {string.Join("; ", candidate.Reasons)}");
                await ValidatePreparedConnectionAsync(node);
                EnsureCurrent(); token.ThrowIfCancellationRequested();
                var startupWatch = System.Diagnostics.Stopwatch.StartNew();
                await CoreStartupOperation.RunAsync(() => StartSelectedCoreOnceAsync(node, token),
                    () => System.Windows.Application.Current.Dispatcher.InvokeAsync(StopAllCoresAsync).Task.Unwrap(), token, TimeSpan.FromSeconds(90));
                startupMs = startupWatch.Elapsed.TotalMilliseconds;
                EnsureCurrent(); token.ThrowIfCancellationRequested();
                AppendLog("[EngineManager] CoreStarted; local endpoint ready");
                var validationWatch = System.Diagnostics.Stopwatch.StartNew();
                using var validation = CancellationTokenSource.CreateLinkedTokenSource(token);
                validation.CancelAfter(TimeSpan.FromSeconds(12));
                validated = null;
                try { await CheckPreparedPathAsync(validation.Token); validated = true; }
                catch (Exception ex) when (ex is IOException or HttpRequestException or OperationCanceledException) {
                    token.ThrowIfCancellationRequested(); EnsureCurrent();
                    AppendLog("[EngineManager] NetworkValidation inconclusive: external reachability not confirmed");
                }
                validationMs = validationWatch.Elapsed.TotalMilliseconds;
                token.ThrowIfCancellationRequested(); EnsureCurrent();
                if (validated == true) AppendLog("[EngineManager] ConnectionValidated");
            }, StopAllCoresAsync, (candidate, success, elapsed, category) => {
                _coreHistory.Record(CorePerformanceHistoryService.Fingerprint(node, attempt.Mode, candidate.Core.ToString()), success, success ? startupMs : elapsed, category.ToString(), validated: validated, validationMs: validationMs);
                AppendLog($"[EngineManager] {candidate.Core}: {(success ? "started" : category.ToString())}; startup={elapsed:F0}ms");
            }, EnsureCurrent);
        }
        finally { if (ReferenceEquals(_engineAttempt, attempt)) _engineAttempt = null; }
    }
}


