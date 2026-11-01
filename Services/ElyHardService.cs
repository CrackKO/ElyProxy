using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using ElyProxy.Core;
using ElyProxy.Models;

namespace ElyProxy.Services;

public enum ProbeVerdict { Failed, Inconclusive, Partial, Unstable, Slow, Verified, Quick }
public sealed record ProbeTarget(string Name, string Url, string Marker = "", int ExactBytes = 0, bool Control = false);
public sealed record ProbeSample(string Name, bool Success, int Milliseconds, long Bytes, string Detail,
    bool Control = false, bool Transfer = false)
{
    // End-to-end throughput includes connection establishment; deliberately conservative.
    public double Mbps => Bytes * 8d / Math.Max(1, Milliseconds) / 1000;
}
public sealed record ProbeContext(VlessNode Node, XrayTunOptions Options, ProxyCoreKind Core, string Description);
public sealed record ServerProbeResult(string NodeId, bool Hard, string Context, IReadOnlyList<ProbeSample> Samples,
    int Starts, string? Failure = null)
{
    public DateTimeOffset CheckedAt { get; init; } = DateTimeOffset.Now;
    public int Successes => Samples.Count(s => s.Success);
    public int Total => Samples.Count;
    private int[] Times => Samples.Where(s => s.Success && !s.Transfer).Select(s => s.Milliseconds).Order().ToArray();
    public int? MedianMs => Times.Length == 0 ? null : Times[Times.Length / 2];
    public int? P95Ms => Times.Length == 0 ? null : Times[(int)Math.Ceiling(Times.Length * .95) - 1];
    public int? SpreadMs => Times.Length < 2 ? null : Times[^1] - Times[0];
    public double? MinimumMbps => Samples.Any(s => s.Transfer && s.Success)
        ? Samples.Where(s => s.Transfer && s.Success).Min(s => s.Mbps) : null;
    public ProbeVerdict Verdict
    {
        get
        {
            if (Failure != null || Total == 0) return ProbeVerdict.Inconclusive;
            if (Successes == 0) return ProbeVerdict.Failed;
            if (!Hard)
            {
                if (Total != 5) return ProbeVerdict.Inconclusive;
                return Successes == Total ? ProbeVerdict.Quick : ProbeVerdict.Partial;
            }
            if (Total != 26 || ElyHardService.Controls.Concat(ElyHardService.Services)
                .Any(t => Samples.Count(s => s.Name == t.Name) != 3)) return ProbeVerdict.Inconclusive;
            var controls = Samples.Where(s => s.Control).ToArray();
            var transfers = Samples.Where(s => s.Transfer).ToArray();
            if (Starts < 2 || controls.Select(s => s.Name).Distinct().Count() < 2 || transfers.Length < 2)
                return ProbeVerdict.Inconclusive;
            if (controls.Any(s => !s.Success) || transfers.Any(s => !s.Success)) return ProbeVerdict.Unstable;
            if (Samples.Any(s => !s.Success)) return ProbeVerdict.Partial;
            if (P95Ms > 2500 || MinimumMbps < 2) return ProbeVerdict.Slow;
            if (SpreadMs > 1500) return ProbeVerdict.Unstable;
            return ProbeVerdict.Verified;
        }
    }
    public string Label => Verdict switch
    {
        ProbeVerdict.Verified => "HTTPS проверен", ProbeVerdict.Quick => "Быстрый тест пройден",
        ProbeVerdict.Partial => "Часть проверок не прошла", ProbeVerdict.Unstable => "Нестабильно",
        ProbeVerdict.Slow => "Медленно", ProbeVerdict.Failed => "HTTPS недоступен", _ => "Не подтверждено"
    };
    public string Compact => $"{Label} · {Successes}/{Total} · медиана {MedianMs?.ToString() ?? "—"} мс" +
        (MinimumMbps is { } speed ? $" · ≥{speed:F1} Мбит/с" : "");
    public ProbeRating Rating => ProbeRatingPolicy.Assess(this);
    public string Report => $"{Rating.Title} · {Rating.Metrics}\n{Rating.Explanation}\n{Compact} · p95 {P95Ms?.ToString() ?? "—"} мс · разброс {SpreadMs?.ToString() ?? "—"} мс\n" +
        $"{Context} · {CheckedAt:HH:mm:ss} · запусков {Starts}\n" +
        (Failure == null ? "" : Failure + "\n") +
        string.Join("\n", Samples.Select(s => $"{s.Name}: {(s.Success ? "OK" : "ошибка")} · {s.Milliseconds} мс · {s.Detail}")) +
        "\nПроверен выход через локальный прокси в текущей сети Windows. TUN-маршруты, UDP, голос, вход в аккаунт и видеопоток не проверены. Если действует другой VPN, результат относится к работе поверх него.";
    public static int Rank(ProbeVerdict verdict) => verdict switch
    {
        ProbeVerdict.Verified => 6, ProbeVerdict.Quick => 5, ProbeVerdict.Slow => 4,
        ProbeVerdict.Partial => 3, ProbeVerdict.Unstable => 2, ProbeVerdict.Failed => 1, _ => 0
    };
}

/// <summary>Bounded fresh HTTPS requests. No OS proxy, routing, helper or DNS mutations.</summary>
public sealed class ProbeHttpTransport
{
    public async Task<ProbeSample> MeasureAsync(int port, ProbeTarget target, TimeSpan timeout, CancellationToken cancellation)
    {
        using var handler = new SocketsHttpHandler
        {
            Proxy = new WebProxy($"socks5://127.0.0.1:{port}"), UseProxy = true,
            AllowAutoRedirect = false, ConnectTimeout = timeout,
            AutomaticDecompression = DecompressionMethods.None
        };
        using var http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        return await MeasureWithClientAsync(http, target, timeout, cancellation).ConfigureAwait(false);
    }

    // Injectable HttpClient lets offline tests cover response validation, streaming and cancellation.
    public static async Task<ProbeSample> MeasureWithClientAsync(HttpClient http, ProbeTarget target,
        TimeSpan timeout, CancellationToken cancellation)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deadline.CancelAfter(timeout);
        var watch = Stopwatch.StartNew();
        long bytes = 0;
        bool transfer = target.ExactBytes > 0;
        ProbeSample Result(bool ok, string detail) => new(target.Name, ok, (int)watch.ElapsedMilliseconds,
            bytes, detail, target.Control, transfer);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, target.Url)
            {
                Version = HttpVersion.Version11, VersionPolicy = HttpVersionPolicy.RequestVersionExact
            };
            request.Headers.ConnectionClose = true;
            request.Headers.CacheControl = new() { NoCache = true, NoStore = true };
            request.Headers.UserAgent.ParseAdd("Mozilla/5.0 ElyProxy/Probe");
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
            int code = (int)response.StatusCode;
            if (code is 401 or 403 or 429 or 451) return Result(false, $"HTTP {code}: ограничение сервиса; это не доказывает отказ сервера");
            if (code >= 300 && code < 400) return Result(false, $"HTTP {code}: перенаправление, содержимое сервиса не подтверждено");
            if (target.Marker == "204")
            {
                if (code != 204 || response.Content.Headers.ContentLength is > 0) return Result(false, $"HTTP {code}, ожидался пустой 204");
                await using var empty = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
                int read = await empty.ReadAsync(new byte[1], deadline.Token).ConfigureAwait(false);
                return Result(read == 0, read == 0 ? "HTTPS: пустой 204 подтверждён" : "В ответе 204 обнаружено тело");
            }
            if (code != 200) return Result(false, $"HTTP {code}, ожидался 200");
            if (transfer && response.Content.Headers.ContentType?.MediaType?.Contains("html", StringComparison.OrdinalIgnoreCase) == true)
                return Result(false, "Вместо загрузки получен HTML");
            await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
            int limit = transfer ? target.ExactBytes : 65536;
            using var prefix = new MemoryStream();
            var buffer = new byte[8192];
            while (bytes < limit)
            {
                int read = await stream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, limit - bytes)), deadline.Token).ConfigureAwait(false);
                if (read == 0) break;
                bytes += read;
                if (!transfer) prefix.Write(buffer, 0, read);
                if (!transfer && bytes >= 4096) break;
            }
            if (transfer)
            {
                if (bytes != target.ExactBytes) return Result(false, $"Неполная загрузка: {bytes}/{target.ExactBytes} байт");
                if (response.Content.Headers.ContentLength is { } size && size != target.ExactBytes)
                    return Result(false, "Размер ответа не совпал с запрошенным");
                int extra = await stream.ReadAsync(buffer.AsMemory(0, 1), deadline.Token).ConfigureAwait(false);
                return Result(extra == 0, extra == 0 ? $"Загружено {bytes} байт полностью" : "Ответ больше запрошенного размера");
            }
            string body = Encoding.UTF8.GetString(prefix.ToArray());
            bool matches = target.Marker.Split('|', StringSplitOptions.RemoveEmptyEntries)
                .Any(marker => body.Contains(marker, StringComparison.OrdinalIgnoreCase));
            return Result(bytes > 0 && matches, matches ? "HTTPS и содержимое подтверждены" : "Неожиданное или пустое содержимое");
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) { return Result(false, "Тайм-аут, включая чтение тела"); }
        catch (Exception ex) when (ex is HttpRequestException or IOException or SocketException)
        { return Result(false, NetworkErrorMessage.Describe(ex)); }
    }
}

public sealed class ProbeCleanupException(Exception inner) : IOException("Очистка пробного ядра не подтверждена; проверка остановлена", inner);

public sealed class ElyHardService
{
    private sealed class OwnedProbe(IAsyncDisposable inner) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try { await inner.DisposeAsync().ConfigureAwait(false); }
            catch (Exception ex) { throw new ProbeCleanupException(ex); }
        }
    }
    public static IReadOnlyList<ProbeTarget> Controls { get; } = new[]
    {
        new ProbeTarget("Google HTTPS", "https://www.gstatic.com/generate_204", "204", Control: true),
        new ProbeTarget("Cloudflare HTTPS", "https://www.cloudflare.com/cdn-cgi/trace", "ip=", Control: true)
    };
    public static IReadOnlyList<ProbeTarget> Services { get; } = new[]
    {
        new ProbeTarget("Discord web", "https://discord.com/", "discord"),
        new ProbeTarget("Telegram web", "https://telegram.org/", "telegram"),
        new ProbeTarget("YouTube web", "https://www.youtube.com/", "youtube|ytcfg"),
        new ProbeTarget("Spotify web", "https://open.spotify.com/", "spotify"),
        new ProbeTarget("GitHub web", "https://github.com/", "github"),
        new ProbeTarget("Gmail web", "https://mail.google.com/mail/generate_204", "204")
    };
    public static ProbeTarget Download { get; } = new("Загрузка 1 МиБ", "https://speed.cloudflare.com/__down?bytes=1048576", ExactBytes: 1048576);
    private readonly Func<ProbeContext, int, Task<IAsyncDisposable>> _start;
    private readonly Func<int, ProbeTarget, TimeSpan, CancellationToken, Task<ProbeSample>> _measure;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    public ElyHardService() : this(async (context, port) => await ProbeCoreRunner.StartAsync(context.Node, port, context.Options,
        context.Core == ProxyCoreKind.SingBox).ConfigureAwait(false), new ProbeHttpTransport().MeasureAsync, Task.Delay) { }
    public ElyHardService(Func<ProbeContext, int, Task<IAsyncDisposable>> start,
        Func<int, ProbeTarget, TimeSpan, CancellationToken, Task<ProbeSample>> measure,
        Func<TimeSpan, CancellationToken, Task> delay)
    { _start = start; _measure = measure; _delay = delay; }

    public async Task<ServerProbeResult> CheckNodeAsync(ProbeContext context, bool hard, CancellationToken cancellation, SemaphoreSlim? transferGate = null)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        var activeBudget = hard ? TimeSpan.FromMinutes(3) : TimeSpan.FromSeconds(40);
        budget.CancelAfter(activeBudget);
        var activeTime = Stopwatch.StartNew();
        var samples = new List<ProbeSample>();
        int starts = 0;
        ServerProbeResult Result(string? failure = null) => new(context.Node.LibraryId, hard, context.Description, samples.ToArray(), starts, failure);
        try
        {
            for (int session = 0; session < (hard ? 2 : 1); session++)
            {
                budget.Token.ThrowIfCancellationRequested();
                using var listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start(); int port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
                // Await startup and disposal fully: cancellation must never orphan a probe core.
                await using var probe = new OwnedProbe(await _start(context, port).ConfigureAwait(false));
                starts++;
                budget.Token.ThrowIfCancellationRequested();
                int rounds = hard && session == 0 ? 2 : 1;
                for (int round = 0; round < rounds; round++)
                {
                    if (round > 0) await _delay(TimeSpan.FromSeconds(1), budget.Token).ConfigureAwait(false);
                    var controls = await Task.WhenAll(Controls.Select(t => _measure(port, t, TimeSpan.FromSeconds(hard ? 6 : 3), budget.Token))).ConfigureAwait(false);
                    samples.AddRange(controls);
                    if (controls.All(s => !s.Success)) return Result("Контрольные HTTPS-проверки не прошли; остальные сервисы не проверялись");
                    var targets = hard ? Services : Services.Take(3);
                    // The per-node request sequence and concurrency remain unchanged.
                    foreach (var pair in targets.Chunk(2))
                        samples.AddRange(await Task.WhenAll(pair.Select(t => _measure(port, t, TimeSpan.FromSeconds(hard ? 6 : 3), budget.Token))).ConfigureAwait(false));
                }
                if (hard)
                {
                    bool entered = false;
                    try
                    {
                        if (transferGate != null)
                        {
                            budget.Token.ThrowIfCancellationRequested();
                            activeTime.Stop();
                            budget.CancelAfter(Timeout.InfiniteTimeSpan);
                            await transferGate.WaitAsync(cancellation).ConfigureAwait(false);
                            entered = true;
                            activeTime.Start();
                            var remaining = activeBudget - activeTime.Elapsed;
                            if (remaining <= TimeSpan.Zero) budget.Cancel();
                            else budget.CancelAfter(remaining);
                        }
                        samples.Add(await _measure(port, Download, TimeSpan.FromSeconds(15), budget.Token).ConfigureAwait(false));
                    }
                    finally { if (entered) transferGate!.Release(); }
                }
            }
            return Result();
        }
        catch (ProbeCleanupException) { throw; }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) { return Result("Исчерпан бюджет проверки; результат неполный"); }
        catch (Exception ex) { DiagnosticHttpHandler.LogException("probe.failure", ex); return Result("Запуск/остановка пробы: " + LifecycleLog.Redact(ex.GetBaseException().Message)); }
    }
}
