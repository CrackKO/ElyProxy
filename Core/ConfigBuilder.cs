using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using ElyProxy.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ElyProxy.Core;

public class ConfigBuilder
{
	public string Build(VlessNode node, int socksPort = 1080, XrayTunOptions? tunOptions = null)
	{
		JArray inbounds = new JArray { BuildSocksInbound(socksPort) };
		if (tunOptions != null)
		{
			inbounds.Add(BuildTunInbound(tunOptions));
		}
		return new JObject
		{
			["log"] = new JObject { ["loglevel"] = "warning" },
			["inbounds"] = inbounds,
			["outbounds"] = BuildVlessOutbounds(node),
			["routing"] = BuildRouting(tunOptions)
		}.ToString(Formatting.Indented);
	}

	public string BuildSocks(SocksNode node, int localPort = 1080)
	{
		return new JObject
		{
			["log"] = new JObject { ["loglevel"] = "warning" },
			["inbounds"] = new JArray { BuildSocksInbound(localPort) },
			["outbounds"] = BuildSocksOutbounds(node),
			["routing"] = BuildRouting(null)
		}.ToString(Formatting.Indented);
	}

	private static JObject BuildSocksInbound(int port)
	{
		JObject jObject = new JObject();
		jObject["tag"] = "socks-in";
		jObject["port"] = port;
		jObject["listen"] = "127.0.0.1";
		jObject["protocol"] = "socks";
		jObject["settings"] = new JObject
		{
			["auth"] = "noauth",
			["udp"] = true
		};
		jObject["sniffing"] = new JObject
		{
			["enabled"] = true,
			["destOverride"] = new JArray("http", "tls")
		};
		return jObject;
	}

	private static JObject BuildTunInbound(XrayTunOptions options)
	{
		JArray gateway = new JArray("172.19.0.1/30");
		JArray routes = new JArray("0.0.0.0/0");
		if (options.IncludeIpv6)
		{
			gateway.Add("fdfe:dcba:9876::1/126");
			routes.Add("::/0");
		}
		JObject settings = new JObject
		{
			["name"] = (string.IsNullOrWhiteSpace(options.InterfaceName) ? "ElyTun" : options.InterfaceName),
			["mtu"] = options.Mtu,
			["gateway"] = gateway,
			["userLevel"] = 0,
			["autoSystemRoutingTable"] = routes,
			["autoOutboundsInterface"] = "auto"
		};
		if (options.DnsServers.Count > 0)
		{
			settings["dns"] = new JArray(options.DnsServers);
		}
		JObject jObject = new JObject();
		jObject["tag"] = "elytun-in";
		jObject["protocol"] = "tun";
		jObject["settings"] = settings;
		jObject["sniffing"] = new JObject
		{
			["enabled"] = true,
			["destOverride"] = new JArray("http", "tls", "quic")
		};
		return jObject;
	}

	private static JArray BuildVlessOutbounds(VlessNode node)
	{
		JObject user = new JObject
		{
			["id"] = node.UUID,
			["encryption"] = (string.IsNullOrEmpty(node.Encryption) ? "none" : node.Encryption)
		};
		if (!string.IsNullOrEmpty(node.Flow))
		{
			user["flow"] = node.Flow;
		}
		JObject vnext = new JObject
		{
			["address"] = node.Address,
			["port"] = node.Port,
			["users"] = new JArray(user)
		};
		JObject proxy = new JObject
		{
			["tag"] = "proxy",
			["protocol"] = "vless",
			["settings"] = new JObject { ["vnext"] = new JArray(vnext) },
			["streamSettings"] = BuildStreamSettings(node)
		};
		return new JArray
		{
			proxy,
			new JObject
			{
				["tag"] = "direct",
				["protocol"] = "freedom"
			},
			new JObject
			{
				["tag"] = "block",
				["protocol"] = "blackhole"
			}
		};
	}

	private static JArray BuildSocksOutbounds(SocksNode node)
	{
		JObject server = new JObject
		{
			["address"] = node.Address,
			["port"] = node.Port
		};
		if (!string.IsNullOrEmpty(node.Username))
		{
			server["users"] = new JArray(new JObject
			{
				["user"] = node.Username,
				["pass"] = node.Password
			});
		}
		JObject proxy = new JObject
		{
			["tag"] = "proxy",
			["protocol"] = "socks",
			["settings"] = new JObject { ["servers"] = new JArray(server) }
		};
		return new JArray
		{
			proxy,
			new JObject
			{
				["tag"] = "direct",
				["protocol"] = "freedom"
			},
			new JObject
			{
				["tag"] = "block",
				["protocol"] = "blackhole"
			}
		};
	}

	private static JObject BuildStreamSettings(VlessNode node)
	{
		string network = (string.IsNullOrEmpty(node.Network) ? "tcp" : node.Network.ToLowerInvariant());
		string security = (string.IsNullOrEmpty(node.Security) ? "none" : node.Security.ToLowerInvariant());
		JObject stream = new JObject
		{
			["network"] = network,
			["security"] = security
		};
		if (!(security == "reality"))
		{
			if (security == "tls")
			{
				stream["tlsSettings"] = BuildTlsSettings(node);
			}
		}
		else
		{
			stream["realitySettings"] = BuildRealitySettings(node);
		}
		switch (network)
		{
		case "ws":
			stream["wsSettings"] = BuildWsSettings(node);
			break;
		case "grpc":
			stream["grpcSettings"] = BuildGrpcSettings(node);
			break;
		case "tcp":
		{
			JObject tcpSettings = BuildTcpSettings(node);
			if (tcpSettings != null)
			{
				stream["tcpSettings"] = tcpSettings;
			}
			break;
		}
		case "h2":
		case "http":
			stream["httpSettings"] = BuildHttpSettings(node);
			break;
		}
		return stream;
	}

	private static JObject BuildRealitySettings(VlessNode node)
	{
		JObject obj = new JObject
		{
			["fingerprint"] = Fallback(node.Fingerprint, "chrome"),
			["serverName"] = node.SNI,
			["publicKey"] = node.PublicKey,
			["shortId"] = node.ShortId
		};
		if (!string.IsNullOrEmpty(node.SpiderX))
		{
			obj["spiderX"] = node.SpiderX;
		}
		return obj;
	}

	private static JObject BuildTlsSettings(VlessNode node)
	{
		JObject obj = new JObject
		{
			["serverName"] = node.SNI,
			["fingerprint"] = Fallback(node.Fingerprint, "chrome")
		};
		if (!string.IsNullOrEmpty(node.Alpn))
		{
			object[] content = (from a in node.Alpn.Split(',')
				select a.Trim()).ToArray();
			obj["alpn"] = new JArray(content);
		}
		return obj;
	}

	private static JObject BuildWsSettings(VlessNode node)
	{
		JObject obj = new JObject { ["path"] = Fallback(node.Path, "/") };
		if (!string.IsNullOrEmpty(node.Host))
		{
			obj["headers"] = new JObject { ["Host"] = node.Host };
		}
		return obj;
	}

	private static JObject BuildGrpcSettings(VlessNode node)
	{
		return new JObject
		{
			["serviceName"] = node.Path,
			["multiMode"] = false
		};
	}

	private static JObject? BuildTcpSettings(VlessNode node)
	{
		if (string.IsNullOrEmpty(node.HeaderType) || node.HeaderType == "none")
		{
			return null;
		}
		return new JObject { ["header"] = new JObject { ["type"] = node.HeaderType } };
	}

	private static JObject BuildHttpSettings(VlessNode node)
	{
		JObject obj = new JObject { ["path"] = Fallback(node.Path, "/") };
		if (!string.IsNullOrEmpty(node.Host))
		{
			object[] content = (from h in node.Host.Split(',')
				select h.Trim()).ToArray();
			obj["host"] = new JArray(content);
		}
		return obj;
	}

	private static JObject BuildRouting(XrayTunOptions? tunOptions)
	{
		JArray rules = new JArray();
		if (tunOptions != null)
		{
			AddTunBypassRules(rules, tunOptions);
			rules.Add(new JObject
			{
				["type"] = "field",
				["inboundTag"] = new JArray("elytun-in"),
				["port"] = 53,
				["network"] = "tcp,udp",
				["outboundTag"] = "direct"
			});
		}
		rules.Add(new JObject
		{
			["type"] = "field",
			["outboundTag"] = "direct",
			["domain"] = new JArray("geosite:private")
		});
		rules.Add(new JObject
		{
			["type"] = "field",
			["outboundTag"] = "direct",
			["ip"] = new JArray("geoip:private")
		});
		return new JObject
		{
			["domainStrategy"] = "AsIs",
			["rules"] = rules
		};
	}

	private static void AddTunBypassRules(JArray rules, XrayTunOptions options)
	{
		HashSet<string> domains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		HashSet<string> ips = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (string bypassRule in options.BypassRules)
		{
			AddBypassRule(bypassRule, domains, ips);
		}
		foreach (string cidr in options.DirectIpCidrs)
		{
			if (IsIpOrCidr(cidr))
			{
				ips.Add(cidr);
			}
		}
		if (domains.Count > 0)
		{
			rules.Add(new JObject
			{
				["type"] = "field",
				["inboundTag"] = new JArray("elytun-in"),
				["domain"] = new JArray(domains),
				["outboundTag"] = "direct"
			});
		}
		if (ips.Count > 0)
		{
			rules.Add(new JObject
			{
				["type"] = "field",
				["inboundTag"] = new JArray("elytun-in"),
				["ip"] = new JArray(ips),
				["outboundTag"] = "direct"
			});
		}
	}

	private static void AddBypassRule(string? source, ISet<string> domains, ISet<string> ips)
	{
		string rule = source?.Trim();
		if (string.IsNullOrWhiteSpace(rule) || rule.StartsWith('#'))
		{
			return;
		}
		if (rule.StartsWith("geoip:", StringComparison.OrdinalIgnoreCase))
		{
			ips.Add(rule.ToLowerInvariant());
			return;
		}
		if (rule.StartsWith("geosite:", StringComparison.OrdinalIgnoreCase) || rule.StartsWith("domain:", StringComparison.OrdinalIgnoreCase) || rule.StartsWith("full:", StringComparison.OrdinalIgnoreCase) || rule.StartsWith("regexp:", StringComparison.OrdinalIgnoreCase) || rule.StartsWith("keyword:", StringComparison.OrdinalIgnoreCase))
		{
			domains.Add(rule);
			return;
		}
		if (IsIpOrCidr(rule))
		{
			ips.Add(rule);
			return;
		}
		string host = rule.Trim().TrimStart('.');
		if (host.StartsWith("*."))
		{
			string text = host;
			host = text.Substring(2, text.Length - 2);
		}
		if (string.IsNullOrWhiteSpace(host) || host.Contains('/') || host.Contains(' '))
		{
			return;
		}
		foreach (string domainVariant in GetDomainVariants(host))
		{
			string escaped = Regex.Escape(domainVariant).Replace("\\*", ".*");
			domains.Add("regexp:(^|\\.)" + escaped + "$");
		}
	}

	private static IReadOnlyList<string> GetDomainVariants(string host)
	{
		List<string> variants = new List<string> { host.ToLowerInvariant() };
		try
		{
			string ascii = new IdnMapping().GetAscii(host).ToLowerInvariant();
			if (!string.Equals(ascii, host, StringComparison.OrdinalIgnoreCase))
			{
				variants.Add(ascii);
			}
		}
		catch
		{
		}
		return variants;
	}

	private static bool IsIpOrCidr(string value)
	{
		int slash = value.LastIndexOf('/');
		if (!IPAddress.TryParse((slash >= 0) ? value.Substring(0, slash) : value, out IPAddress address))
		{
			return false;
		}
		if (slash < 0)
		{
			return true;
		}
		int num = slash + 1;
		if (int.TryParse(value.Substring(num, value.Length - num), out var prefix) && prefix >= 0)
		{
			return prefix <= address.GetAddressBytes().Length * 8;
		}
		return false;
	}

	private static string Fallback(string? value, string fallback)
	{
		if (!string.IsNullOrEmpty(value))
		{
			return value;
		}
		return fallback;
	}
}
