using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using ElyProxy.Models;
using Newtonsoft.Json.Linq;

namespace ElyProxy.Services;

public static class ShareLinkBuilder
{
	public static string Build(VlessNode node)
	{
		string protocol = (node.Protocol ?? "vless").ToLowerInvariant();
		return protocol switch
		{
			"vless" => BuildVless(node),
			"vmess" => BuildVmess(node),
			"trojan" => BuildTrojan(node),
			"shadowsocks" or "ss" => BuildShadowsocks(node),
			"hysteria2" or "hy2" => BuildHysteria2(node),
			"tuic" => BuildTuic(node),
			"anytls" => BuildAnyTls(node),
			"ssh" => BuildSsh(node),
			"socks" => BuildSocks(node),
			"http" => BuildHttp(node),
			_ => string.Empty
		};
	}

	private static string BuildVless(VlessNode node)
	{
		List<string> query = new List<string>();
		AddIfSet(query, "type", node.Network);
		AddIfSet(query, "security", node.Security);
		AddIfSet(query, "sni", node.SNI);
		AddIfSet(query, "pbk", node.PublicKey);
		AddIfSet(query, "sid", node.ShortId);
		AddIfSet(query, "fp", node.Fingerprint);
		AddIfSet(query, "alpn", node.Alpn);
		AddIfSet(query, "path", node.Path);
		AddIfSet(query, "host", node.Host);
		AddIfSet(query, "headerType", node.HeaderType);
		AddIfSet(query, "spx", node.SpiderX);
		AddIfSet(query, "flow", node.Flow);
		AddIfSet(query, "mode", node.Mode);
		AddIfSet(query, "seed", node.Seed);
		if (!string.IsNullOrWhiteSpace(node.Encryption) && !string.Equals(node.Encryption, "none", StringComparison.OrdinalIgnoreCase))
		{
			AddIfSet(query, "encryption", node.Encryption);
		}
		AddBool(query, "allowInsecure", node.AllowInsecure);
		return BuildUri("vless", node.UUID, node, query);
	}

	private static string BuildVmess(VlessNode node)
	{
		JObject payload = new JObject
		{
			["v"] = "2",
			["ps"] = node.DisplayName,
			["add"] = node.Address,
			["port"] = node.Port.ToString(CultureInfo.InvariantCulture),
			["id"] = node.UUID,
			["aid"] = (string.IsNullOrWhiteSpace(node.AlterId) ? "0" : node.AlterId),
			["scy"] = (string.IsNullOrWhiteSpace(node.VmessSecurity) ? "auto" : node.VmessSecurity),
			["net"] = (string.IsNullOrWhiteSpace(node.Network) ? "tcp" : node.Network),
			["type"] = node.HeaderType,
			["host"] = node.Host,
			["path"] = node.Path,
			["tls"] = (string.Equals(node.Security, "tls", StringComparison.OrdinalIgnoreCase) ? "tls" : string.Empty),
			["sni"] = node.SNI,
			["alpn"] = node.Alpn,
			["fp"] = node.Fingerprint
		};
		string json = payload.ToString(Newtonsoft.Json.Formatting.None);
		return "vmess://" + Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
	}

	private static string BuildTrojan(VlessNode node)
	{
		List<string> query = new List<string>();
		AddIfSet(query, "security", node.Security);
		AddIfSet(query, "type", node.Network);
		AddIfSet(query, "sni", node.SNI);
		AddIfSet(query, "alpn", node.Alpn);
		AddIfSet(query, "fp", node.Fingerprint);
		AddIfSet(query, "path", node.Path);
		AddIfSet(query, "host", node.Host);
		AddBool(query, "allowInsecure", node.AllowInsecure);
		return BuildUri("trojan", Uri.EscapeDataString(node.Password), node, query);
	}

	private static string BuildShadowsocks(VlessNode node)
	{
		string credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes(node.Method + ":" + node.Password)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
		return $"ss://{credentials}@{FormatHost(node.Address)}:{node.Port}{Fragment(node)}";
	}

	private static string BuildHysteria2(VlessNode node)
	{
		List<string> query = new List<string>();
		AddIfSet(query, "sni", node.SNI);
		AddBool(query, "insecure", node.AllowInsecure);
		AddIfSet(query, "obfs", node.Obfs);
		AddIfSet(query, "obfs-password", node.ObfsPassword);
		AddIfSet(query, "pinSHA256", node.PinnedPeerCertificateChainSha256);
		AddIfSet(query, "mport", node.ServerPorts);
		AddIfSet(query, "hopInterval", node.HopInterval);
		AddIfSet(query, "up", node.UpMbps);
		AddIfSet(query, "down", node.DownMbps);
		return BuildUri("hysteria2", Uri.EscapeDataString(node.Password), node, query);
	}

	private static string BuildTuic(VlessNode node)
	{
		List<string> query = new List<string>();
		AddIfSet(query, "congestion_control", node.CongestionControl);
		AddIfSet(query, "udp_relay_mode", node.UdpRelayMode);
		AddIfSet(query, "alpn", node.Alpn);
		AddIfSet(query, "sni", node.SNI);
		AddBool(query, "allow_insecure", node.AllowInsecure);
		return BuildUri("tuic", Uri.EscapeDataString(node.UUID) + ":" + Uri.EscapeDataString(node.Password), node, query);
	}

	private static string BuildAnyTls(VlessNode node)
	{
		List<string> query = new List<string>();
		AddIfSet(query, "sni", node.SNI);
		AddIfSet(query, "alpn", node.Alpn);
		AddIfSet(query, "fp", node.Fingerprint);
		AddBool(query, "insecure", node.AllowInsecure);
		return BuildUri("anytls", Uri.EscapeDataString(node.Password), node, query);
	}

	private static string BuildSsh(VlessNode node)
	{
		string userInfo = Uri.EscapeDataString(node.Username);
		if (!string.IsNullOrWhiteSpace(node.Password))
		{
			userInfo += ":" + Uri.EscapeDataString(node.Password);
		}
		return BuildUri("ssh", userInfo, node, new List<string>());
	}

	private static string BuildSocks(VlessNode node)
	{
		string userInfo = Uri.EscapeDataString(node.Username);
		if (!string.IsNullOrWhiteSpace(node.Password))
		{
			userInfo += ":" + Uri.EscapeDataString(node.Password);
		}
		return BuildUri("socks", userInfo, node, new List<string>());
	}

	private static string BuildHttp(VlessNode node)
	{
		string scheme = (string.Equals(node.Security, "tls", StringComparison.OrdinalIgnoreCase) ? "https" : "http");
		string userInfo = Uri.EscapeDataString(node.Username);
		if (!string.IsNullOrWhiteSpace(node.Password))
		{
			userInfo += ":" + Uri.EscapeDataString(node.Password);
		}
		return BuildUri(scheme, userInfo, node, new List<string>());
	}

	private static string BuildUri(string scheme, string userInfo, VlessNode node, List<string> query)
	{
		string queryString = (query.Count > 0 ? "?" + string.Join("&", query) : string.Empty);
		return $"{scheme}://{userInfo}@{FormatHost(node.Address)}:{node.Port}{queryString}{Fragment(node)}";
	}

	private static string FormatHost(string address)
	{
		return (address.Contains(':') ? "[" + address + "]" : address);
	}

	private static string Fragment(VlessNode node)
	{
		return (string.IsNullOrWhiteSpace(node.Name) ? string.Empty : "#" + Uri.EscapeDataString(node.Name));
	}

	private static void AddIfSet(List<string> query, string key, string? value)
	{
		if (!string.IsNullOrWhiteSpace(value))
		{
			query.Add(key + "=" + Uri.EscapeDataString(value.Trim()));
		}
	}

	private static void AddBool(List<string> query, string key, bool value)
	{
		if (value)
		{
			query.Add(key + "=1");
		}
	}
}
