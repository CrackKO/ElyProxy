using System.Globalization;
using System.Net;
using ElyProxy.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ElyProxy.Core;

/// <summary>
/// Builds the sing-box based TUN configuration used by ElyTun.
/// sing-box owns the Windows routes and explicitly excludes local and
/// parallel-adapter networks from the TUN.
/// </summary>
public sealed class SingBoxConfigBuilder
{
    private const string RemoteDnsAddress = "1.1.1.1";
    private const string RemoteDnsServerName = "cloudflare-dns.com";

    public string BuildTun(VlessNode node, int socksPort, XrayTunOptions options)
    {
        var useRoscomVpnDefault = options.RoutingPreset == RoutingPreset.RoscomVpnDefault;
        var direct = BuildDirectRules(options, includeCustomBypassRules: !useRoscomVpnDefault);
        var routeRules = new JArray
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

        if (useRoscomVpnDefault)
            AddRoscomVpnDefaultRouteRules(routeRules, direct);
        else
            AddCustomRouteRules(routeRules, direct);

        var addresses = new JArray("172.19.0.1/30");
        if (options.IncludeIpv6)
            addresses.Add("fdfe:dcba:9876::1/126");

        var tun = new JObject
        {
            ["type"] = "tun",
            ["tag"] = "tun-in",
            ["interface_name"] = string.IsNullOrWhiteSpace(options.InterfaceName) ? "ElyTun" : options.InterfaceName,
            ["address"] = addresses,
            ["mtu"] = options.Mtu,
            ["auto_route"] = true,
            ["stack"] = "system"
        };

        if (direct.RouteExcludes.Count > 0)
            tun["route_exclude_address"] = new JArray(direct.RouteExcludes);

        var config = new JObject
        {
            // A full-system TUN sees every desktop connection. INFO logging
            // emits a line for many of them and can overwhelm a GUI client.
            ["log"] = new JObject { ["level"] = "warn", ["timestamp"] = true },
            ["dns"] = useRoscomVpnDefault ? BuildRoscomVpnDefaultDns() : BuildDns(direct, options),
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
                new JObject { ["type"] = "direct", ["tag"] = "direct" },
                new JObject { ["type"] = "block", ["tag"] = "block" }
            },
            ["route"] = BuildRoute(routeRules, useRoscomVpnDefault)
        };

        return config.ToString(Formatting.Indented);
    }

    private static JObject BuildRoute(JArray routeRules, bool useRoscomVpnDefault)
    {
        var route = new JObject
        {
            ["auto_detect_interface"] = true,
            // Resolve outbound host names (including a VLESS host name)
            // before they can be captured by the TUN. The server is an IP
            // based UDP resolver, so no resolver cycle is created.
            ["default_domain_resolver"] = useRoscomVpnDefault ? "dns-domestic" : "dns-direct",
            ["rules"] = routeRules,
            ["final"] = "proxy"
        };

        if (useRoscomVpnDefault)
        {
            route["rule_set"] = new JArray(RoscomVpnDefaultRouting.RuleSets.Select(ruleSet => new JObject
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
        // This preserves DEFAULT.JSON RouteOrder: block → proxy → direct.
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

    private static void AddRuleSetRoute(JArray rules, IEnumerable<string> tags, string outbound)
    {
        var items = tags.ToArray();
        if (items.Length == 0)
            return;

        rules.Add(new JObject
        {
            ["rule_set"] = new JArray(items),
            ["outbound"] = outbound
        });
    }

    private static JObject BuildDns(DirectRules direct, XrayTunOptions options)
    {
        var directDns = options.DnsServers.FirstOrDefault(address => IPAddress.TryParse(address, out _))
            ?? RemoteDnsAddress;
        var rules = new JArray();

        AddDnsRule(rules, "domain_suffix", direct.DomainSuffixes);
        AddDnsRule(rules, "domain", direct.FullDomains);
        AddDnsRule(rules, "domain_regex", direct.DomainRegexes);
        AddDnsRule(rules, "domain_keyword", direct.DomainKeywords);

        return new JObject
        {
            ["servers"] = new JArray
            {
                // Do not set detour: "direct" here. Since sing-box 1.12 the
                // new DNS servers use an implicit direct dialer and reject a
                // detour to a named, empty direct outbound at runtime.
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
                    ["server"] = RemoteDnsAddress,
                    ["server_port"] = 443,
                    ["path"] = "/dns-query",
                    ["headers"] = new JObject { ["Host"] = RemoteDnsServerName },
                    ["tls"] = new JObject
                    {
                        ["enabled"] = true,
                        ["server_name"] = RemoteDnsServerName
                    },
                    ["detour"] = "proxy"
                }
            },
            ["rules"] = rules,
            ["final"] = "dns-remote",
            ["independent_cache"] = true
        };
    }

    private static JObject BuildRoscomVpnDefaultDns()
    {
        var directTags = RoscomVpnDefaultRouting.DirectSiteTags;
        return new JObject
        {
            ["servers"] = new JArray
            {
                // HAPP DEFAULT.JSON DnsHosts, represented in the sing-box 1.13
                // format as a dedicated hosts DNS server.
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
                // 77.88.8.8 presents a certificate for the documented DoH
                // host common.dot.dns.yandex.net, not dns.yandex.ru.
                BuildDohServer("dns-domestic", "77.88.8.8", "common.dot.dns.yandex.net", detour: null),
                BuildDohServer("dns-remote", "8.8.8.8", "dns.google", detour: "proxy")
            },
            ["rules"] = new JArray
            {
                new JObject
                {
                    ["domain"] = new JArray("lkfl2.nalog.ru", "lknpd.nalog.ru"),
                    ["server"] = "dns-hosts"
                },
                new JObject
                {
                    ["rule_set"] = new JArray(directTags),
                    ["server"] = "dns-domestic"
                }
            },
            ["final"] = "dns-remote",
            ["independent_cache"] = true
        };
    }

    private static JObject BuildDohServer(string tag, string address, string serverName, string? detour)
    {
        var server = new JObject
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
            server["detour"] = detour;

        return server;
    }

    private static JObject BuildVlessOutbound(VlessNode node)
    {
        var outbound = new JObject
        {
            ["type"] = "vless",
            ["tag"] = "proxy",
            ["server"] = node.Address,
            ["server_port"] = node.Port,
            ["uuid"] = node.UUID
        };

        if (!string.IsNullOrWhiteSpace(node.Flow))
            outbound["flow"] = node.Flow;

        var tls = BuildTls(node);
        if (tls != null)
            outbound["tls"] = tls;

        var transport = BuildTransport(node);
        if (transport != null)
            outbound["transport"] = transport;

        return outbound;
    }

    private static JObject? BuildTls(VlessNode node)
    {
        var security = node.Security?.ToLowerInvariant();
        if (security is not ("tls" or "reality"))
            return null;

        var tls = new JObject
        {
            ["enabled"] = true,
            ["server_name"] = node.SNI,
            ["utls"] = new JObject
            {
                ["enabled"] = true,
                ["fingerprint"] = string.IsNullOrWhiteSpace(node.Fingerprint) ? "chrome" : node.Fingerprint
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
            tls["alpn"] = new JArray(node.Alpn.Split(',').Select(value => value.Trim()));

        return tls;
    }

    private static JObject? BuildTransport(VlessNode node)
    {
        return node.Network?.ToLowerInvariant() switch
        {
            "ws" => BuildWebSocketTransport(node),
            "h2" or "http" => BuildHttpTransport(node),
            "httpupgrade" => BuildHttpUpgradeTransport(node),
            "grpc" => new JObject
            {
                ["type"] = "grpc",
                ["service_name"] = node.Path
            },
            "quic" => new JObject { ["type"] = "quic" },
            "tcp" when string.Equals(node.HeaderType, "http", StringComparison.OrdinalIgnoreCase) => BuildHttpTransport(node),
            _ => null
        };
    }

    private static JObject BuildHttpTransport(VlessNode node)
    {
        var transport = new JObject
        {
            ["type"] = "http",
            ["path"] = string.IsNullOrWhiteSpace(node.Path) ? "/" : node.Path
        };

        if (!string.IsNullOrWhiteSpace(node.Host))
        {
            transport["host"] = new JArray(node.Host
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }

        return transport;
    }

    private static JObject BuildHttpUpgradeTransport(VlessNode node)
    {
        var transport = new JObject
        {
            ["type"] = "httpupgrade",
            ["path"] = string.IsNullOrWhiteSpace(node.Path) ? "/" : node.Path
        };

        if (!string.IsNullOrWhiteSpace(node.Host))
            transport["host"] = node.Host;

        return transport;
    }

    private static JObject BuildWebSocketTransport(VlessNode node)
    {
        var transport = new JObject
        {
            ["type"] = "ws",
            ["path"] = string.IsNullOrWhiteSpace(node.Path) ? "/" : node.Path
        };

        if (!string.IsNullOrWhiteSpace(node.Host))
            transport["headers"] = new JObject { ["Host"] = node.Host };

        return transport;
    }

    private static void AddRouteRule(JArray rules, string property, IEnumerable<string> values, string outbound)
    {
        var items = values.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (items.Length == 0)
            return;

        rules.Add(new JObject
        {
            [property] = new JArray(items),
            ["outbound"] = outbound
        });
    }

    private static void AddDnsRule(JArray rules, string property, IEnumerable<string> values)
    {
        var items = values.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (items.Length == 0)
            return;

        rules.Add(new JObject
        {
            [property] = new JArray(items),
            ["server"] = "dns-direct"
        });
    }

    private static DirectRules BuildDirectRules(XrayTunOptions options, bool includeCustomBypassRules)
    {
        var result = new DirectRules();

        if (includeCustomBypassRules)
        {
            foreach (var rule in options.BypassRules)
                result.Add(rule);
        }

        foreach (var cidr in options.DirectIpCidrs)
            result.AddIp(cidr, excludeRoute: true);

        return result;
    }

    private sealed class DirectRules
    {
        private static readonly string[] LocalRouteExclusions =
        [
            "10.0.0.0/8",
            "100.64.0.0/10",
            "169.254.0.0/16",
            "172.16.0.0/12",
            "192.168.0.0/16",
            "224.0.0.0/4",
            "fc00::/7",
            "fe80::/10",
            "ff00::/8"
        ];

        public HashSet<string> DomainSuffixes { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> FullDomains { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> DomainRegexes { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> DomainKeywords { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> IpCidrs { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> RouteExcludes { get; } = new(StringComparer.OrdinalIgnoreCase);

        public DirectRules()
        {
            foreach (var cidr in LocalRouteExclusions)
                RouteExcludes.Add(cidr);
        }

        public void Add(string? source)
        {
            var rule = source?.Trim();
            if (string.IsNullOrWhiteSpace(rule) || rule.StartsWith('#'))
                return;

            if (rule.StartsWith("domain:", StringComparison.OrdinalIgnoreCase))
            {
                AddDomainSuffix(rule[7..]);
                return;
            }

            if (rule.StartsWith("full:", StringComparison.OrdinalIgnoreCase))
            {
                FullDomains.Add(rule[5..]);
                return;
            }

            if (rule.StartsWith("regexp:", StringComparison.OrdinalIgnoreCase))
            {
                DomainRegexes.Add(rule[7..]);
                return;
            }

            if (rule.StartsWith("keyword:", StringComparison.OrdinalIgnoreCase))
            {
                DomainKeywords.Add(rule[8..]);
                return;
            }

            if (rule.StartsWith("geoip:", StringComparison.OrdinalIgnoreCase))
                return;

            if (rule.StartsWith("geosite:category-ru", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var suffix in new[] { "ru", "xn--p1ai", "su", "com.ru", "edu.ru" })
                    AddDomainSuffix(suffix);
                return;
            }

            if (rule.StartsWith("geosite:", StringComparison.OrdinalIgnoreCase))
                return;

            if (IsIpOrCidr(rule))
            {
                AddIp(rule, excludeRoute: true);
                return;
            }

            var host = rule.TrimStart('.');
            if (host.StartsWith("*."))
                host = host[2..];

            if (!string.IsNullOrWhiteSpace(host) && !host.Contains('/') && !host.Contains(' '))
                AddDomainSuffix(host);
        }

        public void AddIp(string value, bool excludeRoute)
        {
            if (!IsIpOrCidr(value))
                return;

            IpCidrs.Add(value);
            if (excludeRoute)
                RouteExcludes.Add(value);
        }

        private void AddDomainSuffix(string? source)
        {
            var host = source?.Trim().TrimStart('.');
            if (string.IsNullOrWhiteSpace(host))
                return;

            if (host.StartsWith("*."))
                host = host[2..];

            DomainSuffixes.Add(host.ToLowerInvariant());

            try
            {
                var ascii = new IdnMapping().GetAscii(host).ToLowerInvariant();
                DomainSuffixes.Add(ascii);
            }
            catch
            {
                // The original value is retained for sing-box to validate.
            }
        }
    }

    private static bool IsIpOrCidr(string value)
    {
        var slash = value.LastIndexOf('/');
        var addressText = slash >= 0 ? value[..slash] : value;
        if (!IPAddress.TryParse(addressText, out var address))
            return false;

        if (slash < 0)
            return true;

        return int.TryParse(value[(slash + 1)..], out var prefix)
            && prefix >= 0
            && prefix <= address.GetAddressBytes().Length * 8;
    }
}
