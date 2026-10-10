using System.Diagnostics;
using System.Net;

namespace ElyProxy.Services;

/// <summary>Checks the OS route without the Windows proxy, using independent HTTPS endpoints.</summary>
public sealed class ElyTunConnectivityService(HttpClient http)
{
    private static readonly Uri[] Endpoints =
    [
        new("https://www.gstatic.com/generate_204"),
        new("https://www.cloudflare.com/cdn-cgi/trace")
    ];

    public async Task<ElyTunConnectivityResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        using var checksCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var pending = Endpoints.Select(uri => ProbeAsync(uri, checksCancellation.Token)).ToList();
        var failures = new List<string>();
        try
        {
            while (pending.Count > 0)
            {
                Task<ElyTunConnectivityResult> completed = await Task.WhenAny(pending).ConfigureAwait(false);
                pending.Remove(completed);
                var result = await completed.ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (result.Reachable)
                {
                    checksCancellation.Cancel();
                    return result;
                }
                failures.Add(result.Details);
            }
            return new(false, stopwatch.ElapsedMilliseconds, string.Join("; ", failures));
        }
        finally
        {
            checksCancellation.Cancel();
            // Observe canceled/failed probes before disposing their shared HttpClient.
            await Task.WhenAll(pending).ConfigureAwait(false);
        }
    }

    private async Task<ElyTunConnectivityResult> ProbeAsync(Uri uri, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd("ElyProxy/ElyTun");
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            int status = (int)response.StatusCode;
            // A valid HTTPS response also proves reachability when the site refuses this client.
            bool reachable = status is >= 200 and < 500 && response.StatusCode != HttpStatusCode.ProxyAuthenticationRequired;
            return new(reachable, stopwatch.ElapsedMilliseconds, $"{uri.Host}: HTTP {status}");
        }
        catch (OperationCanceledException)
        {
            return new(false, stopwatch.ElapsedMilliseconds, $"{uri.Host}: время ожидания истекло или проверка отменена");
        }
        catch (HttpRequestException ex)
        {
            return new(false, stopwatch.ElapsedMilliseconds, $"{uri.Host}: {ex.HttpRequestError}");
        }
    }
}

public sealed record ElyTunConnectivityResult(bool Reachable, long ElapsedMilliseconds, string Details);
