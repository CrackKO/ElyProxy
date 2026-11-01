using System.Collections.Concurrent;
using System.Windows.Input;
using ElyProxy.Core;
using ElyProxy.Models;
using ElyProxy.Services;

namespace ElyProxy.ViewModels;

public partial class MainViewModel
{
    private CancellationTokenSource? _serverProbeCancellation;
    private TaskCompletionSource? _serverProbeFinished;
    private string _serverProbeReport = "";
    public bool IsServerProbing => _serverProbeCancellation != null;
    public string ServerProbeReport => _serverProbeReport;
    public ICommand CancelServerProbeCommand => new RelayCommand(CancelServerProbe, () => IsServerProbing);
    public ICommand CopyServerProbeReportCommand => new RelayCommand(() => System.Windows.Clipboard.SetText(_serverProbeReport), () => _serverProbeReport.Length > 0);
    private void CancelServerProbe()
    {
        if (_serverProbeCancellation == null) return;
        StatusText = "Остановка проверки: ожидание завершения пробных ядер…";
        _serverProbeCancellation.Cancel();
    }

    private ProbeContext CreateProbeContext(VlessNode node)
    {
        if (UsesHysteriaCore(node))
            throw new NotSupportedException("Отдельный Hysteria пока не проверяется: результат другого ядра был бы неточным");
        var plan = CoreSelectionPolicy.ResolveProbe(node, _proxyCoreSelection, DpiFor(node),
            File.Exists(_singBoxProxyManager.GetSingBoxPath()), File.Exists(_xrayManager.GetXrayPath()));
        var options = DpiFor(node).Apply(new XrayTunOptions("ElyProbe", 1500,
            GetElyTunDnsServers(), ElyTunIpv6Enabled, Array.Empty<string>(), Array.Empty<string>(),
            RoutingPreset.Custom, Array.Empty<string>(), ExitProxy: ExitProxyEnabled ? _exitProxy : null),
            plan.Core == ProxyCoreKind.SingBox);
        if (ExitProxyEnabled)
        {
            if (_exitProxy == null) throw new InvalidOperationException("Не настроен выходной прокси");
            ProxyChainPolicy.Validate(node, _exitProxy);
        }
        DpiSettingsValidator.Validate(options, node, plan.Core == ProxyCoreKind.SingBox);
        if (options.TlsSpoofEnabled && !ElyTunElevationService.IsRunningAsAdministrator())
            throw new NotSupportedException("Проба spoofing требует повышенного клиента; helper и текущее подключение не затрагиваются");
        var copy = Newtonsoft.Json.JsonConvert.DeserializeObject<VlessNode>(Newtonsoft.Json.JsonConvert.SerializeObject(node))!;
        string hash = ConfigHash.Compute(Newtonsoft.Json.JsonConvert.SerializeObject(new { Node = copy, Options = options, Core = plan.Core }));
        return new(copy, options, plan.Core, $"конфигурация {hash} · {plan.Core} · DPI сервера · цепочка {(options.ExitProxy == null ? "выкл" : "вкл")} · локальная HTTPS-проба");
    }

    private async Task RunServerProbesAsync(bool hard)
    {
        if (IsServerProbing) return;
        using var cancellation = new CancellationTokenSource();
        _serverProbeCancellation = cancellation;
        _serverProbeFinished = new(TaskCreationOptions.RunContinuationsAsynchronously);
        IsLoading = true;
        OnPropertyChanged(nameof(IsServerProbing));
        CommandManager.InvalidateRequerySuggested();
        string name = hard ? "ElyHard" : "ElyTurbo";
        var results = new ConcurrentBag<ServerProbeResult>();
        var nodes = AllNodes.ToArray();
        foreach (var node in nodes)
        {
            node.Latency = null; node.PingDetails = "Не проверен";
            node.ProbeVerdict = ProbeVerdict.Inconclusive.ToString();
            node.ProbeRating = null; node.ProbeSpeedMbps = null;
            node.ProbeReport = "Текущая проверка ещё не завершена для этого сервера";
        }
        string? batchError = null;
        // Freeze every node's settings before awaiting: editing UI never changes an in-flight probe.
        var prepared = nodes.Select(node =>
        {
            try { return (Node: node, Context: CreateProbeContext(node), Error: (string?)null); }
            catch (Exception ex) { DiagnosticHttpHandler.LogException("probe.prepare", ex); return (Node: node, Context: (ProbeContext?)null, Error: ex.Message); }
        }).ToArray();
        int completed = 0;
        int concurrency = ProbeExecutionPolicy.Current(ProxyPingConcurrency);
        using var transferGate = new SemaphoreSlim(1, 1);
        StatusText = $"{name}: {nodes.Length} серверов · параллельно {concurrency}";
        PingSummary = hard ? "Два запуска ядра, 3 раунда HTTPS, до 2 МиБ загрузки на сервер; UDP/TUN не проверяются" : "Быстрый отбор: HTTPS через выбранное ядро и DPI";
        try
        {
            await Parallel.ForEachAsync(prepared, new ParallelOptions
            {
                MaxDegreeOfParallelism = concurrency,
                CancellationToken = cancellation.Token
            }, async (item, token) =>
            {
                var result = item.Context == null
                    ? new ServerProbeResult(item.Node.LibraryId, hard, "Настройки не поддерживаются", Array.Empty<ProbeSample>(), 0, item.Error)
                    : await _elyHardService.CheckNodeAsync(item.Context, hard, token, transferGate);
                LifecycleLog.Write("probe.result", $"node={item.Node.LibraryId} hard={hard} context={result.Context} starts={result.Starts} failure={result.Failure}");
                foreach (var sample in result.Samples)
                    LifecycleLog.Write("probe.sample", $"node={item.Node.LibraryId} target={sample.Name} success={sample.Success} ms={sample.Milliseconds} bytes={sample.Bytes} detail={sample.Detail}");
                results.Add(result);
                int count = Interlocked.Increment(ref completed);
                await _dispatcher.InvokeAsync(() =>
                {
                    item.Node.Latency = result.MedianMs;
                    item.Node.ProbeRating = result.Rating;
                    item.Node.ProbeSpeedMbps = result.MinimumMbps;
                    item.Node.PingDetails = result.Rating.Title;
                    item.Node.ProbeReport = result.Report;
                    item.Node.ProbeVerdict = result.Verdict.ToString();
                    StatusText = $"{name}: {count}/{nodes.Length} · {concurrency} серверов параллельно · {result.Rating.Title}";
                    if (count % 4 == 0 || count == nodes.Length) OnPropertyChanged("ServerLibrary");
                });
            });
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        { StatusText = $"{name}: остановлено, {completed}/{nodes.Length}"; }
        catch (Exception ex)
        { batchError = LifecycleLog.Redact(ex.GetBaseException().Message); StatusText = $"{name}: ошибка проверки"; AppendLog("[err] " + batchError); }
        finally
        {
            var ordered = results.OrderByDescending(r => r.Rating.Score ?? -1)
                .ThenByDescending(r => r.MinimumMbps ?? 0).ThenBy(r => r.P95Ms ?? int.MaxValue).ToArray();
            var selected = SelectedNode;
            // Keep untested rows after cancellation and preserve the user's selection/active session.
            var byId = nodes.GroupBy(n => n.LibraryId).ToDictionary(g => g.Key, g => g.First());
            AllNodes.Clear();
            foreach (var row in ordered.Select(r => byId[r.NodeId]).Concat(nodes.Where(n => !ordered.Any(r => r.NodeId == n.LibraryId))).Distinct()) AllNodes.Add(row);
            SelectedNode = selected;
            _serverProbeReport = $"{name} · {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}\n" +
                $"Условия: текущая сеть Windows; системные настройки и активное соединение не менялись. Параллельных серверов: {concurrency}; нагрузочные загрузки по одной.\n" +
                (batchError == null ? "" : "Проверка прервана: " + batchError + "\n") +
                (cancellation.IsCancellationRequested ? "Отменено пользователем; непроверенные строки не являются отрицательным результатом.\n" : "") + "\n" +
                string.Join("\n\n", ordered.Select(r => byId[r.NodeId].DisplayName + "\n" + r.Report));
            OnPropertyChanged(nameof(ServerProbeReport));
            if (ordered.Length > 0) PingSummary = $"{name}: {ordered[0].Rating.Title} · {ordered[0].Rating.Metrics}";
            else PingSummary = $"{name}: результатов нет";
            if (!cancellation.IsCancellationRequested && batchError == null) StatusText = $"{name}: завершено {completed}/{nodes.Length}";
            _serverProbeCancellation = null;
            IsLoading = false;
            OnPropertyChanged(nameof(IsServerProbing));
            CommandManager.InvalidateRequerySuggested();
            _serverProbeFinished?.TrySetResult();
            _serverProbeFinished = null;
        }
    }
}
