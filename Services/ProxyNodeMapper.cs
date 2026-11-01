using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ElyProxy.Models;

namespace ElyProxy.Services;

public static class ProxyNodeMapper
{
	public static VlessNode? MapNode(IReadOnlyDictionary<string, object?> map)
	{
		string type = (Str(map, "type") ?? string.Empty).ToLowerInvariant();
		string server = Str(map, "server") ?? string.Empty;
		int port = Int(map, "port");
		if (string.IsNullOrWhiteSpace(server) || port < 1 || port > 65535)
		{
			return null;
		}
		VlessNode node = new VlessNode
		{
			Name = (Str(map, "name") ?? string.Empty),
			Address = server,
			Port = port,
			Network = "tcp",
			Security = "none",
			SNI = (Str(map, "servername") ?? Str(map, "sni") ?? string.Empty),
			AllowInsecure = Bool(map, "skip-cert-verify"),
			Alpn = JoinList(map, "alpn"),
			Fingerprint = (Str(map, "client-fingerprint") ?? "chrome")
		};
		string network = (Str(map, "network") ?? string.Empty).ToLowerInvariant();
		if (!string.IsNullOrWhiteSpace(network))
		{
			node.Network = network;
		}
		IReadOnlyDictionary<string, object?>? reality = Map(map, "reality-opts");
		node.Security = ((reality != null) ? "reality" : (Bool(map, "tls") ? "tls" : "none"));
		if (reality != null)
		{
			node.PublicKey = (Str(reality, "public-key") ?? string.Empty);
			node.ShortId = (Str(reality, "short-id") ?? string.Empty);
			string? realityServerName = Str(reality, "server-name");
			if (!string.IsNullOrWhiteSpace(realityServerName))
			{
				node.SNI = realityServerName!;
			}
		}
		ApplyTransportOptions(node, map, network);
		switch (type)
		{
		case "vless":
			node.Protocol = "vless";
			node.UUID = (Str(map, "uuid") ?? string.Empty);
			node.Flow = (Str(map, "flow") ?? string.Empty);
			if (string.IsNullOrWhiteSpace(node.UUID))
			{
				return null;
			}
			break;
		case "vmess":
			node.Protocol = "vmess";
			node.UUID = (Str(map, "uuid") ?? string.Empty);
			node.AlterId = (Str(map, "alterId") ?? Str(map, "alterid") ?? "0");
			node.VmessSecurity = (Str(map, "cipher") ?? "auto");
			if (string.IsNullOrWhiteSpace(node.UUID))
			{
				return null;
			}
			break;
		case "trojan":
			node.Protocol = "trojan";
			node.Password = (Str(map, "password") ?? string.Empty);
			node.Security = (reality != null ? "reality" : "tls");
			break;
		case "ss":
		case "shadowsocks":
			node.Protocol = "shadowsocks";
			node.Method = (Str(map, "cipher") ?? Str(map, "method") ?? string.Empty);
			node.Password = (Str(map, "password") ?? string.Empty);
			node.Security = "none";
			if (string.IsNullOrWhiteSpace(node.Method))
			{
				return null;
			}
			break;
		case "hysteria2":
		case "hy2":
			node.Protocol = "hysteria2";
			node.Password = (Str(map, "password") ?? Str(map, "auth") ?? string.Empty);
			node.Obfs = (Str(map, "obfs") ?? string.Empty);
			node.ObfsPassword = (Str(map, "obfs-password") ?? string.Empty);
			node.ServerPorts = (Str(map, "ports") ?? string.Empty);
			node.HopInterval = (Str(map, "hop-interval") ?? Str(map, "hopInterval") ?? string.Empty);
			node.UpMbps = (Str(map, "up") ?? string.Empty);
			node.DownMbps = (Str(map, "down") ?? string.Empty);
			node.Security = "tls";
			break;
		case "tuic":
			node.Protocol = "tuic";
			node.UUID = (Str(map, "uuid") ?? string.Empty);
			node.Password = (Str(map, "password") ?? string.Empty);
			node.CongestionControl = (Str(map, "congestion-controller") ?? Str(map, "congestion_control") ?? string.Empty);
			node.UdpRelayMode = (Str(map, "udp-relay-mode") ?? Str(map, "udp_relay_mode") ?? string.Empty);
			node.Security = "tls";
			break;
		case "anytls":
			node.Protocol = "anytls";
			node.Password = (Str(map, "password") ?? string.Empty);
			node.Security = "tls";
			break;
		case "naive":
			node.Protocol = "naive";
			node.Username = (Str(map, "username") ?? string.Empty);
			node.Password = (Str(map, "password") ?? string.Empty);
			node.Security = "tls";
			break;
		case "shadowtls":
			node.Protocol = "shadowtls";
			node.Password = (Str(map, "password") ?? string.Empty);
			node.Security = "tls";
			Put(node, "shadowtls-version", Str(map, "version"));
			Put(node, "ss-method", Str(map, "ss-method") ?? Str(map, "method"));
			Put(node, "ss-password", Str(map, "ss-password"));
			break;
		case "ssh":
			node.Protocol = "ssh";
			node.Username = (Str(map, "username") ?? Str(map, "user") ?? string.Empty);
			node.Password = (Str(map, "password") ?? string.Empty);
			Put(node, "private-key", Str(map, "private-key"));
			Put(node, "private-key-passphrase", Str(map, "private-key-passphrase"));
			node.Security = "none";
			break;
		case "wireguard":
			node.Protocol = "wireguard";
			Put(node, "private-key", Str(map, "private-key"));
			Put(node, "public-key", Str(map, "public-key"));
			Put(node, "pre-shared-key", Str(map, "pre-shared-key"));
			Put(node, "allowed-ips", JoinList(map, "allowed-ips") is { Length: > 0 } allowed ? allowed : "0.0.0.0/0,::/0");
			Put(node, "address", JoinList(map, "ip") is { Length: > 0 } addresses ? addresses : "172.16.0.2/32");
			Put(node, "mtu", Str(map, "mtu"));
			Put(node, "reserved", JoinList(map, "reserved"));
			node.Security = "none";
			break;
		case "socks5":
		case "socks":
			node.Protocol = "socks";
			node.Username = (Str(map, "username") ?? string.Empty);
			node.Password = (Str(map, "password") ?? string.Empty);
			node.Security = "none";
			break;
		case "http":
			node.Protocol = "http";
			node.Username = (Str(map, "username") ?? string.Empty);
			node.Password = (Str(map, "password") ?? string.Empty);
			node.Security = Bool(map, "tls") ? "tls" : "none";
			break;
		default:
			return null;
		}
		return node;
	}

	private static void ApplyTransportOptions(VlessNode node, IReadOnlyDictionary<string, object?> map, string network)
	{
		IReadOnlyDictionary<string, object?>? ws = Map(map, "ws-opts");
		if (ws != null)
		{
			node.Path = (Str(ws, "path") ?? node.Path);
			IReadOnlyDictionary<string, object?>? headers = Map(ws, "headers");
			if (headers != null)
			{
				node.Host = (Str(headers, "Host") ?? Str(headers, "host") ?? node.Host);
			}
		}
		IReadOnlyDictionary<string, object?>? grpc = Map(map, "grpc-opts");
		if (grpc != null)
		{
			node.Path = (Str(grpc, "grpc-service-name") ?? node.Path);
		}
		IReadOnlyDictionary<string, object?>? http = Map(map, "http-opts") ?? Map(map, "h2-opts");
		if (http != null)
		{
			node.Path = (Str(http, "path") ?? node.Path);
			node.Host = (JoinList(http, "host") is { Length: > 0 } hosts ? hosts : node.Host);
		}
		IReadOnlyDictionary<string, object?>? xhttp = Map(map, "xhttp-opts");
		if (xhttp != null)
		{
			node.Path = (Str(xhttp, "path") ?? node.Path);
			node.Host = (Str(xhttp, "host") ?? node.Host);
			node.Mode = (Str(xhttp, "mode") ?? node.Mode);
		}
		if (network is "kcp" or "mkcp")
		{
			IReadOnlyDictionary<string, object?>? kcp = Map(map, "kcp-opts");
			if (kcp != null)
			{
				Put(node, "kcp-mtu", Str(kcp, "mtu"));
				Put(node, "kcp-tti", Str(kcp, "tti"));
				Put(node, "kcp-uplink", Str(kcp, "uplinkCapacity"));
				Put(node, "kcp-downlink", Str(kcp, "downlinkCapacity"));
				Put(node, "kcp-congestion", Str(kcp, "congestion"));
				Put(node, "kcp-read-buffer", Str(kcp, "readBufferSize"));
				Put(node, "kcp-write-buffer", Str(kcp, "writeBufferSize"));
				Put(node, "kcp-seed", Str(kcp, "seed"));
				IReadOnlyDictionary<string, object?>? kcpHeader = Map(kcp, "header");
				if (kcpHeader != null)
				{
					node.HeaderType = (Str(kcpHeader, "type") ?? node.HeaderType);
				}
			}
			else
			{
				Put(node, "seed", Str(map, "seed"));
				IReadOnlyDictionary<string, object?>? header = Map(map, "header");
				if (header != null)
				{
					node.HeaderType = (Str(header, "type") ?? node.HeaderType);
				}
			}
		}
	}

	private static void Put(VlessNode node, string key, string? value)
	{
		if (!string.IsNullOrWhiteSpace(value))
		{
			node.Extra[key] = value!;
		}
	}

	private static string? Str(IReadOnlyDictionary<string, object?> map, string key)
	{
		if (!map.TryGetValue(key, out object? value) || value == null)
		{
			return null;
		}
		return value switch
		{
			string text => text,
			bool flag => flag ? "true" : "false",
			_ => Convert.ToString(value, CultureInfo.InvariantCulture)
		};
	}

	private static bool Bool(IReadOnlyDictionary<string, object?> map, string key)
	{
		if (!map.TryGetValue(key, out object? value) || value == null)
		{
			return false;
		}
		if (value is bool flag)
		{
			return flag;
		}
		string text = value.ToString() ?? string.Empty;
		return text.Equals("true", StringComparison.OrdinalIgnoreCase) || text == "1";
	}

	private static int Int(IReadOnlyDictionary<string, object?> map, string key)
	{
		string? text = Str(map, key);
		return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : 0;
	}

	private static IReadOnlyDictionary<string, object?>? Map(IReadOnlyDictionary<string, object?> map, string key)
	{
		return (map.TryGetValue(key, out object? value) ? value as IReadOnlyDictionary<string, object?> : null);
	}

	private static IReadOnlyList<object?>? List(IReadOnlyDictionary<string, object?> map, string key)
	{
		return (map.TryGetValue(key, out object? value) ? value as IReadOnlyList<object?> : null);
	}

	private static string JoinList(IReadOnlyDictionary<string, object?> map, string key)
	{
		if (!map.TryGetValue(key, out object? value) || value == null)
		{
			return string.Empty;
		}
		if (value is IReadOnlyList<object?> list)
		{
			return string.Join(",", list.Select(item => item?.ToString()).Where(item => !string.IsNullOrWhiteSpace(item)));
		}
		return value.ToString() ?? string.Empty;
	}
}
