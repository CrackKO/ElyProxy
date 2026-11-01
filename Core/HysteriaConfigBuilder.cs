using System;
using System.Linq;
using System.Text;
using ElyProxy.Models;

namespace ElyProxy.Core;

public sealed class HysteriaConfigBuilder
{
	public string Build(VlessNode node, int socksPort, int httpPort)
	{
		StringBuilder yaml = new StringBuilder();
		AppendCommon(yaml, node);
		yaml.AppendLine("socks5:");
		yaml.AppendLine("  listen: 127.0.0.1:" + socksPort);
		if (httpPort > 0)
		{
			yaml.AppendLine("http:");
			yaml.AppendLine("  listen: 127.0.0.1:" + httpPort);
		}
		return yaml.ToString();
	}

	public string BuildTun(VlessNode node, XrayTunOptions options)
	{
		StringBuilder yaml = new StringBuilder();
		AppendCommon(yaml, node);
		yaml.AppendLine("tun:");
		yaml.AppendLine("  name: " + Quote(string.IsNullOrWhiteSpace(options.InterfaceName) ? "ElyTun" : options.InterfaceName));
		yaml.AppendLine("  address:");
		yaml.AppendLine("    - " + Quote("172.19.0.1/30"));
		if (options.IncludeIpv6)
		{
			yaml.AppendLine("    - " + Quote("fdfe:dcba:9876::1/126"));
		}
		yaml.AppendLine("  mtu: " + options.Mtu);
		yaml.AppendLine("  autoRoute: true");
		string[] excludes = options.DirectIpCidrs
			.Where(cidr => !string.IsNullOrWhiteSpace(cidr))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToArray();
		if (excludes.Length > 0)
		{
			yaml.AppendLine("  exclude:");
			foreach (string cidr in excludes)
			{
				yaml.AppendLine("    - " + Quote(cidr));
			}
		}
		return yaml.ToString();
	}

	private static void AppendCommon(StringBuilder yaml, VlessNode node)
	{
		yaml.AppendLine("server: " + Quote(node.Address + ":" + node.Port));
		if (!string.IsNullOrWhiteSpace(node.Password))
		{
			yaml.AppendLine("auth: " + Quote(node.Password));
		}
		yaml.AppendLine("tls:");
		yaml.AppendLine("  sni: " + Quote(node.SNI));
		if (node.AllowInsecure)
		{
			yaml.AppendLine("  insecure: true");
		}
		if (!string.IsNullOrWhiteSpace(node.PinnedPeerCertificateChainSha256))
		{
			yaml.AppendLine("  pinSHA256: " + Quote(node.PinnedPeerCertificateChainSha256));
		}
		if (!string.IsNullOrWhiteSpace(node.Obfs))
		{
			yaml.AppendLine("obfs:");
			yaml.AppendLine("  type: " + Quote(node.Obfs));
			if (string.Equals(node.Obfs, "salamander", StringComparison.OrdinalIgnoreCase))
			{
				yaml.AppendLine("  salamander:");
				yaml.AppendLine("    password: " + Quote(node.ObfsPassword));
			}
			else
			{
				yaml.AppendLine("  " + node.Obfs + ":");
				yaml.AppendLine("    password: " + Quote(node.ObfsPassword));
			}
		}
		if (!string.IsNullOrWhiteSpace(node.ServerPorts))
		{
			yaml.AppendLine("serverPorts: " + Quote(node.ServerPorts));
		}
		if (!string.IsNullOrWhiteSpace(node.HopInterval))
		{
			yaml.AppendLine("hopInterval: " + Quote(NormalizeDuration(node.HopInterval)));
		}
		int upMbps = 0;
		int downMbps = 0;
		int.TryParse(node.UpMbps, out upMbps);
		int.TryParse(node.DownMbps, out downMbps);
		if (upMbps > 0 || downMbps > 0)
		{
			yaml.AppendLine("bandwidth:");
			if (upMbps > 0)
			{
				yaml.AppendLine("  up: " + Quote(upMbps + " mbps"));
			}
			if (downMbps > 0)
			{
				yaml.AppendLine("  down: " + Quote(downMbps + " mbps"));
			}
		}
	}

	private static string Quote(string? value)
	{
		string text = (value ?? string.Empty);
		return "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
	}

	private static string NormalizeDuration(string? value)
	{
		string text = (value ?? string.Empty).Trim();
		if (text.Length == 0)
		{
			return string.Empty;
		}
		return (int.TryParse(text, out int seconds) ? (seconds + "s") : text);
	}
}
