using ElyProxy.Core;
using ElyProxy.Models;

namespace ElyProxy.Tests;

public class HysteriaConfigBuilderTests
{
    [Fact]
    public void Build_ContainsProxyInboundsAndTls()
    {
        var node = CreateNode();

        var yaml = new HysteriaConfigBuilder().Build(node, 1080, 1081);

        Assert.Contains("server: \"hy.example.com:443\"", yaml);
        Assert.Contains("auth: \"token\"", yaml);
        Assert.Contains("sni: \"hy.example.com\"", yaml);
        Assert.Contains("insecure: true", yaml);
        Assert.Contains("type: \"salamander\"", yaml);
        Assert.Contains("password: \"obfs\"", yaml);
        Assert.Contains("serverPorts: \"2000:3000\"", yaml);
        Assert.Contains("hopInterval: \"30s\"", yaml);
        Assert.Contains("up: \"100 mbps\"", yaml);
        Assert.Contains("down: \"200 mbps\"", yaml);
        Assert.Contains("socks5:", yaml);
        Assert.Contains("listen: 127.0.0.1:1080", yaml);
        Assert.Contains("http:", yaml);
        Assert.Contains("listen: 127.0.0.1:1081", yaml);
    }

    [Fact]
    public void BuildTun_ContainsTunSectionWithExclusions()
    {
        var node = CreateNode();
        var options = new XrayTunOptions(
            "ElyTun-Test",
            1400,
            ["1.1.1.1"],
            IncludeIpv6: true,
            BypassRules: [],
            DirectIpCidrs: ["192.168.1.0/24"]);

        var yaml = new HysteriaConfigBuilder().BuildTun(node, options);

        Assert.Contains("tun:", yaml);
        Assert.Contains("name: \"ElyTun-Test\"", yaml);
        Assert.Contains("172.19.0.1/30", yaml);
        Assert.Contains("fdfe:dcba:9876::1/126", yaml);
        Assert.Contains("mtu: 1400", yaml);
        Assert.Contains("autoRoute: true", yaml);
        Assert.Contains("192.168.1.0/24", yaml);
        Assert.DoesNotContain("socks5:", yaml);
    }

    private static VlessNode CreateNode() => new()
    {
        Protocol = "hysteria2",
        Address = "hy.example.com",
        Port = 443,
        Password = "token",
        Security = "tls",
        SNI = "hy.example.com",
        AllowInsecure = true,
        Obfs = "salamander",
        ObfsPassword = "obfs",
        ServerPorts = "2000:3000",
        HopInterval = "30",
        UpMbps = "100",
        DownMbps = "200"
    };
}
