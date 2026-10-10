using System;
using System.Collections.Generic;
using System.Linq;
using ElyProxy.Models;
using Newtonsoft.Json.Linq;

namespace ElyProxy.Services;

public sealed class JsonConfigParser
{
	private const int MaxContentLength = 8 * 1024 * 1024;

	private const int MaxNodes = 10000;

	public List<VlessNode> Parse(string json, Func<string, VlessNode?>? uriParser = null)
	{
		List<VlessNode> nodes = new List<VlessNode>();
		if (string.IsNullOrWhiteSpace(json) || json.Length > MaxContentLength)
		{
			return nodes;
		}
		JToken root;
		try
		{
			root = JToken.Parse(json);
		}
		catch
		{
			return nodes;
		}
		switch (root)
		{
		case JArray array:
		{
			List<VlessNode> parsed = new List<VlessNode>();
			List<JObject> configItems = new List<JObject>();
			foreach (JToken item in array)
			{
				if (item.Type == JTokenType.String)
				{
					VlessNode? linkNode = uriParser?.Invoke(item.Value<string>() ?? string.Empty);
					if (linkNode != null)
					{
						parsed.Add(linkNode);
					}
				}
				else if (item is JObject itemObject)
				{
					if (itemObject["outbounds"] is JArray)
					{
						configItems.Add(itemObject);
					}
					else
					{
						AddObject(itemObject, parsed);
					}
				}
			}
			foreach (JObject configItem in configItems.OrderBy(CountProxyOutbounds))
			{
				AddConfigItem(configItem, parsed);
			}
			nodes.AddRange(parsed);
			break;
		}
		case JObject rootObject:
			if (rootObject["proxies"] is JArray proxies)
			{
				foreach (JObject proxy in proxies.OfType<JObject>())
				{
					VlessNode? node = ProxyNodeMapper.MapNode(ToDictionary(proxy));
					if (node != null)
					{
						nodes.Add(node);
					}
				}
			}
			else if (rootObject["outbounds"] is JArray)
			{
				AddConfigItem(rootObject, nodes);
			}
			else
			{
				AddObject(rootObject, nodes);
			}
			break;
		}
		return Deduplicate(nodes);
	}

	private static void AddConfigItem(JObject config, List<VlessNode> nodes)
	{
		if (config["outbounds"] is not JArray outbounds)
		{
			return;
		}
		List<JObject> outboundList = outbounds.OfType<JObject>().ToList();
		if (outboundList.Count == 0)
		{
			return;
		}
		bool xrayStyle = outboundList.Any(outbound => outbound["protocol"] != null);
		int proxyCount = outboundList.Count(IsProxyOutbound);
		string remarks = (((string?)config["remarks"] ?? (string?)config["ps"]) ?? string.Empty).Trim();
		if (remarks.StartsWith("base64:", StringComparison.OrdinalIgnoreCase))
		{
			remarks = string.Empty;
		}
		int before = nodes.Count;
		if (xrayStyle)
		{
			foreach (JObject outbound in outboundList)
			{
				if (nodes.Count >= MaxNodes)
				{
					break;
				}
				AddObject(outbound, nodes);
			}
		}
		else
		{
			AddSingBoxOutbounds(outboundList, nodes);
		}
		if (remarks.Length == 0)
		{
			return;
		}
		for (int index = before; index < nodes.Count; index++)
		{
			VlessNode added = nodes[index];
			added.Name = ((proxyCount > 1) ? (remarks + " · " + NodeLabel(added)) : remarks);
		}
	}

	private static int CountProxyOutbounds(JObject config)
	{
		return (config["outbounds"] is JArray outbounds) ? outbounds.OfType<JObject>().Count(IsProxyOutbound) : 0;
	}

	private static readonly string[] ProxyProtocols = new string[13]
	{
		"vless", "vmess", "trojan", "shadowsocks", "ss", "hysteria2", "hy2", "tuic", "anytls", "ssh", "wireguard", "naive", "shadowtls"
	};

	private static bool IsProxyOutbound(JObject outbound)
	{
		string protocol = (((string?)outbound["protocol"] ?? (string?)outbound["type"]) ?? string.Empty).ToLowerInvariant();
		return ProxyProtocols.Contains(protocol);
	}

	private static string NodeLabel(VlessNode node)
	{
		string address = (node.Address ?? string.Empty);
		int dot = address.IndexOf('.');
		return (dot > 0 ? address.Substring(0, dot) : address);
	}

	private static List<VlessNode> Deduplicate(List<VlessNode> nodes)
	{
		List<VlessNode> result = new List<VlessNode>(nodes.Count);
		HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (VlessNode node in nodes)
		{
			string key = string.Join(
				"|",
				node.Protocol ?? string.Empty,
				node.Address ?? string.Empty,
				node.Port,
				node.UUID ?? string.Empty,
				node.Password ?? string.Empty,
				node.Method ?? string.Empty,
				node.Network ?? string.Empty,
				node.Security ?? string.Empty,
				node.SNI ?? string.Empty,
				node.Host ?? string.Empty,
				node.Path ?? string.Empty);
			if (seen.Add(key))
			{
				result.Add(node);
			}
		}
		return result;
	}

	private static void AddObject(JObject obj, List<VlessNode> nodes)
	{
		if (nodes.Count >= MaxNodes)
		{
			return;
		}
		VlessNode? node = ((obj["protocol"] != null)
			? MapXrayOutbound(obj)
			: ((obj["server_port"] != null) ? MapSingBoxOutbound(obj) : ProxyNodeMapper.MapNode(ToDictionary(obj))));
		if (node != null)
		{
			nodes.Add(node);
		}
	}

	public static Dictionary<string, object?> ToDictionary(JObject obj)
	{
		Dictionary<string, object?> map = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
		foreach (KeyValuePair<string, JToken?> property in obj)
		{
			map[property.Key] = ToValue(property.Value);
		}
		return map;
	}

	private static object? ToValue(JToken? token)
	{
		return token switch
		{
			null => null,
			JObject obj => ToDictionary(obj),
			JArray array => array.Select(ToValue).ToList(),
			JValue value => value.Value,
			_ => token.ToString()
		};
	}

	private static VlessNode? MapXrayOutbound(JObject outbound)
	{
		string protocol = ((string?)outbound["protocol"] ?? string.Empty).ToLowerInvariant();
		if (protocol is not ("vless" or "vmess" or "trojan" or "shadowsocks" or "socks" or "http"))
		{
			return null;
		}
		JObject? stream = outbound["streamSettings"] as JObject;
		VlessNode node = new VlessNode
		{
			Protocol = protocol,
			Name = ((string?)outbound["tag"] ?? string.Empty),
			Network = "tcp",
			Security = "none"
		};
		if (protocol is "vless" or "vmess")
		{
			JObject? server = (outbound["settings"]?["vnext"] as JArray)?.OfType<JObject>().FirstOrDefault();
			JObject? user = (server?["users"] as JArray)?.OfType<JObject>().FirstOrDefault();
			if (server == null || user == null)
			{
				return null;
			}
			node.Address = ((string?)server["address"] ?? string.Empty);
			node.Port = ((int?)server["port"] ?? 0);
			node.UUID = ((string?)user["id"] ?? string.Empty);
			node.Flow = ((string?)user["flow"] ?? string.Empty);
			node.Encryption = ((string?)user["encryption"] ?? "none");
			node.AlterId = (user["alterId"]?.ToString() ?? "0");
			node.VmessSecurity = ((string?)user["security"] ?? "auto");
			if (string.IsNullOrWhiteSpace(node.UUID))
			{
				return null;
			}
		}
		else
		{
			JObject? server = (outbound["settings"]?["servers"] as JArray)?.OfType<JObject>().FirstOrDefault();
			if (server == null)
			{
				return null;
			}
			node.Address = ((string?)server["address"] ?? string.Empty);
			node.Port = ((int?)server["port"] ?? 0);
			node.Password = ((string?)server["password"] ?? string.Empty);
			node.Method = ((string?)server["method"] ?? string.Empty);
		}
		if (node.Port < 1 || node.Port > 65535 || string.IsNullOrWhiteSpace(node.Address))
		{
			return null;
		}
		ApplyXrayStream(node, stream);
		return node;
	}

	private static void ApplyXrayStream(VlessNode node, JObject? stream)
	{
		if (stream == null)
		{
			return;
		}
		node.Network = ((string?)stream["network"] ?? "tcp").ToLowerInvariant();
		node.Security = ((string?)stream["security"] ?? "none").ToLowerInvariant();
		JObject? tls = stream["tlsSettings"] as JObject;
		JObject? reality = stream["realitySettings"] as JObject;
		if (reality != null)
		{
			node.SNI = ((string?)reality["serverName"] ?? node.SNI);
			node.PublicKey = ((string?)reality["publicKey"] ?? string.Empty);
			node.ShortId = ((string?)reality["shortId"] ?? string.Empty);
			node.Fingerprint = ((string?)reality["fingerprint"] ?? node.Fingerprint);
			node.SpiderX = ((string?)reality["spiderX"] ?? string.Empty);
		}
		else if (tls != null)
		{
			node.SNI = ((string?)tls["serverName"] ?? node.SNI);
			node.AllowInsecure = ((bool?)tls["allowInsecure"] ?? false);
			node.Fingerprint = ((string?)tls["fingerprint"] ?? node.Fingerprint);
			node.Alpn = JoinArray(tls["alpn"]);
		}
		switch (node.Network)
		{
		case "ws":
			node.Path = ((string?)stream["wsSettings"]?["path"] ?? string.Empty);
			node.Host = ((string?)stream["wsSettings"]?["headers"]?["Host"] ?? string.Empty);
			break;
		case "grpc":
			node.Path = ((string?)stream["grpcSettings"]?["serviceName"] ?? string.Empty);
			break;
		case "h2":
		case "http":
			node.Path = ((string?)stream["httpSettings"]?["path"] ?? string.Empty);
			node.Host = JoinArray(stream["httpSettings"]?["host"]);
			break;
		case "httpupgrade":
			node.Path = ((string?)stream["httpupgradeSettings"]?["path"] ?? string.Empty);
			node.Host = ((string?)stream["httpupgradeSettings"]?["host"] ?? string.Empty);
			break;
		case "xhttp":
		case "splithttp":
			node.Network = "xhttp";
			node.Path = ((string?)stream["xhttpSettings"]?["path"] ?? string.Empty);
			node.Host = ((string?)stream["xhttpSettings"]?["host"] ?? string.Empty);
			node.Mode = ((string?)stream["xhttpSettings"]?["mode"] ?? string.Empty);
			break;
		case "kcp":
		case "mkcp":
			node.Network = "mkcp";
			JObject? kcp = stream["kcpSettings"] as JObject;
			node.Seed = ((string?)kcp?["seed"] ?? string.Empty);
			node.HeaderType = ((string?)kcp?["header"]?["type"] ?? string.Empty);
			if (kcp != null)
			{
				node.SetExtra("kcp-mtu", kcp["mtu"]?.ToString());
				node.SetExtra("kcp-tti", kcp["tti"]?.ToString());
				node.SetExtra("kcp-uplink", kcp["uplinkCapacity"]?.ToString());
				node.SetExtra("kcp-downlink", kcp["downlinkCapacity"]?.ToString());
				node.SetExtra("kcp-congestion", kcp["congestion"]?.ToString());
				node.SetExtra("kcp-read-buffer", kcp["readBufferSize"]?.ToString());
				node.SetExtra("kcp-write-buffer", kcp["writeBufferSize"]?.ToString());
			}
			break;
		case "tcp":
		case "raw":
			node.HeaderType = ((string?)(stream["tcpSettings"] ?? stream["rawSettings"])?["header"]?["type"] ?? string.Empty);
			break;
		}
	}

	private static void AddSingBoxOutbounds(List<JObject> outbounds, List<VlessNode> nodes)
	{
		HashSet<string> shadowTlsTags = new HashSet<string>(
			outbounds
				.Where(outbound => string.Equals((string?)outbound["type"], "shadowtls", StringComparison.OrdinalIgnoreCase))
				.Select(outbound => (string?)outbound["tag"] ?? string.Empty)
				.Where(tag => tag.Length > 0),
			StringComparer.OrdinalIgnoreCase);
		foreach (JObject outbound in outbounds)
		{
			string type = ((string?)outbound["type"] ?? string.Empty).ToLowerInvariant();
			if (type == "shadowsocks" && shadowTlsTags.Contains((string?)outbound["detour"] ?? string.Empty))
			{
				continue;
			}
			VlessNode? node = ((type == "shadowtls") ? MapShadowTlsOutbound(outbound, outbounds) : MapSingBoxOutbound(outbound));
			if (node != null)
			{
				nodes.Add(node);
			}
		}
	}

	private static VlessNode? MapShadowTlsOutbound(JObject shadowTls, List<JObject> outbounds)
	{
		string address = ((string?)shadowTls["server"] ?? string.Empty);
		int port = ((int?)shadowTls["server_port"] ?? 0);
		if (string.IsNullOrWhiteSpace(address) || port < 1 || port > 65535)
		{
			return null;
		}
		string tag = ((string?)shadowTls["tag"] ?? string.Empty);
		VlessNode node = new VlessNode
		{
			Protocol = "shadowtls",
			Name = tag,
			Address = address,
			Port = port,
			Password = ((string?)shadowTls["password"] ?? string.Empty),
			Security = "tls",
			Network = "tcp"
		};
		JObject? tls = shadowTls["tls"] as JObject;
		if (tls != null)
		{
			node.SNI = ((string?)tls["server_name"] ?? string.Empty);
			node.AllowInsecure = ((bool?)tls["insecure"] ?? false);
		}
		node.SetExtra("shadowtls-version", shadowTls["version"]?.ToString());
		JObject? inner = outbounds.FirstOrDefault(candidate =>
			string.Equals((string?)candidate["type"], "shadowsocks", StringComparison.OrdinalIgnoreCase)
			&& string.Equals((string?)candidate["detour"], tag, StringComparison.OrdinalIgnoreCase));
		if (inner != null)
		{
			node.SetExtra("ss-method", (string?)inner["method"]);
			node.SetExtra("ss-password", (string?)inner["password"]);
		}
		return node;
	}

	private static VlessNode? MapSingBoxOutbound(JObject outbound)
	{
		string type = ((string?)outbound["type"] ?? string.Empty).ToLowerInvariant();
		string address = ((string?)outbound["server"] ?? string.Empty);
		int port = ((int?)outbound["server_port"] ?? 0);
		if (string.IsNullOrWhiteSpace(address) || port < 1 || port > 65535)
		{
			return null;
		}
		VlessNode node = new VlessNode
		{
			Protocol = type,
			Name = ((string?)outbound["tag"] ?? string.Empty),
			Address = address,
			Port = port,
			Network = "tcp",
			Security = "none"
		};
		JObject? tls = outbound["tls"] as JObject;
		if (tls != null)
		{
			node.SNI = ((string?)tls["server_name"] ?? string.Empty);
			node.AllowInsecure = ((bool?)tls["insecure"] ?? false);
			node.Alpn = JoinArray(tls["alpn"]);
			node.Fingerprint = ((string?)tls["utls"]?["fingerprint"] ?? node.Fingerprint);
			JObject? reality = tls["reality"] as JObject;
			if (reality != null)
			{
				node.Security = "reality";
				node.PublicKey = ((string?)reality["public_key"] ?? string.Empty);
				node.ShortId = ((string?)reality["short_id"] ?? string.Empty);
			}
			else
			{
				node.Security = "tls";
			}
		}
		JObject? transport = outbound["transport"] as JObject;
		if (transport != null)
		{
			string transportType = ((string?)transport["type"] ?? string.Empty).ToLowerInvariant();
			node.Network = transportType switch
			{
				"http" => "h2",
				"httpupgrade" => "httpupgrade",
				"grpc" => "grpc",
				"ws" => "ws",
				"quic" => "quic",
				_ => transportType
			};
			node.Path = ((string?)transport["path"] ?? (string?)transport["service_name"] ?? string.Empty);
			node.Host = ((string?)transport["host"] ?? (string?)transport["headers"]?["Host"] ?? string.Empty);
		}
		switch (type)
		{
		case "vless":
			node.UUID = ((string?)outbound["uuid"] ?? string.Empty);
			node.Flow = ((string?)outbound["flow"] ?? string.Empty);
			break;
		case "vmess":
			node.UUID = ((string?)outbound["uuid"] ?? string.Empty);
			node.VmessSecurity = ((string?)outbound["security"] ?? "auto");
			node.AlterId = (outbound["alter_id"]?.ToString() ?? "0");
			break;
		case "trojan":
			node.Password = ((string?)outbound["password"] ?? string.Empty);
			break;
		case "shadowsocks":
			node.Method = ((string?)outbound["method"] ?? string.Empty);
			node.Password = ((string?)outbound["password"] ?? string.Empty);
			node.Security = "none";
			break;
		case "hysteria2":
			node.Password = ((string?)outbound["password"] ?? string.Empty);
			node.Obfs = ((string?)outbound["obfs"]?["type"] ?? string.Empty);
			node.ObfsPassword = ((string?)outbound["obfs"]?["password"] ?? string.Empty);
			node.ServerPorts = JoinArray(outbound["server_ports"]);
			node.HopInterval = ((string?)outbound["hop_interval"] ?? string.Empty);
			node.UpMbps = (outbound["up_mbps"]?.ToString() ?? string.Empty);
			node.DownMbps = (outbound["down_mbps"]?.ToString() ?? string.Empty);
			node.Security = "tls";
			break;
		case "tuic":
			node.UUID = ((string?)outbound["uuid"] ?? string.Empty);
			node.Password = ((string?)outbound["password"] ?? string.Empty);
			node.CongestionControl = ((string?)outbound["congestion_control"] ?? string.Empty);
			node.UdpRelayMode = ((string?)outbound["udp_relay_mode"] ?? string.Empty);
			node.Security = "tls";
			break;
		case "anytls":
			node.Password = ((string?)outbound["password"] ?? string.Empty);
			node.Security = "tls";
			break;
		case "naive":
			node.Username = ((string?)outbound["username"] ?? string.Empty);
			node.Password = ((string?)outbound["password"] ?? string.Empty);
			node.Security = "tls";
			break;
		case "socks":
		case "http":
			node.Username = ((string?)outbound["username"] ?? string.Empty);
			node.Password = ((string?)outbound["password"] ?? string.Empty);
			break;
		case "wireguard":
			node.Extra["private-key"] = ((string?)outbound["private_key"] ?? string.Empty);
			node.Extra["public-key"] = ((string?)outbound["peer_public_key"] ?? (string?)outbound["peer"]?["public_key"] ?? string.Empty);
			node.Extra["pre-shared-key"] = ((string?)outbound["pre_shared_key"] ?? (string?)outbound["peer"]?["pre_shared_key"] ?? string.Empty);
			node.Extra["allowed-ips"] = (JoinArray(outbound["allowed_ips"]) is { Length: > 0 } allowedIps
				? allowedIps
				: JoinArray(outbound["peer"]?["allowed_ips"]));
			node.Extra["address"] = (JoinArray(outbound["local_address"]) is { Length: > 0 } localAddress
				? localAddress
				: JoinArray(outbound["address"]));
			node.Extra["mtu"] = (outbound["mtu"]?.ToString() ?? string.Empty);
			node.Extra["reserved"] = (JoinArray(outbound["reserved"]) is { Length: > 0 } reserved
				? reserved
				: JoinArray(outbound["peer"]?["reserved"]));
			break;
		case "ssh":
			node.Username = ((string?)outbound["user"] ?? string.Empty);
			node.Password = ((string?)outbound["password"] ?? string.Empty);
			node.Extra["private-key"] = ((string?)outbound["private_key"] ?? string.Empty);
			break;
		default:
			return null;
		}
		return node;
	}

	private static string JoinArray(JToken? token)
	{
		if (token is JArray array)
		{
			return string.Join(",", array.Values<string>().Where(value => !string.IsNullOrWhiteSpace(value)));
		}
		return (token?.ToString() ?? string.Empty);
	}
}
