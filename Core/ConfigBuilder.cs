using Newtonsoft.Json.Linq;
using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using ElyProxy.Models;

namespace ElyProxy.Core;

public class ConfigBuilder
{
    public string Build(VlessNode node, int socksPort = 1080, XrayTunOptions? tunOptions = null)
    {
        var inbounds = new JArray { BuildSocksInbound(socksPort) };
        if (tunOptions != null)
            inbounds.Add(BuildTunInbound(tunOptions));

        var config = new JObject
        {
            ["log"] = new JObject { ["loglevel"] = "warning" },
            ["inbounds"] = inbounds,
            ["outbounds"] = BuildVlessOutbounds(node),
            ["routing"] = BuildRouting(tunOptions)
        };

        return config.ToString(Newtonsoft.Json.Formatting.Indented);
    }

    public string BuildSocks(SocksNode node, int localPort = 1080)
    {
        var config = new JObject
        {
            ["log"] = new JObject { ["loglevel"] = "warning" },
            ["inbounds"] = new JArray { BuildSocksInbound(localPort) },
            ["outbounds"] = BuildSocksOutbounds(node),
            ["routing"] = BuildRouting(null)
        };

        return config.ToString(Newtonsoft.Json.Formatting.Indented);
    }

    private static JObject BuildSocksInbound(int port)
    {
        return new JObject
        {
            ["tag"] = "socks-in",
            ["port"] = port,
            ["listen"] = "127.0.0.1",
            ["protocol"] = "socks",
            ["settings"] = new JObject
            {
                ["auth"] = "noauth",
                ["udp"] = true
            },
            ["sniffing"] = new JObject
            {
                ["enabled"] = true,
                ["destOverride"] = new JArray("http", "tls")
            }
        };
    }

    private static JObject BuildTunInbound(XrayTunOptions options)
    {
        var gateway = new JArray("172.19.0.1/30");
        var routes = new JArray("0.0.0.0/0");

        if (options.IncludeIpv6)
        {
            gateway.Add("fdfe:dcba:9876::1/126");
            routes.Add("::/0");
        }

        var settings = new JObject
        {
            ["name"] = string.IsNullOrWhiteSpace(options.InterfaceName) ? "ElyTun" : options.InterfaceName,
            ["mtu"] = options.Mtu,
            ["gateway"] = gateway,
            ["userLevel"] = 0,
            ["autoSystemRoutingTable"] = routes,
            ["autoOutboundsInterface"] = "auto"
        };

        if (options.DnsServers.Count > 0)
            settings["dns"] = new JArray(options.DnsServers);

        return new JObject
        {
            ["tag"] = "elytun-in",
            ["protocol"] = "tun",
            ["settings"] = settings,
            ["sniffing"] = new JObject
            {
                ["enabled"] = true,
                ["destOverride"] = new JArray("http", "tls", "quic")
            }
        };
    }

    private static JArray BuildVlessOutbounds(VlessNode node)
    {
        var user = new JObject
        {
            ["id"] = node.UUID,
            ["encryption"] = string.IsNullOrEmpty(node.Encryption) ? "none" : node.Encryption
        };

        if (!string.IsNullOrEmpty(node.Flow))
            user["flow"] = node.Flow;

        var vnext = new JObject
        {
            ["address"] = node.Address,
            ["port"] = node.Port,
            ["users"] = new JArray(user)
        };

        var proxy = new JObject
        {
            ["tag"] = "proxy",
            ["protocol"] = "vless",
            ["settings"] = new JObject { ["vnext"] = new JArray(vnext) },
            ["streamSettings"] = BuildStreamSettings(node)
        };

        var outbounds = new JArray
        {
            proxy,
            new JObject { ["tag"] = "direct", ["protocol"] = "freedom" },
            new JObject { ["tag"] = "block", ["protocol"] = "blackhole" }
        };

        return outbounds;
    }

    private static JArray BuildSocksOutbounds(SocksNode node)
    {
        var server = new JObject
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

        var proxy = new JObject
        {
            ["tag"] = "proxy",
            ["protocol"] = "socks",
            ["settings"] = new JObject
            {
                ["servers"] = new JArray(server)
            }
        };

        return new JArray
        {
            proxy,
            new JObject { ["tag"] = "direct", ["protocol"] = "freedom" },
            new JObject { ["tag"] = "block", ["protocol"] = "blackhole" }
        };
    }

    private static JObject BuildStreamSettings(VlessNode node)
    {
        var network = string.IsNullOrEmpty(node.Network) ? "tcp" : node.Network.ToLowerInvariant();
        var security = string.IsNullOrEmpty(node.Security) ? "none" : node.Security.ToLowerInvariant();

        var stream = new JObject
        {
            ["network"] = network,
            ["security"] = security
        };

        switch (security)
        {
            case "reality":
                stream["realitySettings"] = BuildRealitySettings(node);
                break;
            case "tls":
                stream["tlsSettings"] = BuildTlsSettings(node);
                break;
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
                var tcpSettings = BuildTcpSettings(node);
                if (tcpSettings != null)
                    stream["tcpSettings"] = tcpSettings;
                break;
            case "h2" or "http":
                stream["httpSettings"] = BuildHttpSettings(node);
                break;
        }

        return stream;
    }

    private static JObject BuildRealitySettings(VlessNode node)
    {
        var obj = new JObject
        {
            ["fingerprint"] = Fallback(node.Fingerprint, "chrome"),
            ["serverName"] = node.SNI,
            ["publicKey"] = node.PublicKey,
            ["shortId"] = node.ShortId
        };

        if (!string.IsNullOrEmpty(node.SpiderX))
            obj["spiderX"] = node.SpiderX;

        return obj;
    }

    private static JObject BuildTlsSettings(VlessNode node)
    {
        var obj = new JObject
        {
            ["serverName"] = node.SNI,
            ["fingerprint"] = Fallback(node.Fingerprint, "chrome")
        };

        if (!string.IsNullOrEmpty(node.Alpn))
            obj["alpn"] = new JArray(node.Alpn.Split(',').Select(a => a.Trim()).ToArray());

        return obj;
    }

    private static JObject BuildWsSettings(VlessNode node)
    {
        var obj = new JObject
        {
            ["path"] = Fallback(node.Path, "/")
        };

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
            return null;

        return new JObject
        {
            ["header"] = new JObject { ["type"] = node.HeaderType }
        };
    }

    private static JObject BuildHttpSettings(VlessNode node)
    {
        var obj = new JObject
        {
            ["path"] = Fallback(node.Path, "/")
        };

        if (!string.IsNullOrEmpty(node.Host))
            obj["host"] = new JArray(node.Host.Split(',').Select(h => h.Trim()).ToArray());

        return obj;
    }

    private static JObject BuildRouting(XrayTunOptions? tunOptions)
    {
        var rules = new JArray();

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
        var domains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ips = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rule in options.BypassRules)
            AddBypassRule(rule, domains, ips);

        foreach (var cidr in options.DirectIpCidrs)
        {
            if (IsIpOrCidr(cidr))
                ips.Add(cidr);
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
        var rule = source?.Trim();
        if (string.IsNullOrWhiteSpace(rule) || rule.StartsWith('#'))
            return;

        if (rule.StartsWith("geoip:", StringComparison.OrdinalIgnoreCase))
        {
            ips.Add(rule.ToLowerInvariant());
            return;
        }

        if (rule.StartsWith("geosite:", StringComparison.OrdinalIgnoreCase)
            || rule.StartsWith("domain:", StringComparison.OrdinalIgnoreCase)
            || rule.StartsWith("full:", StringComparison.OrdinalIgnoreCase)
            || rule.StartsWith("regexp:", StringComparison.OrdinalIgnoreCase)
            || rule.StartsWith("keyword:", StringComparison.OrdinalIgnoreCase))
        {
            domains.Add(rule);
            return;
        }

        if (IsIpOrCidr(rule))
        {
            ips.Add(rule);
            return;
        }

        var host = rule.Trim().TrimStart('.');
        if (host.StartsWith("*."))
            host = host[2..];

        if (string.IsNullOrWhiteSpace(host) || host.Contains('/') || host.Contains(' '))
            return;

        foreach (var variant in GetDomainVariants(host))
        {
            var escaped = Regex.Escape(variant).Replace("\\*", ".*");
            domains.Add($"regexp:(^|\\.){escaped}$");
        }
    }

    private static IReadOnlyList<string> GetDomainVariants(string host)
    {
        var variants = new List<string> { host.ToLowerInvariant() };

        try
        {
            var ascii = new IdnMapping().GetAscii(host).ToLowerInvariant();
            if (!string.Equals(ascii, host, StringComparison.OrdinalIgnoreCase))
                variants.Add(ascii);
        }
        catch
        {
            // Invalid IDN values are still kept as literal Xray regexp rules.
        }

        return variants;
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

    private static string Fallback(string? value, string fallback) =>
        string.IsNullOrEmpty(value) ? fallback : value;
}
