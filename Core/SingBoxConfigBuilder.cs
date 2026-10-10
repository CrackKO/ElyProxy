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
		bool onlySelectedApps = options.PerAppMode == PerAppMode.OnlySelected;
		bool useRoscomVpnDefault = options.RoutingPreset == RoutingPreset.RoscomVpnDefault && !onlySelectedApps;
		DirectRules direct = BuildDirectRules(options, includeCustomBypassRules: true);
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
		AddProcessBypassRouteRule(routeRules, options.BypassProcessPaths, onlySelectedApps ? "proxy" : "direct");
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
		JObject root = new JObject
		{
			["log"] = new JObject
			{
				["level"] = "warn",
				["timestamp"] = true
			},
			["dns"] = (useRoscomVpnDefault ? BuildRoscomVpnDefaultDns(direct, options.BypassProcessPaths) : BuildDns(direct, options)),
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
			["outbounds"] = BuildOutbounds(node, options),
			["route"] = BuildRoute(routeRules, useRoscomVpnDefault, onlySelectedApps ? "direct" : "proxy")
		};
		return BuildRoot(root, node, options);
	}

	public string BuildSystemProxy(VlessNode node, int proxyPort, XrayTunOptions options)
	{
		bool onlySelectedApps = options.PerAppMode == PerAppMode.OnlySelected;
		bool useRoscomVpnDefault = options.RoutingPreset == RoutingPreset.RoscomVpnDefault && !onlySelectedApps;
		DirectRules direct = BuildDirectRules(options, includeCustomBypassRules: true);
		JArray routeRules = new JArray();
		AddProcessBypassRouteRule(routeRules, options.BypassProcessPaths, onlySelectedApps ? "proxy" : "direct");
		if (useRoscomVpnDefault)
		{
			AddRoscomVpnDefaultRouteRules(routeRules, direct);
		}
		else
		{
			AddCustomRouteRules(routeRules, direct);
		}
		JObject root = new JObject
		{
			["log"] = new JObject
			{
				["level"] = "warn",
				["timestamp"] = true
			},
			["dns"] = (useRoscomVpnDefault ? BuildRoscomVpnDefaultDns(direct, options.BypassProcessPaths) : BuildDns(direct, options)),
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
			["outbounds"] = BuildOutbounds(node, options),
			["route"] = BuildRoute(routeRules, useRoscomVpnDefault, onlySelectedApps ? "direct" : "proxy")
		};
		return BuildRoot(root, node, options);
	}

	private static JObject BuildRoute(JArray routeRules, bool useRoscomVpnDefault, string finalOutbound)
	{
		JObject route = new JObject
		{
			["auto_detect_interface"] = true,
			["default_domain_resolver"] = (useRoscomVpnDefault ? "dns-domestic" : "dns-direct"),
			["rules"] = routeRules,
			["final"] = finalOutbound
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
		AddRouteRule(routeRules, "domain_suffix", direct.DomainSuffixes, "direct");
		AddRouteRule(routeRules, "domain", direct.FullDomains, "direct");
		AddRouteRule(routeRules, "domain_regex", direct.DomainRegexes, "direct");
		AddRouteRule(routeRules, "domain_keyword", direct.DomainKeywords, "direct");
		AddRouteRule(routeRules, "ip_cidr", direct.IpCidrs, "direct");
		AddRuleSetRoute(routeRules, RoscomVpnDefaultRouting.BlockSiteTags, "block");
		AddRuleSetRoute(routeRules, RoscomVpnDefaultRouting.ProxySiteTags, "proxy");
		AddRuleSetRoute(routeRules, RoscomVpnDefaultRouting.DirectSiteTags, "direct");
		AddRuleSetRoute(routeRules, RoscomVpnDefaultRouting.DirectIpTags, "direct");
		routeRules.Add(new JObject
		{
			["ip_is_private"] = true,
			["outbound"] = "direct"
		});
	}

	private static void AddProcessBypassRouteRule(JArray routeRules, IReadOnlyList<string>? processPaths, string outbound)
	{
		string[] paths = NormalizeProcessPaths(processPaths);
		if (paths.Length == 0)
		{
			return;
		}

		routeRules.Add(new JObject
		{
			["process_path"] = new JArray(paths),
			["outbound"] = outbound
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
		bool onlySelectedApps = options.PerAppMode == PerAppMode.OnlySelected;
		JArray rules = new JArray();
		AddProcessDnsRule(rules, options.BypassProcessPaths, onlySelectedApps ? "dns-remote" : "dns-direct");
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
			["final"] = (onlySelectedApps ? "dns-direct" : "dns-remote")
		};
	}

	private static JObject BuildRoscomVpnDefaultDns(DirectRules direct, IReadOnlyList<string>? processPaths)
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
		AddDnsRule(jArray, "domain_suffix", direct.DomainSuffixes, "dns-domestic");
		AddDnsRule(jArray, "domain", direct.FullDomains, "dns-domestic");
		AddDnsRule(jArray, "domain_regex", direct.DomainRegexes, "dns-domestic");
		AddDnsRule(jArray, "domain_keyword", direct.DomainKeywords, "dns-domestic");
		AddProcessDnsRule(jArray, processPaths, "dns-domestic");
		JObject jObject2 = new JObject();
		object[] content = directTags;
		jObject2["rule_set"] = new JArray(content);
		jObject2["server"] = "dns-domestic";
		jArray.Add(jObject2);
		jObject["rules"] = jArray;
		jObject["final"] = "dns-remote";
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

	private static JObject BuildProxyOutbound(VlessNode node, XrayTunOptions options)
	{
		string protocol = (node.Protocol ?? "vless").ToLowerInvariant();
		return protocol switch
		{
			"vless" => BuildVlessOutbound(node, options),
			"vmess" => BuildVmessOutbound(node, options),
			"trojan" => BuildTrojanOutbound(node, options),
			"shadowsocks" or "ss" => BuildShadowsocksOutbound(node),
			"hysteria2" or "hy2" => BuildHysteria2Outbound(node),
			"tuic" => BuildTuicOutbound(node),
			"anytls" => BuildAnyTlsOutbound(node, options),
			"naive" => BuildNaiveOutbound(node),
			"ssh" => BuildSshOutbound(node),
			"socks" or "http" => BuildSocksOutbound(node, options),
			_ => throw new NotSupportedException($"Протокол {node.ProtocolDisplay} не поддерживается ядром sing-box.")
		};
	}

	private static JObject BuildAnyTlsOutbound(VlessNode node, XrayTunOptions options)
	{
		JObject outbound = new JObject
		{
			["type"] = "anytls",
			["tag"] = "proxy",
			["server"] = node.Address,
			["server_port"] = node.Port,
			["password"] = node.Password
		};
		JObject tls = BuildTls(node, options);
		if (tls != null)
		{
			outbound["tls"] = tls;
		}
		return outbound;
	}

	private static JObject BuildSshOutbound(VlessNode node)
	{
		JObject outbound = new JObject
		{
			["type"] = "ssh",
			["tag"] = "proxy",
			["server"] = node.Address,
			["server_port"] = node.Port
		};
		if (!string.IsNullOrWhiteSpace(node.Username))
		{
			outbound["user"] = node.Username;
		}
		if (!string.IsNullOrWhiteSpace(node.Password))
		{
			outbound["password"] = node.Password;
		}
		if (node.Extra.TryGetValue("private-key", out string? privateKey) && !string.IsNullOrWhiteSpace(privateKey))
		{
			outbound["private_key"] = privateKey;
		}
		if (node.Extra.TryGetValue("private-key-passphrase", out string? passphrase) && !string.IsNullOrWhiteSpace(passphrase))
		{
			outbound["private_key_passphrase"] = passphrase;
		}
		return outbound;
	}

	private static JObject BuildVlessOutbound(VlessNode node, XrayTunOptions options)
	{
		if (!string.IsNullOrWhiteSpace(node.Encryption) && !string.Equals(node.Encryption, "none", StringComparison.OrdinalIgnoreCase))
		{
			throw new NotSupportedException("sing-box не поддерживает VLESS Encryption (пост-квантовое шифрование). Выберите ядро Xray.");
		}
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
		JObject tls = BuildTls(node, options);
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

	private static JObject BuildVmessOutbound(VlessNode node, XrayTunOptions options)
	{
		int alterId = 0;
		int.TryParse(node.AlterId, out alterId);
		JObject outbound = new JObject
		{
			["type"] = "vmess",
			["tag"] = "proxy",
			["server"] = node.Address,
			["server_port"] = node.Port,
			["uuid"] = node.UUID,
			["security"] = (string.IsNullOrWhiteSpace(node.VmessSecurity) ? "auto" : node.VmessSecurity)
		};
		if (alterId > 0)
		{
			outbound["alter_id"] = alterId;
		}
		JObject tls = BuildTls(node, options);
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

	private static JObject BuildTrojanOutbound(VlessNode node, XrayTunOptions options)
	{
		JObject outbound = new JObject
		{
			["type"] = "trojan",
			["tag"] = "proxy",
			["server"] = node.Address,
			["server_port"] = node.Port,
			["password"] = node.Password
		};
		JObject tls = BuildTls(node, options);
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

	private static JObject BuildShadowsocksOutbound(VlessNode node)
	{
		return new JObject
		{
			["type"] = "shadowsocks",
			["tag"] = "proxy",
			["server"] = node.Address,
			["server_port"] = node.Port,
			["method"] = node.Method,
			["password"] = node.Password
		};
	}

	private static JObject BuildHysteria2Outbound(VlessNode node)
	{
		JObject outbound = new JObject
		{
			["type"] = "hysteria2",
			["tag"] = "proxy",
			["server"] = node.Address,
			["server_port"] = node.Port,
			["password"] = node.Password
		};
		if (!string.IsNullOrWhiteSpace(node.Obfs))
		{
			outbound["obfs"] = new JObject
			{
				["type"] = node.Obfs,
				["password"] = node.ObfsPassword
			};
		}
		string[] serverPorts = ParseServerPorts(node.ServerPorts);
		if (serverPorts.Length > 0)
		{
			outbound["server_ports"] = new JArray(serverPorts);
		}
		if (int.TryParse(node.UpMbps, out int upMbps) && upMbps > 0)
		{
			outbound["up_mbps"] = upMbps;
		}
		if (int.TryParse(node.DownMbps, out int downMbps) && downMbps > 0)
		{
			outbound["down_mbps"] = downMbps;
		}
		string hopInterval = NormalizeDuration(node.HopInterval);
		if (!string.IsNullOrWhiteSpace(hopInterval))
		{
			outbound["hop_interval"] = hopInterval;
		}
		outbound["tls"] = BuildQuicTls(node, "h3");
		return outbound;
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

	private static JObject BuildTuicOutbound(VlessNode node)
	{
		JObject outbound = new JObject
		{
			["type"] = "tuic",
			["tag"] = "proxy",
			["server"] = node.Address,
			["server_port"] = node.Port,
			["uuid"] = node.UUID,
			["password"] = node.Password
		};
		if (!string.IsNullOrWhiteSpace(node.CongestionControl))
		{
			outbound["congestion_control"] = node.CongestionControl;
		}
		if (!string.IsNullOrWhiteSpace(node.UdpRelayMode))
		{
			outbound["udp_relay_mode"] = node.UdpRelayMode;
		}
		outbound["tls"] = BuildQuicTls(node, "h3");
		return outbound;
	}

	private static JObject BuildSocksOutbound(VlessNode node, XrayTunOptions options)
	{
		bool isHttp = string.Equals(node.Protocol, "http", StringComparison.OrdinalIgnoreCase);
		JObject outbound = new JObject
		{
			["type"] = (isHttp ? "http" : "socks"),
			["tag"] = "proxy",
			["server"] = node.Address,
			["server_port"] = node.Port
		};
		if (isHttp)
		{
			if (!string.IsNullOrWhiteSpace(node.Username))
			{
				outbound["username"] = node.Username;
				outbound["password"] = node.Password;
			}
			JObject tls = BuildTls(node, options);
			if (tls != null)
			{
				outbound["tls"] = tls;
			}
		}
		else
		{
			outbound["version"] = "5";
			if (!string.IsNullOrWhiteSpace(node.Username))
			{
				outbound["username"] = node.Username;
				outbound["password"] = node.Password;
			}
		}
		return outbound;
	}

	private static JArray BuildOutbounds(VlessNode node, XrayTunOptions options)
	{
		JArray outbounds = new JArray();
		string protocol = (node.Protocol ?? "vless").ToLowerInvariant();
		if (protocol == "shadowtls")
		{
			outbounds.Add(BuildShadowTlsInnerOutbound(node));
			outbounds.Add(BuildShadowTlsOutbound(node));
		}
		else if (!IsWireGuard(node))
		{
			outbounds.Add(BuildProxyOutbound(node, options));
		}
		outbounds.Add(new JObject
		{
			["type"] = "direct",
			["tag"] = "direct"
		});
		outbounds.Add(new JObject
		{
			["type"] = "block",
			["tag"] = "block"
		});
		return outbounds;
	}

	private static JObject BuildShadowTlsInnerOutbound(VlessNode node)
	{
		string method = node.GetExtra("ss-method");
		string password = node.GetExtra("ss-password");
		if (string.IsNullOrWhiteSpace(method) || string.IsNullOrWhiteSpace(password))
		{
			throw new NotSupportedException("Для ShadowTLS нужны параметры Shadowsocks (method и password). Импортируйте узел из sing-box JSON с цепочкой shadowsocks → shadowtls.");
		}
		return new JObject
		{
			["type"] = "shadowsocks",
			["tag"] = "proxy",
			["server"] = node.Address,
			["server_port"] = node.Port,
			["method"] = method,
			["password"] = password,
			["detour"] = "proxy-shadowtls"
		};
	}

	private static JObject BuildShadowTlsOutbound(VlessNode node)
	{
		int version = 3;
		if (!int.TryParse(node.GetExtra("shadowtls-version"), out version) || version < 1 || version > 3)
		{
			version = 3;
		}
		JObject tls = new JObject
		{
			["enabled"] = true,
			["server_name"] = node.SNI
		};
		if (node.AllowInsecure)
		{
			tls["insecure"] = true;
		}
		return new JObject
		{
			["type"] = "shadowtls",
			["tag"] = "proxy-shadowtls",
			["server"] = node.Address,
			["server_port"] = node.Port,
			["version"] = version,
			["password"] = node.Password,
			["tls"] = tls
		};
	}

	private static JObject BuildNaiveOutbound(VlessNode node)
	{
		JObject tls = new JObject
		{
			["enabled"] = true,
			["server_name"] = node.SNI
		};
		if (node.AllowInsecure)
		{
			tls["insecure"] = true;
		}
		if (!string.IsNullOrWhiteSpace(node.Alpn))
		{
			tls["alpn"] = new JArray(from value in node.Alpn.Split(',')
				select value.Trim());
		}
		return new JObject
		{
			["type"] = "naive",
			["tag"] = "proxy",
			["server"] = node.Address,
			["server_port"] = node.Port,
			["username"] = node.Username,
			["password"] = node.Password,
			["tls"] = tls
		};
	}

	private static bool IsWireGuard(VlessNode node)
	{
		return string.Equals(node.Protocol, "wireguard", StringComparison.OrdinalIgnoreCase);
	}

	private static JArray? BuildEndpointsOrNull(VlessNode node)
	{
		if (!IsWireGuard(node))
		{
			return null;
		}
		JObject peer = new JObject
		{
			["address"] = node.Address,
			["port"] = node.Port,
			["public_key"] = ExtraValue(node, "public-key"),
			["allowed_ips"] = new JArray(SplitList(ExtraValue(node, "allowed-ips"), new string[2] { "0.0.0.0/0", "::/0" }))
		};
		string preSharedKey = ExtraValue(node, "pre-shared-key");
		if (!string.IsNullOrWhiteSpace(preSharedKey))
		{
			peer["pre_shared_key"] = preSharedKey;
		}
		JArray? reserved = ParseIntArray(ExtraValue(node, "reserved"));
		if (reserved != null)
		{
			peer["reserved"] = reserved;
		}
		JObject endpoint = new JObject
		{
			["type"] = "wireguard",
			["tag"] = "proxy",
			["system"] = false,
			["address"] = new JArray(SplitList(ExtraValue(node, "address"), new string[1] { "172.16.0.2/32" })),
			["private_key"] = ExtraValue(node, "private-key"),
			["peers"] = new JArray(peer)
		};
		if (int.TryParse(ExtraValue(node, "mtu"), out int mtu) && mtu > 0)
		{
			endpoint["mtu"] = mtu;
		}
		return new JArray(endpoint);
	}

	private static string ExtraValue(VlessNode node, string key)
	{
		return (node.Extra.TryGetValue(key, out string? value) ? (value ?? string.Empty) : string.Empty);
	}

	private static string[] SplitList(string value, params string[] fallback)
	{
		string[] items = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		return ((items.Length != 0) ? items : fallback);
	}

	private static JArray? ParseIntArray(string value)
	{
		string[] items = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		if (items.Length == 0)
		{
			return null;
		}
		JArray array = new JArray();
		foreach (string item in items)
		{
			if (!int.TryParse(item, out int parsed))
			{
				return null;
			}
			array.Add(parsed);
		}
		return array;
	}

	private static string BuildRoot(JObject root, VlessNode node, XrayTunOptions options)
	{
		JArray? endpoints = BuildEndpointsOrNull(node);
		if (endpoints != null)
		{
			root["endpoints"] = endpoints;
		}
		if (options.ClashApiPort > 0)
		{
			root["experimental"] = new JObject
			{
				["clash_api"] = new JObject
				{
					["external_controller"] = ("127.0.0.1:" + options.ClashApiPort),
					["secret"] = options.ClashApiSecret
				}
			};
		}
		return root.ToString(Formatting.Indented);
	}

	private static JObject BuildQuicTls(VlessNode node, string defaultAlpn)
	{
		JObject tls = new JObject
		{
			["enabled"] = true,
			["server_name"] = node.SNI
		};
		if (node.AllowInsecure)
		{
			tls["insecure"] = true;
		}
		if (!string.IsNullOrWhiteSpace(node.Alpn))
		{
			tls["alpn"] = new JArray(from value in node.Alpn.Split(',')
				select value.Trim());
		}
		else if (!string.IsNullOrWhiteSpace(defaultAlpn))
		{
			tls["alpn"] = new JArray(defaultAlpn);
		}
		return tls;
	}

	private static string[] ParseServerPorts(string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return Array.Empty<string>();
		}
		return value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
	}

	private static JObject? BuildTls(VlessNode node, XrayTunOptions options)
	{
		string security = node.Security?.ToLowerInvariant();
		if (!(security == "tls") && !(security == "reality"))
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
		if (node.AllowInsecure)
		{
			tls["insecure"] = true;
		}
		if (security == "reality")
		{
			tls["reality"] = new JObject
			{
				["enabled"] = true,
				["public_key"] = node.PublicKey,
				["short_id"] = node.ShortId
			};
		}
		if (options is { FragmentEnabled: true })
		{
			tls["fragment"] = true;
			tls["fragment_fallback_delay"] = FragmentFallbackDelay(options.FragmentInterval);
			if (options.RecordFragment)
			{
				tls["record_fragment"] = true;
			}
		}
		if (security == "tls" && options is { TlsSpoofEnabled: true } && !string.IsNullOrWhiteSpace(options.TlsSpoofSni))
		{
			tls["spoof"] = options.TlsSpoofSni;
			tls["spoof_method"] = (string.IsNullOrWhiteSpace(options.TlsSpoofMethod) ? "wrong-sequence" : options.TlsSpoofMethod);
		}
		if (!string.IsNullOrWhiteSpace(node.Alpn))
		{
			tls["alpn"] = new JArray(from value in node.Alpn.Split(',')
				select value.Trim());
		}
		return tls;
	}

	private static string FragmentFallbackDelay(string? interval)
	{
		if (!string.IsNullOrWhiteSpace(interval))
		{
			string first = interval.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? string.Empty;
			if (int.TryParse(first, out int milliseconds) && milliseconds > 0)
			{
				return milliseconds + "ms";
			}
		}
		return "500ms";
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
		case "xhttp":
		case "splithttp":
			throw new NotSupportedException("sing-box не поддерживает транспорт xhttp. Выберите ядро Xray.");
		case "kcp":
		case "mkcp":
			throw new NotSupportedException("sing-box не поддерживает транспорт mKCP. Выберите ядро Xray.");
		case "raw":
			return null;
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
		AddDnsRule(rules, property, values, "dns-direct");
	}

	private static void AddDnsRule(JArray rules, string property, IEnumerable<string> values, string server)
	{
		string[] items = values.Distinct<string>(StringComparer.OrdinalIgnoreCase).ToArray();
		if (items.Length != 0)
		{
			JObject jObject = new JObject();
			object[] content = items;
			jObject[property] = new JArray(content);
			jObject["server"] = server;
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
