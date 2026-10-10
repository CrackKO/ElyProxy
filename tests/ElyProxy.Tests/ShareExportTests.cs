using ElyProxy.Models;
using ElyProxy.Services;
using Newtonsoft.Json.Linq;

namespace ElyProxy.Tests;

public class ShareExportTests
{
    private readonly ParserService _parser = new();

    [Fact]
    public void ShareLink_RoundTripsVlessReality()
    {
        var node = CreateVless();

        var link = ShareLinkBuilder.Build(node);
        var parsed = _parser.ParseUri(link);

        Assert.StartsWith("vless://", link);
        Assert.NotNull(parsed);
        Assert.Equal(node.UUID, parsed.UUID);
        Assert.Equal(node.Address, parsed.Address);
        Assert.Equal(node.Port, parsed.Port);
        Assert.Equal("reality", parsed.Security);
        Assert.Equal(node.PublicKey, parsed.PublicKey);
        Assert.Equal(node.ShortId, parsed.ShortId);
        Assert.Equal("ws", parsed.Network);
        Assert.Equal(node.Path, parsed.Path);
        Assert.Equal(node.Host, parsed.Host);
    }

    [Theory]
    [InlineData("hysteria2")]
    [InlineData("tuic")]
    [InlineData("anytls")]
    [InlineData("trojan")]
    public void ShareLink_RoundTripsCommonProtocols(string protocol)
    {
        var node = protocol switch
        {
            "hysteria2" => new VlessNode
            {
                Protocol = "hysteria2",
                Address = "hy.example.com",
                Port = 443,
                Password = "auth",
                Security = "tls",
                SNI = "hy.example.com",
                Obfs = "salamander",
                ObfsPassword = "op",
                ServerPorts = "2000:3000",
                AllowInsecure = true
            },
            "tuic" => new VlessNode
            {
                Protocol = "tuic",
                Address = "tuic.example.com",
                Port = 443,
                UUID = "44444444-4444-4444-4444-444444444444",
                Password = "pw",
                Security = "tls",
                SNI = "tuic.example.com",
                CongestionControl = "bbr",
                UdpRelayMode = "native"
            },
            "anytls" => new VlessNode
            {
                Protocol = "anytls",
                Address = "a.example.com",
                Port = 443,
                Password = "secret",
                Security = "tls",
                SNI = "a.example.com"
            },
            _ => new VlessNode
            {
                Protocol = "trojan",
                Address = "t.example.com",
                Port = 443,
                Password = "pass",
                Security = "tls",
                SNI = "t.example.com"
            }
        };

        var link = ShareLinkBuilder.Build(node);
        var parsed = _parser.ParseUri(link);

        Assert.NotNull(parsed);
        Assert.Equal(protocol, parsed.Protocol);
        Assert.Equal(node.Address, parsed.Address);
        Assert.Equal(node.Port, parsed.Port);
        Assert.Equal(node.Password, parsed.Password);
    }

    [Fact]
    public void ShareLink_RoundTripsShadowsocks()
    {
        var node = new VlessNode
        {
            Protocol = "shadowsocks",
            Address = "ss.example.com",
            Port = 8388,
            Method = "aes-256-gcm",
            Password = "secret"
        };

        var link = ShareLinkBuilder.Build(node);
        var parsed = _parser.ParseUri(link);

        Assert.StartsWith("ss://", link);
        Assert.NotNull(parsed);
        Assert.Equal("shadowsocks", parsed.Protocol);
        Assert.Equal("aes-256-gcm", parsed.Method);
        Assert.Equal("secret", parsed.Password);
    }

    [Fact]
    public void ShareLink_RoundTripsVmess()
    {
        var node = new VlessNode
        {
            Protocol = "vmess",
            Address = "vm.example.com",
            Port = 443,
            UUID = "55555555-5555-5555-5555-555555555555",
            VmessSecurity = "chacha20-poly1305",
            Network = "ws",
            Security = "tls",
            SNI = "vm.example.com",
            Path = "/vm",
            Host = "vm.example.com"
        };

        var link = ShareLinkBuilder.Build(node);
        var parsed = _parser.ParseUri(link);

        Assert.StartsWith("vmess://", link);
        Assert.NotNull(parsed);
        Assert.Equal("vmess", parsed.Protocol);
        Assert.Equal(node.UUID, parsed.UUID);
        Assert.Equal("chacha20-poly1305", parsed.VmessSecurity);
        Assert.Equal("/vm", parsed.Path);
    }

    [Fact]
    public void ClashYamlExport_RoundTripsNodes()
    {
        var nodes = new List<VlessNode> { CreateVless(), new VlessNode
        {
            Protocol = "hysteria2",
            Address = "hy.example.com",
            Port = 443,
            Password = "auth",
            Security = "tls",
            SNI = "hy.example.com"
        } };

        var yaml = ClashYamlExporter.Export(nodes);
        var parsed = new ClashYamlParser().Parse(yaml);

        Assert.Equal(2, parsed.Count);
        Assert.Equal("vless", parsed[0].Protocol);
        Assert.Equal("reality", parsed[0].Security);
        Assert.Equal("hy.example.com", parsed[1].Address);
        Assert.Equal("hysteria2", parsed[1].Protocol);
    }

    [Fact]
    public void V2rayNExport_ProducesXrayOutboundsAndSkipsUnsupported()
    {
        var nodes = new List<VlessNode>
        {
            CreateVless(),
            new VlessNode
            {
                Protocol = "hysteria2",
                Address = "hy.example.com",
                Port = 443,
                Password = "auth",
                Security = "tls"
            }
        };

        var json = V2rayNJsonExporter.Export(nodes);
        var root = JObject.Parse(json);
        var outbounds = (JArray)root["outbounds"]!;

        Assert.Single(outbounds);
        Assert.Equal("vless", (string?)outbounds[0]!["protocol"]);
        Assert.Contains("Hysteria2", (string?)root["comment"]);
    }

    private static VlessNode CreateVless() => new()
    {
        Protocol = "vless",
        Name = "Reality node",
        Address = "r.example.com",
        Port = 443,
        UUID = "11111111-1111-1111-1111-111111111111",
        Flow = "xtls-rprx-vision",
        Network = "ws",
        Security = "reality",
        SNI = "www.example.com",
        PublicKey = "PBK",
        ShortId = "ab12",
        Path = "/ws",
        Host = "cdn.example.com",
        Fingerprint = "chrome"
    };
}
