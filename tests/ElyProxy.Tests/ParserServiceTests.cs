using System.Text;
using ElyProxy.Services;

namespace ElyProxy.Tests;

public class ParserServiceTests
{
    private readonly ParserService _parser = new();

    [Fact]
    public void ParseVlessUri_ParsesIpv6AndTransportParameters()
    {
        const string uri = "vless://11111111-1111-1111-1111-111111111111@[2001:db8::10]:8443?type=ws&security=tls&sni=cdn.example.com&host=edge.example.com&path=%2Fws&alpn=h2%2Chttp%2F1.1#Test%20node";

        var node = _parser.ParseVlessUri(uri);

        Assert.NotNull(node);
        Assert.Equal("Test node", node.Name);
        Assert.Equal("2001:db8::10", node.Address);
        Assert.Equal(8443, node.Port);
        Assert.Equal("ws", node.Network);
        Assert.Equal("tls", node.Security);
        Assert.Equal("cdn.example.com", node.SNI);
        Assert.Equal("edge.example.com", node.Host);
        Assert.Equal("/ws", node.Path);
        Assert.Equal("h2,http/1.1", node.Alpn);
    }

    [Fact]
    public void ParseSubscriptionContent_DecodesUrlSafeBase64AndSkipsInvalidLines()
    {
        const string vless = "vless://22222222-2222-2222-2222-222222222222@example.com:443?type=grpc&security=reality&pbk=public-key&sid=abcd#Main";
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes($"ignored\n{vless}\nhttp://example.com"))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

        var nodes = _parser.ParseSubscriptionContent(encoded);

        var node = Assert.Single(nodes);
        Assert.Equal("Main", node.Name);
        Assert.Equal("grpc", node.Network);
        Assert.Equal("reality", node.Security);
        Assert.Equal("public-key", node.PublicKey);
        Assert.Equal("abcd", node.ShortId);
    }

    [Theory]
    [InlineData("vless://uuid@example.com:0")]
    [InlineData("vless://uuid@example.com:65536")]
    [InlineData("vless://uuid@example.com:not-a-port")]
    [InlineData("vless://uuid@example.com")]
    public void ParseVlessUri_RejectsInvalidEndpoint(string uri)
    {
        Assert.Null(_parser.ParseVlessUri(uri));
    }

    [Fact]
    public void ParseVmessUri_DecodesBase64Json()
    {
        const string json = """{"v":"2","ps":"VM node","add":"vm.example.com","port":"8443","id":"33333333-3333-3333-3333-333333333333","aid":"0","scy":"chacha20-poly1305","net":"ws","type":"none","host":"cdn.example.com","path":"/vm","tls":"tls","sni":"cdn.example.com","fp":"firefox"}""";
        var uri = "vmess://" + Convert.ToBase64String(Encoding.UTF8.GetBytes(json));

        var node = _parser.ParseVmessUri(uri);

        Assert.NotNull(node);
        Assert.Equal("vmess", node.Protocol);
        Assert.Equal("VM node", node.Name);
        Assert.Equal("vm.example.com", node.Address);
        Assert.Equal(8443, node.Port);
        Assert.Equal("33333333-3333-3333-3333-333333333333", node.UUID);
        Assert.Equal("chacha20-poly1305", node.VmessSecurity);
        Assert.Equal("ws", node.Network);
        Assert.Equal("cdn.example.com", node.Host);
        Assert.Equal("/vm", node.Path);
        Assert.Equal("tls", node.Security);
        Assert.Equal("firefox", node.Fingerprint);
    }

    [Fact]
    public void ParseTrojanUri_ParsesPasswordAndTransport()
    {
        const string uri = "trojan://p%40ssw0rd@trojan.example.com:443?security=tls&sni=trojan.example.com&type=ws&path=%2Fws&host=edge.example.com&allowInsecure=1#Trojan";

        var node = _parser.ParseTrojanUri(uri);

        Assert.NotNull(node);
        Assert.Equal("trojan", node.Protocol);
        Assert.Equal("p@ssw0rd", node.Password);
        Assert.Equal("trojan.example.com", node.Address);
        Assert.Equal(443, node.Port);
        Assert.Equal("ws", node.Network);
        Assert.Equal("/ws", node.Path);
        Assert.True(node.AllowInsecure);
    }

    [Fact]
    public void ParseShadowsocksUri_ParsesSip002AndLegacyLinks()
    {
        var sip002 = "ss://" + Convert.ToBase64String(Encoding.UTF8.GetBytes("aes-256-gcm:secret")) + "@ss.example.com:8388#SS";
        var legacy = "ss://" + Convert.ToBase64String(Encoding.UTF8.GetBytes("2022-blake3-aes-128-gcm:key==@ss2.example.com:8389")) + "#Legacy";

        var modern = _parser.ParseShadowsocksUri(sip002);
        var old = _parser.ParseShadowsocksUri(legacy);

        Assert.NotNull(modern);
        Assert.Equal("shadowsocks", modern.Protocol);
        Assert.Equal("aes-256-gcm", modern.Method);
        Assert.Equal("secret", modern.Password);
        Assert.Equal(8388, modern.Port);
        Assert.NotNull(old);
        Assert.Equal("2022-blake3-aes-128-gcm", old.Method);
        Assert.Equal("key==", old.Password);
        Assert.Equal("ss2.example.com", old.Address);
    }

    [Fact]
    public void ParseHysteria2Uri_ParsesAuthObfsAndPortHopping()
    {
        const string uri = "hysteria2://auth%2Btoken@hy.example.com:443/?sni=hy.example.com&insecure=1&obfs=salamander&obfs-password=obfs-secret&pinSHA256=abc123&mport=2000%3A3000&hopInterval=30&up=100&down=200#HY2";

        var node = _parser.ParseHysteria2Uri(uri);

        Assert.NotNull(node);
        Assert.Equal("hysteria2", node.Protocol);
        Assert.Equal("auth+token", node.Password);
        Assert.Equal("hy.example.com", node.SNI);
        Assert.Equal("salamander", node.Obfs);
        Assert.Equal("obfs-secret", node.ObfsPassword);
        Assert.Equal("abc123", node.PinnedPeerCertificateChainSha256);
        Assert.Equal("2000:3000", node.ServerPorts);
        Assert.Equal("30", node.HopInterval);
        Assert.Equal("100", node.UpMbps);
        Assert.Equal("200", node.DownMbps);
        Assert.True(node.AllowInsecure);
    }

    [Fact]
    public void ParseTuicUri_ParsesUuidPasswordAndCongestion()
    {
        const string uri = "tuic://44444444-4444-4444-4444-444444444444:p%40ss@tuic.example.com:443?congestion_control=bbr&udp_relay_mode=native&alpn=h3&sni=tuic.example.com#TUIC";

        var node = _parser.ParseTuicUri(uri);

        Assert.NotNull(node);
        Assert.Equal("tuic", node.Protocol);
        Assert.Equal("44444444-4444-4444-4444-444444444444", node.UUID);
        Assert.Equal("p@ss", node.Password);
        Assert.Equal("bbr", node.CongestionControl);
        Assert.Equal("native", node.UdpRelayMode);
        Assert.Equal("h3", node.Alpn);
        Assert.Equal("tuic.example.com", node.SNI);
    }

    [Fact]
    public void ParseSubscriptionContent_ParsesMixedProtocolsFromPlainText()
    {
        const string content = """
            vless://55555555-5555-5555-5555-555555555555@v.example.com:443?type=tcp&security=reality&pbk=key&sid=ab#V
            vmess://eyJ2IjoiMiIsInBzIjoiTSIsImFkZCI6Im0uZXhhbXBsZS5jb20iLCJwb3J0IjoiODAiLCJpZCI6IjY2NjY2NjY2LTY2NjYtNjY2Ni02NjY2LTY2NjY2NjY2NjY2NiIsIm5ldCI6IndzIiwidGxzIjoiIn0=
            trojan://pass@t.example.com:443#T
            hysteria2://auth@h.example.com:443#H
            tuic://77777777-7777-7777-7777-777777777777:pw@c.example.com:443#C
            """;

        var nodes = _parser.ParseSubscriptionContent(content);

        Assert.Equal(5, nodes.Count);
        Assert.Equal(["vless", "vmess", "trojan", "hysteria2", "tuic"], nodes.Select(node => node.Protocol));
    }
}
