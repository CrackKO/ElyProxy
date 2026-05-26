using System.Net;
using System.Net.Sockets;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace ElyProxy.Services;

public class PacServerService : IDisposable
{
    public static readonly string[] DefaultBypassRules =
    [
        "*.ru",
        "*.рф",
        "*.xn--p1ai",
        "*.su",
        "*.com.ru",
        "*.edu.ru"
    ];

    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _serverTask;
    private string[] _bypassRules = DefaultBypassRules;
    private int _pacPort;
    private int _socksPort;

    public bool IsRunning => _listener != null;
    public string PacUrl => $"http://127.0.0.1:{_pacPort}/proxy.pac";

    public Task StartAsync(int pacPort, int socksPort, IEnumerable<string>? bypassRules)
    {
        if (IsRunning && _pacPort == pacPort)
        {
            _socksPort = socksPort;
            _bypassRules = NormalizeRules(bypassRules);
            return Task.CompletedTask;
        }

        Stop();

        _pacPort = pacPort;
        _socksPort = socksPort;
        _bypassRules = NormalizeRules(bypassRules);
        _cts = new CancellationTokenSource();
        _listener = new TcpListener(IPAddress.Loopback, pacPort);
        _listener.Start();
        _serverTask = Task.Run(() => AcceptLoopAsync(_cts.Token));

        return Task.CompletedTask;
    }

    public void Stop()
    {
        _cts?.Cancel();

        try { _listener?.Stop(); }
        catch { }

        _listener = null;
        _cts?.Dispose();
        _cts = null;
        _serverTask = null;
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _listener != null)
        {
            TcpClient? client = null;
            try
            {
                client = await _listener.AcceptTcpClientAsync(cancellationToken);
                _ = Task.Run(() => HandleClientAsync(client, cancellationToken), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                client?.Dispose();
                break;
            }
            catch
            {
                client?.Dispose();
            }
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using var _ = client;

        try
        {
            var stream = client.GetStream();
            var buffer = new byte[2048];
            var read = await stream.ReadAsync(buffer, cancellationToken);
            var request = Encoding.ASCII.GetString(buffer, 0, read);
            var firstLine = request.Split(["\r\n", "\n"], StringSplitOptions.None).FirstOrDefault() ?? string.Empty;
            var path = firstLine.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1).FirstOrDefault() ?? "/";

            if (!path.StartsWith("/proxy.pac", StringComparison.OrdinalIgnoreCase))
            {
                await WriteResponseAsync(stream, "404 Not Found", "text/plain; charset=utf-8", "Not found", cancellationToken);
                return;
            }

            await WriteResponseAsync(
                stream,
                "200 OK",
                "application/x-ns-proxy-autoconfig; charset=utf-8",
                BuildPacScript(_socksPort, _bypassRules),
                cancellationToken);
        }
        catch
        {
            // The browser may close PAC probes early. Nothing useful to surface.
        }
    }

    private static async Task WriteResponseAsync(
        NetworkStream stream,
        string status,
        string contentType,
        string body,
        CancellationToken cancellationToken)
    {
        var bodyBytes = Encoding.UTF8.GetBytes(body);
        var header =
            $"HTTP/1.1 {status}\r\n" +
            $"Content-Type: {contentType}\r\n" +
            "Cache-Control: no-cache, no-store, must-revalidate\r\n" +
            "Pragma: no-cache\r\n" +
            "Connection: close\r\n" +
            $"Content-Length: {bodyBytes.Length}\r\n\r\n";

        var headerBytes = Encoding.ASCII.GetBytes(header);
        await stream.WriteAsync(headerBytes, cancellationToken);
        await stream.WriteAsync(bodyBytes, cancellationToken);
    }

    private static string BuildPacScript(int socksPort, IReadOnlyCollection<string> bypassRules)
    {
        var rulesJson = JsonSerializer.Serialize(bypassRules);

        return $$"""
function FindProxyForURL(url, host) {
    var h = host.toLowerCase();
    var rules = {{rulesJson}};

    if (isPlainHostName(h) || isLocalHost(h) || matchesDirectRule(h, rules)) {
        return "DIRECT";
    }

    return "SOCKS5 127.0.0.1:{{socksPort}}; SOCKS 127.0.0.1:{{socksPort}}; DIRECT";
}

function matchesDirectRule(host, rules) {
    for (var i = 0; i < rules.length; i++) {
        var rule = rules[i];

        if (rule.indexOf("*") >= 0) {
            if (shExpMatch(host, rule)) {
                return true;
            }
            continue;
        }

        if (host === rule || dnsDomainIs(host, "." + rule)) {
            return true;
        }
    }

    return false;
}

function isLocalHost(host) {
    return host === "localhost" ||
           host === "::1" ||
           host === "[::1]" ||
           shExpMatch(host, "127.*") ||
           shExpMatch(host, "10.*") ||
           shExpMatch(host, "192.168.*") ||
           shExpMatch(host, "172.16.*") ||
           shExpMatch(host, "172.17.*") ||
           shExpMatch(host, "172.18.*") ||
           shExpMatch(host, "172.19.*") ||
           shExpMatch(host, "172.20.*") ||
           shExpMatch(host, "172.21.*") ||
           shExpMatch(host, "172.22.*") ||
           shExpMatch(host, "172.23.*") ||
           shExpMatch(host, "172.24.*") ||
           shExpMatch(host, "172.25.*") ||
           shExpMatch(host, "172.26.*") ||
           shExpMatch(host, "172.27.*") ||
           shExpMatch(host, "172.28.*") ||
           shExpMatch(host, "172.29.*") ||
           shExpMatch(host, "172.30.*") ||
           shExpMatch(host, "172.31.*");
}
""";
    }

    private static string[] NormalizeRules(IEnumerable<string>? rules)
    {
        var source = rules?.ToArray();
        if (source is not { Length: > 0 })
            source = DefaultBypassRules;

        return source
            .SelectMany(SplitRuleLine)
            .SelectMany(NormalizeRuleVariants)
            .Where(rule => !string.IsNullOrWhiteSpace(rule))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IEnumerable<string> SplitRuleLine(string line)
    {
        return line.Split(['\r', '\n', '|', ';', ','], StringSplitOptions.RemoveEmptyEntries);
    }

    private static IEnumerable<string> NormalizeRuleVariants(string rule)
    {
        var normalized = rule.Trim().TrimStart('.');
        if (normalized.StartsWith("*."))
            normalized = normalized[2..];

        normalized = normalized.ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
            yield break;

        yield return normalized;

        string? ascii = null;
        try
        {
            ascii = new IdnMapping().GetAscii(normalized);
        }
        catch
        {
            // Keep the user-provided rule even if it is not an IDN domain.
        }

        if (!string.IsNullOrWhiteSpace(ascii)
            && !string.Equals(ascii, normalized, StringComparison.OrdinalIgnoreCase))
        {
            yield return ascii.ToLowerInvariant();
        }
    }

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }
}
