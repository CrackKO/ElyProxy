using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using ElyProxy.Core;
using ElyProxy.Models;

namespace ElyProxy.Services;

public sealed class ElyHardService
{
    private static readonly ElyHardTarget[] Targets =
    [
        new("Discord", "https://discord.com/api/v9/experiments"),
        new("Telegram", "https://telegram.org/"),
        new("YouTube", "https://www.youtube.com/"),
        new("Spotify", "https://open.spotify.com/"),
        new("GitHub", "https://github.com/"),
        new("Gmail", "https://mail.google.com/mail/u/0/")
    ];

    public async Task<ElyHardNodeResult> CheckNodeAsync(VlessNode node, ElyHardOptions options)
    {
        var probePort = GetFreeTcpPort();
        using var xray = new XrayManager();

        try
        {
            await xray.StartAsync(node, probePort);

            if (!await WaitForLocalPortAsync(probePort, options.StartupTimeout))
                return ElyHardNodeResult.Empty(node, Targets);

            using var handler = new HttpClientHandler
            {
                Proxy = new WebProxy($"socks5://127.0.0.1:{probePort}"),
                UseProxy = true,
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 5
            };
            using var http = new HttpClient(handler)
            {
                Timeout = options.RequestTimeout
            };

            http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 ElyProxy/ElyHard");

            var services = await Task.WhenAll(Targets.Select(target => CheckTargetAsync(http, target, options.AttemptsPerTarget)));
            return new ElyHardNodeResult(node, services);
        }
        catch
        {
            return ElyHardNodeResult.Empty(node, Targets);
        }
        finally
        {
            await xray.StopAsync();
        }
    }

    private static async Task<ElyHardServiceResult> CheckTargetAsync(HttpClient http, ElyHardTarget target, int attempts)
    {
        var latencies = new List<int>(attempts);
        string? lastError = null;

        for (var i = 0; i < attempts; i++)
        {
            try
            {
                var sw = Stopwatch.StartNew();
                using var response = await http.GetAsync(target.Url, HttpCompletionOption.ResponseHeadersRead);
                sw.Stop();

                var code = (int)response.StatusCode;
                if (code is >= 200 and < 500)
                {
                    latencies.Add((int)sw.ElapsedMilliseconds);
                    continue;
                }

                lastError = $"HTTP {code}";
            }
            catch (Exception ex)
            {
                lastError = ex.GetType().Name;
            }
        }

        return new ElyHardServiceResult(target.Name, attempts, latencies, lastError);
    }

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static async Task<bool> WaitForLocalPortAsync(int port, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync(IPAddress.Loopback, port).WaitAsync(TimeSpan.FromMilliseconds(250));
                return true;
            }
            catch
            {
                await Task.Delay(100);
            }
        }

        return false;
    }

    public sealed record ElyHardTarget(string Name, string Url);

    public sealed record ElyHardOptions(
        int AttemptsPerTarget,
        TimeSpan StartupTimeout,
        TimeSpan RequestTimeout);

    public sealed class ElyHardServiceResult
    {
        public ElyHardServiceResult(string name, int attempts, IReadOnlyList<int> successfulLatencies, string? lastError)
        {
            Name = name;
            Attempts = attempts;
            SuccessfulLatencies = successfulLatencies;
            LastError = lastError;
        }

        public string Name { get; }
        public int Attempts { get; }
        public IReadOnlyList<int> SuccessfulLatencies { get; }
        public string? LastError { get; }
        public int SuccessCount => SuccessfulLatencies.Count;
        public bool IsStable => SuccessCount == Attempts;
        public bool IsReachable => SuccessCount > 0;
        public int? AverageLatencyMs => IsReachable
            ? (int)Math.Round(SuccessfulLatencies.Average())
            : null;

        public string CompactDisplay => IsReachable
            ? $"{Name} {SuccessCount}/{Attempts} {AverageLatencyMs} ms"
            : $"{Name} 0/{Attempts}";
    }

    public sealed class ElyHardNodeResult
    {
        public ElyHardNodeResult(VlessNode node, IReadOnlyList<ElyHardServiceResult> services)
        {
            Node = node;
            Services = services;
        }

        public VlessNode Node { get; }
        public IReadOnlyList<ElyHardServiceResult> Services { get; }
        public int TotalServices => Services.Count;
        public int StableServices => Services.Count(service => service.IsStable);
        public int ReachableServices => Services.Count(service => service.IsReachable);
        public int TotalSuccessfulAttempts => Services.Sum(service => service.SuccessCount);
        public int TotalAttempts => Services.Sum(service => service.Attempts);
        public int? AverageLatencyMs => ReachableServices > 0
            ? (int)Math.Round(Services.Where(service => service.AverageLatencyMs.HasValue).Average(service => service.AverageLatencyMs!.Value))
            : null;
        public int? WorstLatencyMs => ReachableServices > 0
            ? Services.Where(service => service.AverageLatencyMs.HasValue).Max(service => service.AverageLatencyMs!.Value)
            : null;

        public static ElyHardNodeResult Empty(VlessNode node, IReadOnlyList<ElyHardTarget> targets)
        {
            return new ElyHardNodeResult(
                node,
                targets.Select(target => new ElyHardServiceResult(target.Name, 2, [], "not started")).ToList());
        }
    }
}
