using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using ElyProxy.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ElyProxy.Core;

public sealed class SingBoxConfigBuilder
{
	private sealed class DirectRules
	{
		private static readonly string[] LocalRouteExclusions = new string[9] { "10.0.0.0/8", "100.64.0.0/10", "169.254.0.0/16", "172.16.0.0/12", "192.168.0.0/16", "224.0.0.0/4", "fc00::/7", "fe80::/10", "ff00::/8" };

		public HashSet<string> DomainSuffixes { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		public HashSet<string> FullDomains { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		public HashSet<string> DomainRegexes { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		public HashSet<string> DomainKeywords { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		public HashSet<string> IpCidrs { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		public HashSet<string> RouteExcludes { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		public DirectRules()
		{
			string[] localRouteExclusions = LocalRouteExclusions;
			foreach (string cidr in localRouteExclusions)
			{
				RouteExcludes.Add(cidr);
			}
		}

		public void Add(string? source)
		{
			string rule = source?.Trim();
			if (string.IsNullOrWhiteSpace(rule) || rule.StartsWith('#'))
			{
				return;
			}
			if (rule.StartsWith("domain:", StringComparison.OrdinalIgnoreCase))
			{
				string text = rule;
				AddDomainSuffix(text.Substring(7, text.Length - 7));
			}
			else if (rule.StartsWith("full:", StringComparison.OrdinalIgnoreCase))
			{
				HashSet<string> fullDomains = FullDomains;
				string text = rule;
				fullDomains.Add(text.Substring(5, text.Length - 5));
			}
			else if (rule.StartsWith("regexp:", StringComparison.OrdinalIgnoreCase))
			{
				HashSet<string> domainRegexes = DomainRegexes;
				string text = rule;
				domainRegexes.Add(text.Substring(7, text.Length - 7));
			}
			else if (rule.StartsWith("keyword:", StringComparison.OrdinalIgnoreCase))
			{
				HashSet<string> domainKeywords = DomainKeywords;
				string text = rule;
				domainKeywords.Add(text.Substring(8, text.Length - 8));
			}
			else
			{
				if (rule.StartsWith("geoip:", StringComparison.OrdinalIgnoreCase))
				{
					return;
				}
				if (rule.StartsWith("geosite:category-ru", StringComparison.OrdinalIgnoreCase))
				{
					string[] array = new string[5] { "ru", "xn--p1ai", "su", "com.ru", "edu.ru" };
					foreach (string suffix in array)
					{
						AddDomainSuffix(suffix);
					}
				}
				else
				{
					if (rule.StartsWith("geosite:", StringComparison.OrdinalIgnoreCase))
					{
						return;
					}
					if (IsIpOrCidr(rule))
					{
						AddIp(rule, excludeRoute: true);
						return;
					}
					string host = rule.TrimStart('.');
					if (host.StartsWith("*."))
					{
						string text = host;
						host = text.Substring(2, text.Length - 2);
					}
					if (!string.IsNullOrWhiteSpace(host) && !host.Contains('/') && !host.Contains(' '))
					{
						AddDomainSuffix(host);
					}
				}
			}
		}

		public void AddIp(string value, bool excludeRoute)
		{
			if (IsIpOrCidr(value))
			{
				IpCidrs.Add(value);
				if (excludeRoute)
				{
					RouteExcludes.Add(value);
				}
			}
		}

		private void AddDomainSuffix(string? source)
		{
			string host = source?.Trim().TrimStart('.');
			if (string.IsNullOrWhiteSpace(host))
			{
				return;
			}
			if (host.StartsWith("*."))
			{
				string text = host;
				host = text.Substring(2, text.Length - 2);
			}
			DomainSuffixes.Add(host.ToLowerInvariant());
			try
			{
				string ascii = new IdnMapping().GetAscii(host).ToLowerInvariant();
				DomainSuffixes.Add(ascii);
			}
			catch
			{
			}
		}
	}

	private const string RemoteDnsAddress = "1.1.1.1";

	private const string RemoteDnsServerName = "cloudflare-dns.com";

	public string BuildTun(VlessNode node, int socksPort, XrayTunOptions options)
	{
		bool useRoscomVpnDefault = options.RoutingPreset == RoutingPreset.RoscomVpnDefault;
		DirectRules direct = BuildDirectRules(options, !useRoscomVpnDefault);
		JArray routeRules = new JArray
		{
			new JObject
			{
				["inbound"] = new JArray("tun-in"),
				["action"] = "sniff"
			},
			new JObject
			{
				["protocol"] = new JArray("dns"),
				["action"] = "hijack-dns"
			}
		};
		AddProcessBypassRouteRule(routeRules, options.BypassProcessPaths);
		if (useRoscomVpnDefault)
		{
			AddRoscomVpnDefaultRouteRules(routeRules, direct);
		}
		else
		{
			AddCustomRouteRules(routeRules, direct);
		}
		JArray addresses = new JArray("172.19.0.1/30");
		if (options.IncludeIpv6)
		{
			addresses.Add("fdfe:dcba:9876::1/126");
		}
		JObject tun = new JObject
		{
			["type"] = "tun",
			["tag"] = "tun-in",
			["interface_name"] = (string.IsNullOrWhiteSpace(options.InterfaceName) ? "ElyTun" : options.InterfaceName),
			["address"] = addresses,
			["mtu"] = options.Mtu,
			["auto_route"] = true,
			["stack"] = "system"
		};
		if (direct.RouteExcludes.Count > 0)
		{
			tun["route_exclude_address"] = new JArray(direct.RouteExcludes);
		}
		return new JObject
		{
			["log"] = new JObject
			{
				["level"] = "warn",
				["timestamp"] = true
			},
			["dns"] = (useRoscomVpnDefault ? BuildRoscomVpnDefaultDns(options.BypassProcessPaths) : BuildDns(direct, options)),
			["inbounds"] = new JArray
			{
				new JObject
				{
					["type"] = "mixed",
					["tag"] = "mixed-in",
					["listen"] = "127.0.0.1",
					["listen_port"] = socksPort
				},
				tun
			},
			["outbounds"] = new JArray
			{
				BuildVlessOutbound(node),
				new JObject
				{
					["type"] = "direct",
					["tag"] = "direct"
				},
				new JObject
				{
					["type"] = "block",
					["tag"] = "block"
				}
			},
			["route"] = BuildRoute(routeRules, useRoscomVpnDefault)
		}.ToString(Formatting.Indented);
	}

	public string BuildSystemProxy(VlessNode node, int proxyPort, XrayTunOptions options)
	{
		bool useRoscomVpnDefault = options.RoutingPreset == RoutingPreset.RoscomVpnDefault;
		DirectRules direct = BuildDirectRules(options, !useRoscomVpnDefault);
		JArray routeRules = new JArray();
		AddProcessBypassRouteRule(routeRules, options.BypassProcessPaths);
		if (useRoscomVpnDefault)
		{
			AddRoscomVpnDefaultRouteRules(routeRules, direct);
		}
		else
		{
			AddCustomRouteRules(routeRules, direct);
		}
		return new JObject
		{
			["log"] = new JObject
			{
				["level"] = "warn",
				["timestamp"] = true
			},
			["dns"] = (useRoscomVpnDefault ? BuildRoscomVpnDefaultDns(options.BypassProcessPaths) : BuildDns(direct, options)),
			["inbounds"] = new JArray
			{
				new JObject
				{
					["type"] = "mixed",
					["tag"] = "system-proxy-in",
					["listen"] = "127.0.0.1",
					["listen_port"] = proxyPort
				}
			},
			["outbounds"] = new JArray
			{
				BuildVlessOutbound(node),
				new JObject
				{
					["type"] = "direct",
					["tag"] = "direct"
				},
				new JObject
				{
					["type"] = "block",
					["tag"] = "block"
				}
			},
			["route"] = BuildRoute(routeRules, useRoscomVpnDefault)
		}.ToString(Formatting.Indented);
	}

	private static JObject BuildRoute(JArray routeRules, bool useRoscomVpnDefault)
	{
		JObject route = new JObject
		{
			["auto_detect_interface"] = true,
			["default_domain_resolver"] = (useRoscomVpnDefault ? "dns-domestic" : "dns-direct"),
			["rules"] = routeRules,
			["final"] = "proxy"
		};
		if (useRoscomVpnDefault)
		{
			route["rule_set"] = new JArray(RoscomVpnDefaultRouting.RuleSets.Select((RoscomVpnDefaultRouting.RuleSetAsset ruleSet) => new JObject
			{
				["tag"] = ruleSet.Tag,
				["type"] = "local",
				["format"] = "binary",
				["path"] = RoscomVpnDefaultRouting.GetAssetPath(ruleSet.FileName)
			}));
		}
		return route;
	}

	private static void AddCustomRouteRules(JArray routeRules, DirectRules direct)
	{
		AddRouteRule(routeRules, "domain_suffix", direct.DomainSuffixes, "direct");
		AddRouteRule(routeRules, "domain", direct.FullDomains, "direct");
		AddRouteRule(routeRules, "domain_regex", direct.DomainRegexes, "direct");
		AddRouteRule(routeRules, "domain_keyword", direct.DomainKeywords, "direct");
		AddRouteRule(routeRules, "ip_cidr", direct.IpCidrs, "direct");
		routeRules.Add(new JObject
		{
			["ip_is_private"] = true,
			["outbound"] = "direct"
		});
	}

	private static void AddRoscomVpnDefaultRouteRules(JArray routeRules, DirectRules direct)
	{
		AddRuleSetRoute(routeRules, RoscomVpnDefaultRouting.BlockSiteTags, "block");
		AddRuleSetRoute(routeRules, RoscomVpnDefaultRouting.ProxySiteTags, "proxy");
		AddRuleSetRoute(routeRules, RoscomVpnDefaultRouting.DirectSiteTags, "direct");
		AddRuleSetRoute(routeRules, RoscomVpnDefaultRouting.DirectIpTags, "direct");
		AddRouteRule(routeRules, "ip_cidr", direct.IpCidrs, "direct");
		routeRules.Add(new JObject
		{
			["ip_is_private"] = true,
			["outbound"] = "direct"
		});
	}

	private static void AddProcessBypassRouteRule(JArray routeRules, IReadOnlyList<string>? processPaths)
	{
		string[] paths = NormalizeProcessPaths(processPaths);
		if (paths.Length == 0)
		{
			return;
		}

		routeRules.Add(new JObject
		{
			["process_path"] = new JArray(paths),
			["outbound"] = "direct"
		});
	}

	private static void AddRuleSetRoute(JArray rules, IEnumerable<string> tags, string outbound)
	{
		string[] items = tags.ToArray();
		if (items.Length != 0)
		{
			JObject jObject = new JObject();
			object[] content = items;
			jObject["rule_set"] = new JArray(content);
			jObject["outbound"] = outbound;
			rules.Add(jObject);
		}
	}

	private static JObject BuildDns(DirectRules direct, XrayTunOptions options)
	{
		IPAddress address2;
		string directDns = options.DnsServers.FirstOrDefault((string address) => IPAddress.TryParse(address, out address2)) ?? "1.1.1.1";
		JArray rules = new JArray();
		AddProcessDnsRule(rules, options.BypassProcessPaths, "dns-direct");
		AddDnsRule(rules, "domain_suffix", direct.DomainSuffixes);
		AddDnsRule(rules, "domain", direct.FullDomains);
		AddDnsRule(rules, "domain_regex", direct.DomainRegexes);
		AddDnsRule(rules, "domain_keyword", direct.DomainKeywords);
		return new JObject
		{
			["servers"] = new JArray
			{
				new JObject
				{
					["type"] = "udp",
					["tag"] = "dns-direct",
					["server"] = directDns,
					["server_port"] = 53
				},
				new JObject
				{
					["type"] = "https",
					["tag"] = "dns-remote",
					["server"] = "1.1.1.1",
					["server_port"] = 443,
					["path"] = "/dns-query",
					["headers"] = new JObject { ["Host"] = "cloudflare-dns.com" },
					["tls"] = new JObject
					{
						["enabled"] = true,
						["server_name"] = "cloudflare-dns.com"
					},
					["detour"] = "proxy"
				}
			},
			["rules"] = rules,
			["final"] = "dns-remote",
			["independent_cache"] = true
		};
	}

	private static JObject BuildRoscomVpnDefaultDns(IReadOnlyList<string>? processPaths)
	{
		string[] directTags = RoscomVpnDefaultRouting.DirectSiteTags;
		JObject jObject = new JObject();
		jObject["servers"] = new JArray
		{
			new JObject
			{
				["type"] = "hosts",
				["tag"] = "dns-hosts",
				["predefined"] = new JObject
				{
					["lkfl2.nalog.ru"] = "213.24.64.175",
					["lknpd.nalog.ru"] = "213.24.64.181"
				}
			},
			BuildDohServer("dns-domestic", "77.88.8.8", "common.dot.dns.yandex.net", null),
			BuildDohServer("dns-remote", "8.8.8.8", "dns.google", "proxy")
		};
		JArray jArray = new JArray
		{
			new JObject
			{
				["domain"] = new JArray("lkfl2.nalog.ru", "lknpd.nalog.ru"),
				["server"] = "dns-hosts"
			}
		};
		AddProcessDnsRule(jArray, processPaths, "dns-domestic");
		JObject jObject2 = new JObject();
		object[] content = directTags;
		jObject2["rule_set"] = new JArray(content);
		jObject2["server"] = "dns-domestic";
		jArray.Add(jObject2);
		jObject["rules"] = jArray;
		jObject["final"] = "dns-remote";
		jObject["independent_cache"] = true;
		return jObject;
	}

	private static void AddProcessDnsRule(JArray rules, IReadOnlyList<string>? processPaths, string server)
	{
		string[] paths = NormalizeProcessPaths(processPaths);
		if (paths.Length == 0)
		{
			return;
		}

		rules.Insert(0, new JObject
		{
			["process_path"] = new JArray(paths),
			["server"] = server
		});
	}

	private static string[] NormalizeProcessPaths(IReadOnlyList<string>? processPaths)
	{
		return (processPaths ?? Array.Empty<string>())
			.Select(path => path?.Trim().Trim('"'))
			.Where(path => !string.IsNullOrWhiteSpace(path))
			.Select(path => path!)
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToArray();
	}

	private static JObject BuildDohServer(string tag, string address, string serverName, string? detour)
	{
		JObject server = new JObject
		{
			["type"] = "https",
			["tag"] = tag,
			["server"] = address,
			["server_port"] = 443,
			["path"] = "/dns-query",
			["headers"] = new JObject { ["Host"] = serverName },
			["tls"] = new JObject
			{
				["enabled"] = true,
				["server_name"] = serverName
			}
		};
		if (!string.IsNullOrWhiteSpace(detour))
		{
			server["detour"] = detour;
		}
		return server;
	}

	private static JObject BuildVlessOutbound(VlessNode node)
	{
		JObject outbound = new JObject
		{
			["type"] = "vless",
			["tag"] = "proxy",
			["server"] = node.Address,
			["server_port"] = node.Port,
			["uuid"] = node.UUID
		};
		if (!string.IsNullOrWhiteSpace(node.Flow))
		{
			outbound["flow"] = node.Flow;
		}
		JObject tls = BuildTls(node);
		if (tls != null)
		{
			outbound["tls"] = tls;
		}
		JObject transport = BuildTransport(node);
		if (transport != null)
		{
			outbound["transport"] = transport;
		}
		return outbound;
	}

	private static JObject? BuildTls(VlessNode node)
	{
		string security = node.Security?.ToLowerInvariant();
		if ((!(security == "tls") && !(security == "reality")) || 1 == 0)
		{
			return null;
		}
		JObject tls = new JObject
		{
			["enabled"] = true,
			["server_name"] = node.SNI,
			["utls"] = new JObject
			{
				["enabled"] = true,
				["fingerprint"] = (string.IsNullOrWhiteSpace(node.Fingerprint) ? "chrome" : node.Fingerprint)
			}
		};
		if (security == "reality")
		{
			tls["reality"] = new JObject
			{
				["enabled"] = true,
				["public_key"] = node.PublicKey,
				["short_id"] = node.ShortId
			};
		}
		if (!string.IsNullOrWhiteSpace(node.Alpn))
		{
			tls["alpn"] = new JArray(from value in node.Alpn.Split(',')
				select value.Trim());
		}
		return tls;
	}

	private static JObject? BuildTransport(VlessNode node)
	{
		switch (node.Network?.ToLowerInvariant())
		{
		case "ws":
			return BuildWebSocketTransport(node);
		case "h2":
		case "http":
			return BuildHttpTransport(node);
		case "httpupgrade":
			return BuildHttpUpgradeTransport(node);
		case "grpc":
			return new JObject
			{
				["type"] = "grpc",
				["service_name"] = node.Path
			};
		case "quic":
			return new JObject { ["type"] = "quic" };
		case "tcp":
			if (string.Equals(node.HeaderType, "http", StringComparison.OrdinalIgnoreCase))
			{
				return BuildHttpTransport(node);
			}
			break;
		}
		return null;
	}

	private static JObject BuildHttpTransport(VlessNode node)
	{
		JObject transport = new JObject
		{
			["type"] = "http",
			["path"] = (string.IsNullOrWhiteSpace(node.Path) ? "/" : node.Path)
		};
		if (!string.IsNullOrWhiteSpace(node.Host))
		{
			object[] content = node.Host.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
			transport["host"] = new JArray(content);
		}
		return transport;
	}

	private static JObject BuildHttpUpgradeTransport(VlessNode node)
	{
		JObject transport = new JObject
		{
			["type"] = "httpupgrade",
			["path"] = (string.IsNullOrWhiteSpace(node.Path) ? "/" : node.Path)
		};
		if (!string.IsNullOrWhiteSpace(node.Host))
		{
			transport["host"] = node.Host;
		}
		return transport;
	}

	private static JObject BuildWebSocketTransport(VlessNode node)
	{
		JObject transport = new JObject
		{
			["type"] = "ws",
			["path"] = (string.IsNullOrWhiteSpace(node.Path) ? "/" : node.Path)
		};
		if (!string.IsNullOrWhiteSpace(node.Host))
		{
			transport["headers"] = new JObject { ["Host"] = node.Host };
		}
		return transport;
	}

	private static void AddRouteRule(JArray rules, string property, IEnumerable<string> values, string outbound)
	{
		string[] items = values.Distinct<string>(StringComparer.OrdinalIgnoreCase).ToArray();
		if (items.Length != 0)
		{
			JObject jObject = new JObject();
			object[] content = items;
			jObject[property] = new JArray(content);
			jObject["outbound"] = outbound;
			rules.Add(jObject);
		}
	}

	private static void AddDnsRule(JArray rules, string property, IEnumerable<string> values)
	{
		string[] items = values.Distinct<string>(StringComparer.OrdinalIgnoreCase).ToArray();
		if (items.Length != 0)
		{
			JObject jObject = new JObject();
			object[] content = items;
			jObject[property] = new JArray(content);
			jObject["server"] = "dns-direct";
			rules.Add(jObject);
		}
	}

	private static DirectRules BuildDirectRules(XrayTunOptions options, bool includeCustomBypassRules)
	{
		DirectRules result = new DirectRules();
		if (includeCustomBypassRules)
		{
			foreach (string rule in options.BypassRules)
			{
				result.Add(rule);
			}
		}
		foreach (string cidr in options.DirectIpCidrs)
		{
			result.AddIp(cidr, excludeRoute: true);
		}
		return result;
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
}
