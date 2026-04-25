using Newtonsoft.Json.Linq;
using ElyProxy.Models;

namespace ElyProxy.Core;

public class ConfigBuilder
{
    public string Build(VlessNode node, int socksPort = 1080)
    {
        var config = new JObject
        {
            ["log"] = new JObject { ["loglevel"] = "warning" },
            ["inbounds"] = new JArray { BuildSocksInbound(socksPort) },
            ["outbounds"] = BuildVlessOutbounds(node),
            ["routing"] = BuildRouting()
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
            ["routing"] = BuildRouting()
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

        return new JArray
        {
            proxy,
            new JObject { ["tag"] = "direct", ["protocol"] = "freedom" },
            new JObject { ["tag"] = "block", ["protocol"] = "blackhole" }
        };
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

    private static JObject BuildRouting()
    {
        return new JObject
        {
            ["domainStrategy"] = "AsIs",
            ["rules"] = new JArray
            {
                new JObject
                {
                    ["type"] = "field",
                    ["outboundTag"] = "direct",
                    ["domain"] = new JArray("geosite:private")
                },
                new JObject
                {
                    ["type"] = "field",
                    ["outboundTag"] = "direct",
                    ["ip"] = new JArray("geoip:private")
                }
            }
        };
    }

    private static string Fallback(string? value, string fallback) =>
        string.IsNullOrEmpty(value) ? fallback : value;
}
