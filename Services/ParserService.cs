using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Text;
using System.Web;
using ElyProxy.Models;
using Newtonsoft.Json.Linq;

namespace ElyProxy.Services;

public class ParserService
{
	private static readonly string[] SupportedSchemes = new string[10] { "vless://", "vmess://", "trojan://", "ss://", "hysteria2://", "hy2://", "tuic://", "socks://", "naive://", "naive+https://" };

	public List<VlessNode> ParseSubscriptionContent(string content)
	{
		string raw = content.Trim();
		if (raw.Length > 12 * 1024 * 1024)
		{
			return new List<VlessNode>();
		}
		string decoded = TryBase64Decode(raw) ?? raw;
		string trimmedDecoded = decoded.TrimStart();
		if (trimmedDecoded.StartsWith('{') || trimmedDecoded.StartsWith('['))
		{
			List<VlessNode> jsonNodes = new JsonConfigParser().Parse(decoded, ParseUri);
			if (jsonNodes.Count > 0)
			{
				return jsonNodes;
			}
		}
		if (ClashYamlParser.LooksLikeClashYaml(decoded))
		{
			List<VlessNode> clashNodes = new ClashYamlParser().Parse(decoded);
			if (clashNodes.Count > 0)
			{
				return clashNodes;
			}
		}
		List<VlessNode> nodes = new List<VlessNode>();
		foreach (string line in decoded.Split(new char[2] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
		{
			string trimmed = line.Trim();
			VlessNode? node = null;
			if (IsSupportedUri(trimmed))
			{
				node = ParseUri(trimmed);
			}
			else if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
			{
				node = ParseHttpUri(trimmed);
			}
			if (node != null)
			{
				nodes.Add(node);
			}
		}
		return nodes;
	}

	public VlessNode? ParseUri(string uri)
	{
		if (string.IsNullOrWhiteSpace(uri))
		{
			return null;
		}
		string value = uri.Trim();
		if (value.StartsWith("vless://", StringComparison.OrdinalIgnoreCase))
		{
			return ParseVlessUri(value);
		}
		if (value.StartsWith("vmess://", StringComparison.OrdinalIgnoreCase))
		{
			return ParseVmessUri(value);
		}
		if (value.StartsWith("trojan://", StringComparison.OrdinalIgnoreCase))
		{
			return ParseTrojanUri(value);
		}
		if (value.StartsWith("ss://", StringComparison.OrdinalIgnoreCase))
		{
			return ParseShadowsocksUri(value);
		}
		if (value.StartsWith("hysteria2://", StringComparison.OrdinalIgnoreCase) || value.StartsWith("hy2://", StringComparison.OrdinalIgnoreCase))
		{
			return ParseHysteria2Uri(value);
		}
		if (value.StartsWith("tuic://", StringComparison.OrdinalIgnoreCase))
		{
			return ParseTuicUri(value);
		}
		if (value.StartsWith("socks://", StringComparison.OrdinalIgnoreCase))
		{
			return ParseSocksUri(value);
		}
		if (value.StartsWith("anytls://", StringComparison.OrdinalIgnoreCase))
		{
			return ParseAnyTlsUri(value);
		}
		if (value.StartsWith("naive://", StringComparison.OrdinalIgnoreCase) || value.StartsWith("naive+https://", StringComparison.OrdinalIgnoreCase))
		{
			return ParseNaiveUri(value);
		}
		if (value.StartsWith("ssh://", StringComparison.OrdinalIgnoreCase))
		{
			return ParseSshUri(value);
		}
		if (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
		{
			return ParseHttpUri(value);
		}
		return null;
	}

	public VlessNode? ParseHttpUri(string uri)
	{
		try
		{
			bool tls = uri.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
			string scheme = (tls ? "https://" : "http://");
			if (!TrySplitUri(uri, scheme, out string body, out string name))
			{
				return null;
			}
			SplitQuery(ref body, out NameValueCollection query);
			int atIdx = body.LastIndexOf('@');
			if (atIdx < 0)
			{
				return null;
			}
			string userInfo = Uri.UnescapeDataString(body.Substring(0, atIdx));
			string hostPort = body.Substring(atIdx + 1);
			if (!TryParseHostPort(hostPort, out string address, out int port))
			{
				return null;
			}
			int colon = userInfo.IndexOf(':');
			return new VlessNode
			{
				Protocol = "http",
				Name = name,
				Address = address,
				Port = port,
				Username = ((colon >= 0) ? userInfo.Substring(0, colon) : userInfo),
				Password = ((colon >= 0) ? userInfo.Substring(colon + 1) : string.Empty),
				Security = (tls ? "tls" : "none"),
				Network = "tcp",
				SNI = (query["sni"] ?? string.Empty),
				AllowInsecure = IsTruthy(query["allowInsecure"] ?? query["insecure"])
			};
		}
		catch
		{
			return null;
		}
	}

	public VlessNode? ParseNaiveUri(string uri)
	{
		try
		{
			string scheme = uri.StartsWith("naive+https://", StringComparison.OrdinalIgnoreCase) ? "naive+https://" : "naive://";
			if (!TrySplitUri(uri, scheme, out string body, out string name))
			{
				return null;
			}
			SplitQuery(ref body, out NameValueCollection query);
			int atIdx = body.LastIndexOf('@');
			if (atIdx < 0)
			{
				return null;
			}
			string userInfo = Uri.UnescapeDataString(body.Substring(0, atIdx));
			string hostPort = body.Substring(atIdx + 1);
			if (!TryParseHostPort(hostPort, out string address, out int port))
			{
				return null;
			}
			int colon = userInfo.IndexOf(':');
			return new VlessNode
			{
				Protocol = "naive",
				Name = name,
				Address = address,
				Port = port,
				Username = ((colon >= 0) ? userInfo.Substring(0, colon) : userInfo),
				Password = ((colon >= 0) ? userInfo.Substring(colon + 1) : string.Empty),
				Security = "tls",
				SNI = (query["sni"] ?? string.Empty),
				Alpn = (query["alpn"] ?? string.Empty),
				AllowInsecure = IsTruthy(query["insecure"] ?? query["allowInsecure"])
			};
		}
		catch
		{
			return null;
		}
	}

	public VlessNode? ParseAnyTlsUri(string uri)
	{		try
		{
			if (!TrySplitUri(uri, "anytls://", out string body, out string name))
			{
				return null;
			}
			SplitQuery(ref body, out NameValueCollection query);
			int atIdx = body.LastIndexOf('@');
			if (atIdx < 0)
			{
				return null;
			}
			string password = Uri.UnescapeDataString(body.Substring(0, atIdx));
			string hostPort = body.Substring(atIdx + 1);
			if (string.IsNullOrEmpty(password) || !TryParseHostPort(hostPort, out string address, out int port))
			{
				return null;
			}
			return new VlessNode
			{
				Protocol = "anytls",
				Name = name,
				Address = address,
				Port = port,
				Password = password,
				Security = "tls",
				SNI = (query["sni"] ?? string.Empty),
				Alpn = (query["alpn"] ?? string.Empty),
				Fingerprint = (query["fp"] ?? "chrome"),
				AllowInsecure = IsTruthy(query["insecure"] ?? query["allowInsecure"])
			};
		}
		catch
		{
			return null;
		}
	}

	public VlessNode? ParseSshUri(string uri)
	{
		try
		{
			if (!TrySplitUri(uri, "ssh://", out string body, out string name))
			{
				return null;
			}
			SplitQuery(ref body, out NameValueCollection query);
			int atIdx = body.LastIndexOf('@');
			string userInfo = ((atIdx >= 0) ? Uri.UnescapeDataString(body.Substring(0, atIdx)) : string.Empty);
			string hostPort = ((atIdx >= 0) ? body.Substring(atIdx + 1) : body);
			if (!TryParseHostPort(hostPort, out string address, out int port))
			{
				return null;
			}
			int colon = userInfo.IndexOf(':');
			VlessNode node = new VlessNode
			{
				Protocol = "ssh",
				Name = name,
				Address = address,
				Port = port,
				Username = ((colon >= 0) ? userInfo.Substring(0, colon) : userInfo),
				Password = ((colon >= 0) ? userInfo.Substring(colon + 1) : string.Empty),
				Security = "none",
				Network = "tcp"
			};
			if (!string.IsNullOrWhiteSpace(query["private-key"]))
			{
				node.Extra["private-key"] = query["private-key"]!;
			}
			return node;
		}
		catch
		{
			return null;
		}
	}

	public VlessNode? ParseVlessUri(string uri)
	{
		try
		{
			if (!TrySplitUri(uri, "vless://", out string body, out string name))
			{
				return null;
			}
			SplitQuery(ref body, out NameValueCollection query);
			int atIdx = body.LastIndexOf('@');
			if (atIdx < 0)
			{
				return null;
			}
			string uuid = body.Substring(0, atIdx);
			string hostPort = body.Substring(atIdx + 1);
			if (string.IsNullOrWhiteSpace(uuid) || !TryParseHostPort(hostPort, out string address, out int port))
			{
				return null;
			}
			return new VlessNode
			{
				Protocol = "vless",
				Name = name,
				Address = address,
				Port = port,
				UUID = uuid,
				Flow = (query["flow"] ?? string.Empty),
				Security = (query["security"] ?? "none"),
				Network = (query["type"] ?? "tcp"),
				SNI = (query["sni"] ?? string.Empty),
				PublicKey = (query["pbk"] ?? string.Empty),
				ShortId = (query["sid"] ?? string.Empty),
				Fingerprint = (query["fp"] ?? "chrome"),
				Alpn = (query["alpn"] ?? string.Empty),
				Path = (query["path"] ?? query["serviceName"] ?? string.Empty),
				Host = (query["host"] ?? string.Empty),
				HeaderType = (query["headerType"] ?? string.Empty),
				SpiderX = (query["spx"] ?? string.Empty),
				Encryption = (query["encryption"] ?? "none"),
				Seed = (query["seed"] ?? string.Empty),
				Mode = (query["mode"] ?? string.Empty),
				Extra = ParseXhttpExtra(query),
				AllowInsecure = IsTruthy(query["allowInsecure"] ?? query["insecure"])
			};
		}
		catch
		{
			return null;
		}
	}

	public VlessNode? ParseVmessUri(string uri)
	{
		try
		{
			if (!uri.StartsWith("vmess://", StringComparison.OrdinalIgnoreCase))
			{
				return null;
			}
			string payload = uri.Substring(8).Trim();
			int fragmentIdx = payload.IndexOf('#');
			if (fragmentIdx >= 0)
			{
				payload = payload.Substring(0, fragmentIdx);
			}
			string? json = TryDecodeBase64Utf8(payload);
			if (string.IsNullOrWhiteSpace(json))
			{
				return null;
			}
			JObject obj = JObject.Parse(json);
			string address = (string?)obj["add"] ?? string.Empty;
			string id = (string?)obj["id"] ?? string.Empty;
			if (string.IsNullOrWhiteSpace(address) || string.IsNullOrWhiteSpace(id))
			{
				return null;
			}
			if (!int.TryParse(obj["port"]?.ToString(), out int port) || !IsValidPort(port))
			{
				return null;
			}
			string network = ((string?)obj["net"] ?? "tcp").ToLowerInvariant();
			string tls = ((string?)obj["tls"] ?? string.Empty).ToLowerInvariant();
			return new VlessNode
			{
				Protocol = "vmess",
				Name = ((string?)obj["ps"] ?? string.Empty),
				Address = address,
				Port = port,
				UUID = id,
				AlterId = (obj["aid"]?.ToString() ?? "0"),
				VmessSecurity = ((string?)obj["scy"] ?? "auto"),
				Network = network,
				HeaderType = ((string?)obj["type"] ?? string.Empty),
				Host = ((string?)obj["host"] ?? string.Empty),
				Path = ((string?)obj["path"] ?? string.Empty),
				Security = ((tls == "tls" || tls == "reality") ? tls : "none"),
				SNI = ((string?)obj["sni"] ?? string.Empty),
				Alpn = ((string?)obj["alpn"] ?? string.Empty),
				Fingerprint = (string.IsNullOrWhiteSpace((string?)obj["fp"]) ? "chrome" : ((string?)obj["fp"] ?? "chrome"))
			};
		}
		catch
		{
			return null;
		}
	}

	public VlessNode? ParseTrojanUri(string uri)
	{
		try
		{
			if (!TrySplitUri(uri, "trojan://", out string body, out string name))
			{
				return null;
			}
			SplitQuery(ref body, out NameValueCollection query);
			int atIdx = body.LastIndexOf('@');
			if (atIdx < 0)
			{
				return null;
			}
			string password = Uri.UnescapeDataString(body.Substring(0, atIdx));
			string hostPort = body.Substring(atIdx + 1);
			if (string.IsNullOrEmpty(password) || !TryParseHostPort(hostPort, out string address, out int port))
			{
				return null;
			}
			return new VlessNode
			{
				Protocol = "trojan",
				Name = name,
				Address = address,
				Port = port,
				Password = password,
				Security = (query["security"] ?? "tls"),
				Network = (query["type"] ?? "tcp"),
				SNI = (query["sni"] ?? string.Empty),
				Alpn = (query["alpn"] ?? string.Empty),
				Fingerprint = (query["fp"] ?? "chrome"),
				Path = (query["path"] ?? query["serviceName"] ?? string.Empty),
				Host = (query["host"] ?? string.Empty),
				HeaderType = (query["headerType"] ?? string.Empty),
				AllowInsecure = IsTruthy(query["allowInsecure"] ?? query["insecure"])
			};
		}
		catch
		{
			return null;
		}
	}

	public VlessNode? ParseShadowsocksUri(string uri)
	{
		try
		{
			if (!TrySplitUri(uri, "ss://", out string body, out string name))
			{
				return null;
			}
			int queryIdx = body.IndexOf('?');
			if (queryIdx >= 0)
			{
				body = body.Substring(0, queryIdx);
			}
			string method;
			string password;
			string hostPort;
			int atIdx = body.LastIndexOf('@');
			if (atIdx >= 0)
			{
				string userInfo = Uri.UnescapeDataString(body.Substring(0, atIdx));
				hostPort = body.Substring(atIdx + 1);
				string credentials = (userInfo.Contains(':') ? userInfo : (TryDecodeBase64Utf8(userInfo) ?? userInfo));
				int colon = credentials.IndexOf(':');
				if (colon < 0)
				{
					return null;
				}
				method = credentials.Substring(0, colon);
				password = credentials.Substring(colon + 1);
			}
			else
			{
				string? decoded = TryDecodeBase64Utf8(Uri.UnescapeDataString(body));
				if (decoded == null)
				{
					return null;
				}
				int credentialsIdx = decoded.LastIndexOf('@');
				if (credentialsIdx < 0)
				{
					return null;
				}
				string credentials = decoded.Substring(0, credentialsIdx);
				hostPort = decoded.Substring(credentialsIdx + 1);
				int colon = credentials.IndexOf(':');
				if (colon < 0)
				{
					return null;
				}
				method = credentials.Substring(0, colon);
				password = credentials.Substring(colon + 1);
			}
			if (string.IsNullOrWhiteSpace(method) || !TryParseHostPort(hostPort, out string address, out int port))
			{
				return null;
			}
			return new VlessNode
			{
				Protocol = "shadowsocks",
				Name = name,
				Address = address,
				Port = port,
				Method = method,
				Password = password,
				Network = "tcp",
				Security = "none"
			};
		}
		catch
		{
			return null;
		}
	}

	public VlessNode? ParseHysteria2Uri(string uri)
	{
		try
		{
			string scheme = uri.StartsWith("hysteria2://", StringComparison.OrdinalIgnoreCase) ? "hysteria2://" : "hy2://";
			if (!TrySplitUri(uri, scheme, out string body, out string name))
			{
				return null;
			}
			SplitQuery(ref body, out NameValueCollection query);
			int atIdx = body.LastIndexOf('@');
			string auth = ((atIdx >= 0) ? Uri.UnescapeDataString(body.Substring(0, atIdx)) : string.Empty);
			string hostPort = ((atIdx >= 0) ? body.Substring(atIdx + 1) : body);
			if (!TryParseHostPort(hostPort, out string address, out int port))
			{
				return null;
			}
			return new VlessNode
			{
				Protocol = "hysteria2",
				Name = name,
				Address = address,
				Port = port,
				Password = auth,
				Security = "tls",
				SNI = (query["sni"] ?? query["peer"] ?? string.Empty),
				Alpn = (query["alpn"] ?? string.Empty),
				Obfs = (query["obfs"] ?? string.Empty),
				ObfsPassword = (query["obfs-password"] ?? query["obfsPassword"] ?? string.Empty),
				PinnedPeerCertificateChainSha256 = (query["pinSHA256"] ?? query["pinSha256"] ?? string.Empty),
				ServerPorts = (query["mport"] ?? query["ports"] ?? string.Empty),
				HopInterval = (query["hopInterval"] ?? query["hop-interval"] ?? string.Empty),
				UpMbps = (query["up"] ?? string.Empty),
				DownMbps = (query["down"] ?? string.Empty),
				AllowInsecure = IsTruthy(query["insecure"] ?? query["allowInsecure"] ?? query["allow_insecure"])
			};
		}
		catch
		{
			return null;
		}
	}

	public VlessNode? ParseTuicUri(string uri)
	{
		try
		{
			if (!TrySplitUri(uri, "tuic://", out string body, out string name))
			{
				return null;
			}
			SplitQuery(ref body, out NameValueCollection query);
			int atIdx = body.LastIndexOf('@');
			if (atIdx < 0)
			{
				return null;
			}
			string userInfo = Uri.UnescapeDataString(body.Substring(0, atIdx));
			int colon = userInfo.IndexOf(':');
			string uuid = ((colon >= 0) ? userInfo.Substring(0, colon) : userInfo);
			string password = ((colon >= 0) ? userInfo.Substring(colon + 1) : string.Empty);
			string hostPort = body.Substring(atIdx + 1);
			if (string.IsNullOrWhiteSpace(uuid) || !TryParseHostPort(hostPort, out string address, out int port))
			{
				return null;
			}
			return new VlessNode
			{
				Protocol = "tuic",
				Name = name,
				Address = address,
				Port = port,
				UUID = uuid,
				Password = password,
				Security = "tls",
				SNI = (query["sni"] ?? string.Empty),
				Alpn = (query["alpn"] ?? string.Empty),
				CongestionControl = (query["congestion_control"] ?? query["congestion"] ?? string.Empty),
				UdpRelayMode = (query["udp_relay_mode"] ?? string.Empty),
				AllowInsecure = IsTruthy(query["allow_insecure"] ?? query["allowInsecure"] ?? query["insecure"])
			};
		}
		catch
		{
			return null;
		}
	}

	public VlessNode? ParseSocksUri(string uri)
	{
		try
		{
			if (!TrySplitUri(uri, "socks://", out string body, out string name))
			{
				return null;
			}
			SplitQuery(ref body, out NameValueCollection query);
			int atIdx = body.LastIndexOf('@');
			string userInfo = ((atIdx >= 0) ? Uri.UnescapeDataString(body.Substring(0, atIdx)) : string.Empty);
			string hostPort = ((atIdx >= 0) ? body.Substring(atIdx + 1) : body);
			if (!TryParseHostPort(hostPort, out string address, out int port))
			{
				return null;
			}
			int colon = userInfo.IndexOf(':');
			return new VlessNode
			{
				Protocol = "socks",
				Name = name,
				Address = address,
				Port = port,
				Username = ((colon >= 0) ? userInfo.Substring(0, colon) : userInfo),
				Password = ((colon >= 0) ? userInfo.Substring(colon + 1) : string.Empty),
				Security = "none"
			};
		}
		catch
		{
			return null;
		}
	}

	private static Dictionary<string, string> ParseXhttpExtra(NameValueCollection query)
	{
		Dictionary<string, string> extra = new Dictionary<string, string>();
		string value = query["extra"] ?? string.Empty;
		if (!string.IsNullOrWhiteSpace(value))
		{
			extra["xhttp-extra"] = value;
		}
		return extra;
	}

	private static bool IsSupportedUri(string value)
	{		foreach (string scheme in SupportedSchemes)
		{
			if (value.StartsWith(scheme, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}
		return false;
	}

	private static bool ContainsSupportedUri(string value)
	{
		foreach (string scheme in SupportedSchemes)
		{
			if (value.Contains(scheme, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}
		return false;
	}

	private static bool TrySplitUri(string uri, string scheme, out string body, out string name)
	{
		body = string.Empty;
		name = string.Empty;
		if (!uri.StartsWith(scheme, StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}
		string rest = uri.Substring(scheme.Length);
		int fragmentIdx = rest.LastIndexOf('#');
		if (fragmentIdx >= 0)
		{
			string fragment = rest.Substring(fragmentIdx + 1);
			try
			{
				name = Uri.UnescapeDataString(fragment);
			}
			catch
			{
				name = fragment;
			}
			rest = rest.Substring(0, fragmentIdx);
		}
		body = rest;
		return true;
	}

	private static void SplitQuery(ref string body, out NameValueCollection query)
	{
		string queryString = string.Empty;
		int queryIdx = body.IndexOf('?');
		if (queryIdx >= 0)
		{
			queryString = body.Substring(queryIdx + 1);
			body = body.Substring(0, queryIdx);
		}
		query = HttpUtility.ParseQueryString(queryString);
	}

	private static bool TryParseHostPort(string value, out string address, out int port)
	{
		address = string.Empty;
		port = 0;
		string hostPort = value;
		int pathIdx = hostPort.IndexOfAny(new char[3] { '/', '?', '#' });
		if (pathIdx >= 0)
		{
			hostPort = hostPort.Substring(0, pathIdx);
		}
		if (string.IsNullOrWhiteSpace(hostPort))
		{
			return false;
		}
		if (hostPort.StartsWith('['))
		{
			int closeBracket = hostPort.IndexOf(']');
			if (closeBracket < 0)
			{
				return false;
			}
			address = hostPort.Substring(1, closeBracket - 1);
			string portPart = hostPort.Substring(closeBracket + 1);
			if (!portPart.StartsWith(':'))
			{
				return false;
			}
			return int.TryParse(portPart.Substring(1), out port) && IsValidPort(port) && !string.IsNullOrWhiteSpace(address);
		}
		int lastColon = hostPort.LastIndexOf(':');
		if (lastColon < 0)
		{
			return false;
		}
		address = hostPort.Substring(0, lastColon);
		return !string.IsNullOrWhiteSpace(address) && int.TryParse(hostPort.Substring(lastColon + 1), out port) && IsValidPort(port);
	}

	private static bool IsValidPort(int port)
	{
		return port >= 1 && port <= 65535;
	}

	private static bool IsTruthy(string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return false;
		}
		return value.Trim().ToLowerInvariant() switch
		{
			"1" or "true" or "yes" or "on" => true,
			_ => false,
		};
	}

	private static string? TryBase64Decode(string input)
	{
		try
		{
			string? result = TryDecodeBase64Utf8(input);
			if (result != null)
			{
				string trimmed = result.TrimStart();
				if (ContainsSupportedUri(result) || trimmed.StartsWith('{') || trimmed.StartsWith('['))
				{
					return result;
				}
			}
			return null;
		}
		catch
		{
			return null;
		}
	}

	private static string? TryDecodeBase64Utf8(string input)
	{
		try
		{
			string sanitized = input.Trim().Replace('-', '+').Replace('_', '/');
			switch (sanitized.Length % 4)
			{
			case 2:
				sanitized += "==";
				break;
			case 3:
				sanitized += "=";
				break;
			}
			byte[] bytes = Convert.FromBase64String(sanitized);
			return Encoding.UTF8.GetString(bytes);
		}
		catch
		{
			return null;
		}
	}
}
