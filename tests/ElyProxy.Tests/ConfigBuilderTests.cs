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
