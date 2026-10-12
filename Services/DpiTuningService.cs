using ElyProxy.Core;
using ElyProxy.Models;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace ElyProxy.Services;

public sealed record DpiTrial(DpiProfile Profile, int Successes, double Milliseconds, string Detail);

/// <summary>Explicit, bounded local probes. Never changes OS routes or active settings.</summary>
public sealed class DpiTuningService
{
    public static DpiTrial? Recommend(IEnumerable<DpiTrial> trials) => trials
        .Where(t => t.Successes == 4)
        .OrderBy(t => t.Profile.Id == "off" ? 0 : 1)
        .ThenBy(t => t.Milliseconds).FirstOrDefault();

    public async Task<IReadOnlyList<DpiTrial>> RunAsync(VlessNode node, bool singBox,
        XrayTunOptions options, IProgress<string> progress, CancellationToken cancellation)
    {
        var results = new List<DpiTrial>();
        foreach (var profile in DpiProfileCatalog.ForCore(singBox))
        {
            cancellation.ThrowIfCancellationRequested();
            progress.Report($"Проверка: {profile.Name}…");
            int successes = 0;
            double elapsed = 0;
            string detail = "";
            try
            {
                var listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start();
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                listener.Stop();
                await using var core = await ProbeCoreRunner.StartAsync(node, port,
                    profile.Apply(options), singBox).ConfigureAwait(false);
                using var handler = new HttpClientHandler
                {
                    Proxy = new WebProxy($"socks5://127.0.0.1:{port}"), UseProxy = true,
                    AllowAutoRedirect = false
                };
                using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
                // Two independent endpoints, repeated to reject one-off handshake success.
                foreach (var url in new[] { "https://www.gstatic.com/generate_204", "https://cp.cloudflare.com/generate_204",
                    "https://www.gstatic.com/generate_204", "https://cp.cloudflare.com/generate_204" })
                {
                    cancellation.ThrowIfCancellationRequested();
                    try
                    {
                        var watch = Stopwatch.StartNew();
                        using var request = new HttpRequestMessage(HttpMethod.Get, url);
                        request.Headers.ConnectionClose = true;
                        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellation).ConfigureAwait(false);
                        if (response.StatusCode == HttpStatusCode.NoContent)
                        {
                            successes++;
                            elapsed += watch.Elapsed.TotalMilliseconds;
                        }
                        else detail = $"HTTP {(int)response.StatusCode}";
                    }
                    catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { throw; }
                    catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
                    { detail = "HTTPS не прошёл (тайм-аут или ошибка соединения)"; }
                }
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { throw; }
            catch (Exception ex) { detail = "Ядро не запущено: " + ex.Message; }
            var trial = new DpiTrial(profile, successes, successes > 0 ? elapsed / successes : double.PositiveInfinity, detail);
            results.Add(trial);
            progress.Report($"{profile.Name}: {successes}/4" + (successes > 0 ? $", {trial.Milliseconds:0} мс" : "") + (detail.Length > 0 ? $" · {detail}" : ""));
        }
        return results;
    }
}
