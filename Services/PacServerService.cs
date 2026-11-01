using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ElyProxy.Services;

public class PacServerService : IDisposable
{
	public static readonly string[] DefaultBypassRules = new string[6] { "*.ru", "*.рф", "*.xn--p1ai", "*.su", "*.com.ru", "*.edu.ru" };

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

		// Bind the replacement before stopping the current PAC server. If the new port is unavailable,
		// the old server remains available for the still-registered Windows PAC URL.
		var replacementListener = new TcpListener(IPAddress.Loopback, pacPort);
		replacementListener.Start();
		var replacementCancellation = new CancellationTokenSource();
		TcpListener? previousListener = _listener;
		CancellationTokenSource? previousCancellation = _cts;

		_pacPort = pacPort;
		_socksPort = socksPort;
		_bypassRules = NormalizeRules(bypassRules);
		_cts = replacementCancellation;
		_listener = replacementListener;
		_serverTask = Task.Run(() => AcceptLoopAsync(replacementListener, replacementCancellation.Token));

		previousCancellation?.Cancel();
		try
		{
			previousListener?.Stop();
		}
		catch
		{
		}
		previousCancellation?.Dispose();
		return Task.CompletedTask;
	}

	public void Stop()
	{
		_cts?.Cancel();
		try
		{
			_listener?.Stop();
		}
		catch
		{
		}
		_listener = null;
		_cts?.Dispose();
		_cts = null;
		_serverTask = null;
	}

	private async Task AcceptLoopAsync(TcpListener listener, CancellationToken cancellationToken)
	{
		while (!cancellationToken.IsCancellationRequested)
		{
			TcpClient client = null;
			try
			{
				client = await listener.AcceptTcpClientAsync(cancellationToken);
				Task.Run(() => HandleClientAsync(client, cancellationToken), cancellationToken);
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
		using (client)
		{
			int num = 2;
			try
			{
				NetworkStream stream = client.GetStream();
				byte[] buffer = new byte[2048];
				int read = await stream.ReadAsync(buffer, cancellationToken);
				if (!((Encoding.ASCII.GetString(buffer, 0, read).Split(new string[2] { "\r\n", "\n" }, StringSplitOptions.None).FirstOrDefault() ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1).FirstOrDefault() ?? "/").StartsWith("/proxy.pac", StringComparison.OrdinalIgnoreCase))
				{
					await WriteResponseAsync(stream, "404 Not Found", "text/plain; charset=utf-8", "Not found", cancellationToken);
				}
				else
				{
					await WriteResponseAsync(stream, "200 OK", "application/x-ns-proxy-autoconfig; charset=utf-8", BuildPacScript(_socksPort, (IReadOnlyCollection<string>)(object)_bypassRules), cancellationToken);
				}
			}
			catch
			{
			}
		}
	}

	private static async Task WriteResponseAsync(NetworkStream stream, string status, string contentType, string body, CancellationToken cancellationToken)
	{
		byte[] bodyBytes = Encoding.UTF8.GetBytes(body);
		string header = $"HTTP/1.1 {status}\r\nContent-Type: {contentType}\r\n" + "Cache-Control: no-cache, no-store, must-revalidate\r\nPragma: no-cache\r\nConnection: close\r\n" + $"Content-Length: {bodyBytes.Length}\r\n\r\n";
		byte[] headerBytes = Encoding.ASCII.GetBytes(header);
		await stream.WriteAsync(headerBytes, cancellationToken);
		await stream.WriteAsync(bodyBytes, cancellationToken);
	}

	private static string BuildPacScript(int socksPort, IReadOnlyCollection<string> bypassRules)
	{
		string rulesJson = JsonSerializer.Serialize(bypassRules);
		return $"function FindProxyForURL(url, host) {{\n    var h = host.toLowerCase();\n    var rules = {rulesJson};\n\n    if (isPlainHostName(h) || isLocalHost(h) || matchesDirectRule(h, rules)) {{\n        return \"DIRECT\";\n    }}\n\n    return \"SOCKS5 127.0.0.1:{socksPort}; SOCKS 127.0.0.1:{socksPort}; DIRECT\";\n}}\n\nfunction matchesDirectRule(host, rules) {{\n    for (var i = 0; i < rules.length; i++) {{\n        var rule = rules[i];\n\n        if (rule.indexOf(\"*\") >= 0) {{\n            if (shExpMatch(host, rule)) {{\n                return true;\n            }}\n            continue;\n        }}\n\n        if (host === rule || dnsDomainIs(host, \".\" + rule)) {{\n            return true;\n        }}\n    }}\n\n    return false;\n}}\n\nfunction isLocalHost(host) {{\n    return host === \"localhost\" ||\n           host === \"::1\" ||\n           host === \"[::1]\" ||\n           shExpMatch(host, \"127.*\") ||\n           shExpMatch(host, \"10.*\") ||\n           shExpMatch(host, \"192.168.*\") ||\n           shExpMatch(host, \"172.16.*\") ||\n           shExpMatch(host, \"172.17.*\") ||\n           shExpMatch(host, \"172.18.*\") ||\n           shExpMatch(host, \"172.19.*\") ||\n           shExpMatch(host, \"172.20.*\") ||\n           shExpMatch(host, \"172.21.*\") ||\n           shExpMatch(host, \"172.22.*\") ||\n           shExpMatch(host, \"172.23.*\") ||\n           shExpMatch(host, \"172.24.*\") ||\n           shExpMatch(host, \"172.25.*\") ||\n           shExpMatch(host, \"172.26.*\") ||\n           shExpMatch(host, \"172.27.*\") ||\n           shExpMatch(host, \"172.28.*\") ||\n           shExpMatch(host, \"172.29.*\") ||\n           shExpMatch(host, \"172.30.*\") ||\n           shExpMatch(host, \"172.31.*\");\n}}";
	}

	private static string[] NormalizeRules(IEnumerable<string>? rules)
	{
		string[] source = rules?.ToArray();
		if (source == null || source.Length <= 0)
		{
			source = DefaultBypassRules;
		}
		return (from rule in source.SelectMany(SplitRuleLine).SelectMany(NormalizeRuleVariants)
			where !string.IsNullOrWhiteSpace(rule)
			select rule).Distinct<string>(StringComparer.OrdinalIgnoreCase).ToArray();
	}

	private static IEnumerable<string> SplitRuleLine(string line)
	{
		return line.Split(new char[5] { '\r', '\n', '|', ';', ',' }, StringSplitOptions.RemoveEmptyEntries);
	}

	private static IEnumerable<string> NormalizeRuleVariants(string rule)
	{
		string normalized = rule.Trim().TrimStart('.');
		if (normalized.StartsWith("*."))
		{
			string text = normalized;
			normalized = text.Substring(2, text.Length - 2);
		}
		normalized = normalized.ToLowerInvariant();
		if (!string.IsNullOrWhiteSpace(normalized))
		{
			yield return normalized;
			string ascii = null;
			try
			{
				ascii = new IdnMapping().GetAscii(normalized);
			}
			catch
			{
			}
			if (!string.IsNullOrWhiteSpace(ascii) && !string.Equals(ascii, normalized, StringComparison.OrdinalIgnoreCase))
			{
				yield return ascii.ToLowerInvariant();
			}
		}
	}

	public void Dispose()
	{
		Stop();
		GC.SuppressFinalize(this);
	}
}
