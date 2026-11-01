using System;
using System.Collections.Generic;
using System.Linq;
using ElyProxy.Models;
using YamlDotNet.Serialization;

namespace ElyProxy.Services;

public static class ClashYamlExporter
{
	private static readonly ISerializer Serializer = new SerializerBuilder()
		.ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
		.Build();

	public static string Export(IEnumerable<VlessNode> nodes)
	{
		List<Dictionary<string, object?>> proxies = nodes.Select(Map).Where(proxy => proxy != null).Select(proxy => proxy!).ToList();
		Dictionary<string, object?> root = new Dictionary<string, object?>
		{
			["proxies"] = proxies
		};
		return Serializer.Serialize(root);
	}

	private static Dictionary<string, object?>? Map(VlessNode node)
	{
		string protocol = (node.Protocol ?? "vless").ToLowerInvariant();
		Dictionary<string, object?> proxy = new Dictionary<string, object?>
		{
			["name"] = node.DisplayName,
			["server"] = node.Address,
			["port"] = node.Port
		};
		AddIfSet(proxy, "network", node.Network);
		AddIfSet(proxy, "sni", node.SNI);
		AddIfSet(proxy, "alpn", SplitList(node.Alpn));
		if (node.AllowInsecure)
		{
			proxy["skip-cert-verify"] = true;
		}
		if (!string.IsNullOrWhiteSpace(node.Fingerprint) && node.Fingerprint != "chrome")
		{
			proxy["client-fingerprint"] = node.Fingerprint;
		}

		switch (protocol)
		{
		case "vless":
			proxy["type"] = "vless";
			proxy["uuid"] = node.UUID;
			proxy["tls"] = !string.Equals(node.Security, "none", StringComparison.OrdinalIgnoreCase);
			AddIfSet(proxy, "flow", node.Flow);
			if (string.Equals(node.Security, "reality", StringComparison.OrdinalIgnoreCase))
			{
				proxy["reality-opts"] = new Dictionary<string, object?>
				{
					["public-key"] = node.PublicKey,
					["short-id"] = node.ShortId
				};
			}
			ApplyTransportOptions(proxy, node);
			break;
		case "vmess":
			proxy["type"] = "vmess";
			proxy["uuid"] = node.UUID;
			proxy["alterId"] = (string.IsNullOrWhiteSpace(node.AlterId) ? "0" : node.AlterId);
			proxy["cipher"] = (string.IsNullOrWhiteSpace(node.VmessSecurity) ? "auto" : node.VmessSecurity);
			proxy["tls"] = string.Equals(node.Security, "tls", StringComparison.OrdinalIgnoreCase);
			ApplyTransportOptions(proxy, node);
			break;
		case "trojan":
			proxy["type"] = "trojan";
			proxy["password"] = node.Password;
			ApplyTransportOptions(proxy, node);
			break;
		case "shadowsocks":
		case "ss":
			proxy["type"] = "ss";
			proxy["cipher"] = node.Method;
			proxy["password"] = node.Password;
			break;
		case "hysteria2":
		case "hy2":
			proxy["type"] = "hysteria2";
			proxy["password"] = node.Password;
			AddIfSet(proxy, "obfs", node.Obfs);
			AddIfSet(proxy, "obfs-password", node.ObfsPassword);
			AddIfSet(proxy, "ports", node.ServerPorts);
			AddIfSet(proxy, "hop-interval", node.HopInterval);
			AddIfSet(proxy, "up", node.UpMbps);
			AddIfSet(proxy, "down", node.DownMbps);
			break;
		case "tuic":
			proxy["type"] = "tuic";
			proxy["uuid"] = node.UUID;
			proxy["password"] = node.Password;
			AddIfSet(proxy, "congestion-controller", node.CongestionControl);
			AddIfSet(proxy, "udp-relay-mode", node.UdpRelayMode);
			break;
		case "anytls":
			proxy["type"] = "anytls";
			proxy["password"] = node.Password;
			break;
		case "naive":
			proxy["type"] = "naive";
			AddIfSet(proxy, "username", node.Username);
			AddIfSet(proxy, "password", node.Password);
			break;
		case "ssh":
			proxy["type"] = "ssh";
			AddIfSet(proxy, "username", node.Username);
			AddIfSet(proxy, "password", node.Password);
			AddIfSet(proxy, "private-key", node.GetExtra("private-key"));
			break;
		case "wireguard":
			proxy["type"] = "wireguard";
			AddIfSet(proxy, "private-key", node.GetExtra("private-key"));
			AddIfSet(proxy, "public-key", node.GetExtra("public-key"));
			AddIfSet(proxy, "pre-shared-key", node.GetExtra("pre-shared-key"));
			AddIfSet(proxy, "allowed-ips", SplitList(node.GetExtra("allowed-ips")));
			AddIfSet(proxy, "ip", SplitList(node.GetExtra("address")));
			AddIfSet(proxy, "reserved", SplitList(node.GetExtra("reserved")));
			AddIfSet(proxy, "mtu", node.GetExtra("mtu"));
			break;
		case "shadowtls":
			proxy["type"] = "shadowtls";
			proxy["password"] = node.Password;
			AddIfSet(proxy, "version", node.GetExtra("shadowtls-version"));
			AddIfSet(proxy, "ss-method", node.GetExtra("ss-method"));
			AddIfSet(proxy, "ss-password", node.GetExtra("ss-password"));
			break;
		case "socks":
		case "socks5":
			proxy["type"] = "socks5";
			AddIfSet(proxy, "username", node.Username);
			AddIfSet(proxy, "password", node.Password);
			break;
		case "http":
			proxy["type"] = "http";
			proxy["tls"] = string.Equals(node.Security, "tls", StringComparison.OrdinalIgnoreCase);
			AddIfSet(proxy, "username", node.Username);
			AddIfSet(proxy, "password", node.Password);
			break;
		default:
			return null;
		}
		return proxy;
	}

	private static void ApplyTransportOptions(Dictionary<string, object?> proxy, VlessNode node)
	{
		string network = (node.Network ?? "tcp").ToLowerInvariant();
		switch (network)
		{
		case "ws":
			Dictionary<string, object?> wsOptions = new Dictionary<string, object?>
			{
				["path"] = (string.IsNullOrWhiteSpace(node.Path) ? "/" : node.Path)
			};
			if (!string.IsNullOrWhiteSpace(node.Host))
			{
				wsOptions["headers"] = new Dictionary<string, object?> { ["Host"] = node.Host };
			}
			proxy["ws-opts"] = wsOptions;
			break;
		case "grpc":
			proxy["grpc-opts"] = new Dictionary<string, object?> { ["grpc-service-name"] = node.Path };
			break;
		case "h2":
		case "http":
			Dictionary<string, object?> httpOptions = new Dictionary<string, object?>
			{
				["path"] = (string.IsNullOrWhiteSpace(node.Path) ? "/" : node.Path)
			};
			if (!string.IsNullOrWhiteSpace(node.Host))
			{
				httpOptions["host"] = SplitList(node.Host);
			}
			proxy["h2-opts"] = httpOptions;
			break;
		case "xhttp":
			Dictionary<string, object?> xhttpOptions = new Dictionary<string, object?>
			{
				["path"] = (string.IsNullOrWhiteSpace(node.Path) ? "/" : node.Path)
			};
			if (!string.IsNullOrWhiteSpace(node.Host))
			{
				xhttpOptions["host"] = node.Host;
			}
			if (!string.IsNullOrWhiteSpace(node.Mode))
			{
				xhttpOptions["mode"] = node.Mode;
			}
			proxy["xhttp-opts"] = xhttpOptions;
			break;
		}
	}

	private static List<string>? SplitList(string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return null;
		}
		List<string> items = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
		return (items.Count > 0 ? items : null);
	}

	private static void AddIfSet(Dictionary<string, object?> map, string key, string? value)
	{
		if (!string.IsNullOrWhiteSpace(value))
		{
			map[key] = value;
		}
	}

	private static void AddIfSet(Dictionary<string, object?> map, string key, List<string>? value)
	{
		if (value is { Count: > 0 })
		{
			map[key] = value;
		}
	}
}
