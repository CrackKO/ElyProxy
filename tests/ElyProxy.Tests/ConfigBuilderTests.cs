using ElyProxy.Core;
using ElyProxy.Models;
using Newtonsoft.Json.Linq;

namespace ElyProxy.Tests;

public class ConfigBuilderTests
{
    private readonly ConfigBuilder _builder = new();

    [Fact]
    public void Build_WithTun_AddsIpv6AndBypassRulesWithoutBlockingQuic()
    {
        var config = JObject.Parse(_builder.Build(CreateNode(), tunOptions: new XrayTunOptions(
            "ElyTun-Test",
            1500,
            ["1.1.1.1"],
            IncludeIpv6: true,
            BypassRules: ["*.ru", "*.рф", "26.0.0.0/8", "geosite:category-ru", "geoip:ru"],
            DirectIpCidrs: ["192.168.10.0/24"])));

        var tun = Tokens(config["inbounds"]).Single(item => item["tag"]?.Value<string>() == "elytun-in");
        Assert.Equal(
            ["0.0.0.0/0", "::/0"],
            Tokens(Object(tun["settings"])["autoSystemRoutingTable"]).Select(token => token.Value<string>()));

        var rules = Tokens(Object(config["routing"])["rules"]);
        var directDomainRule = rules.Single(rule => IsTunRule(rule, "direct") && rule["domain"] != null);
        var directIpRule = rules.Single(rule => IsTunRule(rule, "direct") && rule["ip"] != null);

        Assert.Contains("regexp:(^|\\.)ru$", Tokens(directDomainRule["domain"]).Select(token => token.Value<string>()));
        Assert.Contains("regexp:(^|\\.)xn--p1ai$", Tokens(directDomainRule["domain"]).Select(token => token.Value<string>()));
        Assert.Contains("geosite:category-ru", Tokens(directDomainRule["domain"]).Select(token => token.Value<string>()));
        Assert.Contains("26.0.0.0/8", Tokens(directIpRule["ip"]).Select(token => token.Value<string>()));
        Assert.Contains("192.168.10.0/24", Tokens(directIpRule["ip"]).Select(token => token.Value<string>()));
        Assert.Contains("geoip:ru", Tokens(directIpRule["ip"]).Select(token => token.Value<string>()));
        Assert.DoesNotContain(rules, rule => IsTunRule(rule, "block"));
    }

    [Fact]
    public void Build_WithTun_NeverAddsQuicBlockRule()
    {
        var config = JObject.Parse(_builder.Build(CreateNode(), tunOptions: new XrayTunOptions(
            "ElyTun-Test",
            1500,
            ["1.1.1.1"],
            IncludeIpv6: false,
            BypassRules: [],
            DirectIpCidrs: [])));

        var rules = Tokens(Object(config["routing"])["rules"]);

        Assert.DoesNotContain(rules, rule => IsTunRule(rule, "block"));
    }

    [Fact]
    public void Build_WithoutTun_OnlyCreatesSocksInbound()
    {
        var config = JObject.Parse(_builder.Build(CreateNode()));

        var inbound = Assert.Single(Tokens(config["inbounds"]));
        Assert.Equal("socks-in", inbound["tag"]?.Value<string>());
        Assert.DoesNotContain(Tokens(Object(config["routing"])["rules"]), rule => rule["inboundTag"]?.Values<string>().Contains("elytun-in") == true);
    }

    [Fact]
    public void Build_Vmess_UsesVmessOutboundWithAlterIdAndSecurity()
    {
        var node = CreateNode();
        node.Protocol = "vmess";
        node.AlterId = "0";
        node.VmessSecurity = "chacha20-poly1305";
        node.Security = "tls";
        node.Network = "ws";
        node.Path = "/vm";

        var config = JObject.Parse(_builder.Build(node));
        var proxy = Tokens(config["outbounds"]).Single(item => item["tag"]?.Value<string>() == "proxy");
        var vnext = Tokens(Object(proxy["settings"])["vnext"]).Single();
        var user = Tokens(Object(vnext)["users"]).Single();

        Assert.Equal("vmess", proxy["protocol"]?.Value<string>());
        Assert.Equal("chacha20-poly1305", user["security"]?.Value<string>());
        Assert.Equal(0, user["alterId"]?.Value<int>());
        var stream = Object(proxy["streamSettings"]);
        Assert.Equal("ws", stream["network"]?.Value<string>());
        Assert.Equal("tls", stream["security"]?.Value<string>());
    }

    [Fact]
    public void Build_Trojan_UsesTrojanOutboundWithPassword()
    {
        var node = CreateNode();
        node.Protocol = "trojan";
        node.Password = "secret";
        node.Security = "tls";

        var config = JObject.Parse(_builder.Build(node));
        var proxy = Tokens(config["outbounds"]).Single(item => item["tag"]?.Value<string>() == "proxy");
        var server = Tokens(Object(proxy["settings"])["servers"]).Single();

        Assert.Equal("trojan", proxy["protocol"]?.Value<string>());
        Assert.Equal("secret", server["password"]?.Value<string>());
        Assert.Equal("198.51.100.10", server["address"]?.Value<string>());
    }

    [Fact]
    public void Build_Shadowsocks_UsesMethodAndPassword()
    {
        var node = CreateNode();
        node.Protocol = "shadowsocks";
        node.Method = "2022-blake3-aes-128-gcm";
        node.Password = "key==";

        var config = JObject.Parse(_builder.Build(node));
        var proxy = Tokens(config["outbounds"]).Single(item => item["tag"]?.Value<string>() == "proxy");
        var server = Tokens(Object(proxy["settings"])["servers"]).Single();

        Assert.Equal("shadowsocks", proxy["protocol"]?.Value<string>());
        Assert.Equal("2022-blake3-aes-128-gcm", server["method"]?.Value<string>());
        Assert.Equal("key==", server["password"]?.Value<string>());
        Assert.Null(proxy["streamSettings"]);
    }

    [Fact]
    public void Build_Hysteria2_IsRejectedByXray()
    {
        var node = CreateNode();
        node.Protocol = "hysteria2";
        node.Password = "auth";

        Assert.Throws<NotSupportedException>(() => _builder.Build(node));
    }

    [Fact]
    public void Build_SocksBackend_WithoutTunInbound_ScopesBypassRulesToSocksInbound()
    {
        var options = new XrayTunOptions(
            "ElyTun-Test",
            1500,
            ["1.1.1.1"],
            IncludeIpv6: false,
            BypassRules: ["*.ru"],
            DirectIpCidrs: ["192.168.10.0/24"]);

        var config = JObject.Parse(_builder.Build(CreateNode(), tunOptions: options, includeTunInbound: false));
        var inbounds = Tokens(config["inbounds"]);

        Assert.Single(inbounds);
        Assert.Equal("socks-in", inbounds[0]["tag"]?.Value<string>());
        var rules = Tokens(Object(config["routing"])["rules"]);
        Assert.Contains(rules, rule => rule["inboundTag"]?.Values<string>().Contains("socks-in") == true && rule["domain"] != null);
        Assert.Contains(rules, rule => rule["inboundTag"]?.Values<string>().Contains("socks-in") == true && rule["ip"] != null);
        Assert.DoesNotContain(rules, rule => rule["inboundTag"]?.Values<string>().Contains("elytun-in") == true);
    }

    [Fact]
    public void Build_WithProcessBypass_AddsProcessRuleWithForwardSlashesBeforeDomainRules()
    {
        var options = new XrayTunOptions(
            "ElyTun-Test",
            1500,
            ["1.1.1.1"],
            IncludeIpv6: false,
            BypassRules: ["*.ru"],
            DirectIpCidrs: [],
            BypassProcessPaths: [@"C:\Program Files\Example Browser\browser.exe"]);

        var config = JObject.Parse(_builder.Build(CreateNode(), tunOptions: options));
        var rules = Tokens(Object(config["routing"])["rules"]).ToArray();
        var processIndex = Array.FindIndex(rules, rule => rule["process"] != null);
        var domainIndex = Array.FindIndex(rules, rule => rule["domain"] != null);

        Assert.True(processIndex >= 0, "Process rule was not generated.");
        Assert.True(domainIndex >= 0);
        Assert.True(processIndex < domainIndex, "Process bypass must precede domain rules.");
        Assert.Equal("direct", rules[processIndex]["outboundTag"]?.Value<string>());
        Assert.Contains("C:/Program Files/Example Browser/browser.exe", rules[processIndex]["process"]!.Values<string>());
    }

    [Fact]
    public void Build_WithFragment_AddsFragmentOutboundAndDialerProxy()    {
        var options = new XrayTunOptions(
            "ElyTun-Test",
            1500,
            ["1.1.1.1"],
            IncludeIpv6: false,
            BypassRules: [],
            DirectIpCidrs: [],
            FragmentEnabled: true,
            FragmentPackets: "tlshello",
            FragmentLength: "100-200",
            FragmentInterval: "10-20",
            NoiseType: "rand",
            NoisePacket: "50-150",
            NoiseDelay: "10-16");

        var config = JObject.Parse(_builder.Build(CreateNode(), tunOptions: options, includeTunInbound: false));
        var outbounds = Tokens(config["outbounds"]);
        var proxy = outbounds.Single(item => item["tag"]?.Value<string>() == "proxy");
        var fragment = outbounds.Single(item => item["tag"]?.Value<string>() == "fragment");

        Assert.Equal("fragment", Object(Object(proxy["streamSettings"])["sockopt"])["dialerProxy"]?.Value<string>());
        var fragmentSettings = Object(fragment["settings"]);
        Assert.Equal("tlshello", Object(fragmentSettings["fragment"])["packets"]?.Value<string>());
        var noise = Tokens(fragmentSettings["noises"]).Single();
        Assert.Equal("rand", noise["type"]?.Value<string>());
        Assert.Equal("50-150", noise["packet"]?.Value<string>());
    }

    [Fact]
    public void Build_WithOnlySelectedApps_SendsProcessesToProxyAndFallsBackToDirect()
    {
        var options = new XrayTunOptions(
            "ElyTun-Test",
            1500,
            ["1.1.1.1"],
            IncludeIpv6: false,
            BypassRules: [],
            DirectIpCidrs: [],
            BypassProcessPaths: [@"C:\Program Files\Example Browser\browser.exe"],
            PerAppMode: PerAppMode.OnlySelected);

        var config = JObject.Parse(_builder.Build(CreateNode(), tunOptions: options));
        var rules = Tokens(Object(config["routing"])["rules"]).ToArray();
        var processRule = rules.Single(rule => rule["process"] != null && rule["port"] == null);
        var dnsRule = rules.Single(rule => rule["process"] != null && rule["port"] != null);
        var catchAll = rules.Last();

        Assert.Equal("proxy", processRule["outboundTag"]?.Value<string>());
        Assert.Equal("proxy", dnsRule["outboundTag"]?.Value<string>());
        Assert.Equal(53, dnsRule["port"]?.Value<int>());
        Assert.Equal("direct", catchAll["outboundTag"]?.Value<string>());
        Assert.Equal("tcp,udp", catchAll["network"]?.Value<string>());
    }

    [Fact]
    public void Build_XhttpExtraAndKcpOptions_AreForwarded()
    {
        var xhttpNode = CreateNode();
        xhttpNode.Network = "xhttp";
        xhttpNode.Path = "/x";
        xhttpNode.Mode = "packet-up";
        xhttpNode.SetExtra("xhttp-extra", """{"scMaxEachPostBytes":1000000,"xPaddingBytes":"100-1000"}""");

        var xhttpConfig = JObject.Parse(_builder.Build(xhttpNode));
        var xhttpProxy = Tokens(xhttpConfig["outbounds"]).Single(item => item["tag"]?.Value<string>() == "proxy");
        var xhttpSettings = Object(Object(xhttpProxy["streamSettings"])["xhttpSettings"]);

        Assert.Equal("packet-up", xhttpSettings["mode"]?.Value<string>());
        Assert.Equal(1000000, Object(xhttpSettings["extra"])["scMaxEachPostBytes"]?.Value<int>());

        var kcpNode = CreateNode();
        kcpNode.Network = "mkcp";
        kcpNode.HeaderType = "dtls";
        kcpNode.SetExtra("kcp-mtu", "1200");
        kcpNode.SetExtra("kcp-congestion", "true");
        kcpNode.SetExtra("kcp-seed", "seed-42");

        var kcpConfig = JObject.Parse(_builder.Build(kcpNode));
        var kcpProxy = Tokens(kcpConfig["outbounds"]).Single(item => item["tag"]?.Value<string>() == "proxy");
        var kcpSettings = Object(Object(kcpProxy["streamSettings"])["kcpSettings"]);

        Assert.Equal(1200, kcpSettings["mtu"]?.Value<int>());
        Assert.True(kcpSettings["congestion"]?.Value<bool>());
        Assert.Equal("seed-42", kcpSettings["seed"]?.Value<string>());
        Assert.Equal("dtls", Object(kcpSettings["header"])["type"]?.Value<string>());
    }

    private static VlessNode CreateNode() => new()
    {
        Address = "198.51.100.10",
        Port = 443,
        UUID = "00000000-0000-0000-0000-000000000000",
        Network = "tcp",
        Security = "reality",
        PublicKey = "test-key",
        ShortId = "abcd",
        SNI = "example.com"
    };

    private static List<JToken> Tokens(JToken? token) => Assert.IsType<JArray>(token).Children().ToList();

    private static JObject Object(JToken? token) => Assert.IsType<JObject>(token);

    private static bool IsTunRule(JToken? rule, string outboundTag) =>
        rule?["inboundTag"]?.Values<string>().Contains("elytun-in") == true
        && rule?["outboundTag"]?.Value<string>() == outboundTag;
}
