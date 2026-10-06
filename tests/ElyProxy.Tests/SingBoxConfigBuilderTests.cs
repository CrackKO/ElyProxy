using System.Diagnostics;
using ElyProxy.Core;
using ElyProxy.Models;
using Newtonsoft.Json.Linq;

namespace ElyProxy.Tests;

public class SingBoxConfigBuilderTests
{
    [Fact]
    public void BuildTun_ExcludesLocalAdapterNetworksAndKeepsSystemStack()
    {
        var options = new XrayTunOptions(
            "ElyTun-Test",
            1500,
            ["1.1.1.1"],
            IncludeIpv6: true,
            BypassRules: ["*.ru", "26.0.0.0/8"],
            DirectIpCidrs: ["192.168.1.0/24"]);

        var config = JObject.Parse(new SingBoxConfigBuilder().BuildTun(CreateNode(), 1080, options));
        var tun = GetTunInbound(config);
        var route = GetRoute(config);

        Assert.True(tun.Value<bool>("auto_route"));
        Assert.Null(tun["strict_route"]);
        Assert.Equal("system", tun.Value<string>("stack"));
        Assert.Equal("warn", Assert.IsType<JObject>(config["log"]).Value<string>("level"));
        Assert.Contains("192.168.1.0/24", GetArray(tun, "route_exclude_address").Values<string>());
        Assert.Contains("26.0.0.0/8", GetArray(tun, "route_exclude_address").Values<string>());
        Assert.Contains("10.0.0.0/8", GetArray(tun, "route_exclude_address").Values<string>());
        Assert.Contains("fdfe:dcba:9876::1/126", GetArray(tun, "address").Values<string>());
        Assert.Equal("proxy", route.Value<string>("final"));
        Assert.Equal("dns-direct", route.Value<string>("default_domain_resolver"));

        var directDomainRule = GetArray(route, "rules").Children<JObject>().Single(rule => rule["domain_suffix"] != null);
        Assert.Contains("ru", GetArray(directDomainRule, "domain_suffix").Values<string>());
    }

    [Fact]
    public void BuildTun_MapsRealityWebSocketToSingBoxOutbound()
    {
        var node = CreateNode();
        node.Network = "ws";
        node.Path = "/ws";
        node.Host = "edge.example.com";

        var config = JObject.Parse(new SingBoxConfigBuilder().BuildTun(node, 1080, CreateOptions()));
        var proxy = GetArray(config, "outbounds").Children<JObject>().Single(outbound => outbound.Value<string>("tag") == "proxy");

        Assert.Equal("vless", proxy.Value<string>("type"));
        Assert.Equal("xtls-rprx-vision", proxy.Value<string>("flow"));
        var tls = Assert.IsType<JObject>(proxy["tls"]);
        var transport = Assert.IsType<JObject>(proxy["transport"]);
        Assert.True(tls.Value<bool>("enabled"));
        Assert.True(Assert.IsType<JObject>(tls["reality"]).Value<bool>("enabled"));
        Assert.Equal("ws", transport.Value<string>("type"));
        Assert.Equal("/ws", transport.Value<string>("path"));
        Assert.Equal("edge.example.com", Assert.IsType<JObject>(transport["headers"]).Value<string>("Host"));
    }

    [Fact]
    public void BuildTun_MapsHttp2TransportToSingBoxHttpTransport()
    {
        var node = CreateNode();
        node.Network = "h2";
        node.Path = "/vless";
        node.Host = "one.example.com,two.example.com";

        var config = JObject.Parse(new SingBoxConfigBuilder().BuildTun(node, 1080, CreateOptions()));
        var proxy = GetArray(config, "outbounds").Children<JObject>().Single(outbound => outbound.Value<string>("tag") == "proxy");
        var transport = Assert.IsType<JObject>(proxy["transport"]);

        Assert.Equal("http", transport.Value<string>("type"));
        Assert.Equal("/vless", transport.Value<string>("path"));
        Assert.Equal(["one.example.com", "two.example.com"], GetArray(transport, "host").Values<string>());
    }

    [Fact]
    public void BuildTun_SendsBypassDomainsToDirectDnsAndOtherDomainsToProxyDoh()
    {
        var options = CreateOptions() with
        {
            DnsServers = ["192.168.1.1"],
            BypassRules = ["*.ru", "full:lan.example", "keyword:intranet"]
        };
        var config = JObject.Parse(new SingBoxConfigBuilder().BuildTun(CreateNode(), 1080, options));
        var dns = Assert.IsType<JObject>(config["dns"]);
        var direct = GetArray(dns, "servers").Children<JObject>().Single(server => server.Value<string>("tag") == "dns-direct");
        var remote = GetArray(dns, "servers").Children<JObject>().Single(server => server.Value<string>("tag") == "dns-remote");
        var directRule = GetArray(dns, "rules").Children<JObject>().Single(rule => rule["domain_suffix"] != null);

        Assert.Equal("udp", direct.Value<string>("type"));
        Assert.Equal("192.168.1.1", direct.Value<string>("server"));
        Assert.Null(direct["detour"]);
        Assert.Equal("https", remote.Value<string>("type"));
        Assert.Equal("proxy", remote.Value<string>("detour"));
        Assert.Equal("dns-remote", dns.Value<string>("final"));
        Assert.Equal("dns-direct", directRule.Value<string>("server"));
        Assert.Contains("ru", GetArray(directRule, "domain_suffix").Values<string>());
    }

    [Fact]
    public async Task BuildTun_RoutesSelectedApplicationsDirectBeforeDomainRules()
    {
        const string browserPath = @"C:\Program Files\Example Browser\browser.exe";
        var options = CreateOptions() with
        {
            BypassRules = ["*.example.org"],
            BypassProcessPaths = [browserPath]
        };

        var config = JObject.Parse(new SingBoxConfigBuilder().BuildTun(CreateNode(), 1080, options));
        var routeRules = GetArray(GetRoute(config), "rules").Children<JObject>().ToArray();
        var applicationRuleIndex = Array.FindIndex(routeRules, rule => rule["process_path"] != null);
        var domainRuleIndex = Array.FindIndex(routeRules, rule => rule["domain_suffix"] != null);
        Assert.True(applicationRuleIndex >= 0);
        Assert.True(domainRuleIndex >= 0);
        var applicationRule = routeRules[applicationRuleIndex];
        var processDnsRule = GetArray(Assert.IsType<JObject>(config["dns"]), "rules")
            .Children<JObject>()
            .Single(rule => rule["process_path"] != null);

        Assert.True(applicationRuleIndex < domainRuleIndex);
        Assert.Equal("direct", applicationRule.Value<string>("outbound"));
        Assert.Contains(browserPath, GetArray(applicationRule, "process_path").Values<string>());
        Assert.Equal("dns-direct", processDnsRule.Value<string>("server"));
        Assert.Contains(browserPath, GetArray(processDnsRule, "process_path").Values<string>());

        await AssertConfigAcceptedAsync(CreateNode(), options);
    }

    [Fact]
    public void BuildSystemProxy_RoutesSelectedApplicationsDirectBeforeDomainRules()
    {
        const string browserPath = @"C:\Program Files\Example Browser\browser.exe";
        var options = CreateOptions() with
        {
            BypassRules = ["*.example.org"],
            BypassProcessPaths = [browserPath]
        };

        var config = JObject.Parse(new SingBoxConfigBuilder().BuildSystemProxy(CreateNode(), 1080, options));
        var routeRules = GetArray(GetRoute(config), "rules").Children<JObject>().ToArray();
        var applicationRuleIndex = Array.FindIndex(routeRules, rule => rule["process_path"] != null);
        var domainRuleIndex = Array.FindIndex(routeRules, rule => rule["domain_suffix"] != null);

        Assert.True(applicationRuleIndex >= 0);
        Assert.True(applicationRuleIndex < domainRuleIndex);
        Assert.Equal("direct", routeRules[applicationRuleIndex].Value<string>("outbound"));
        Assert.Contains(browserPath, GetArray(routeRules[applicationRuleIndex], "process_path").Values<string>());
    }

    [Fact]
    public async Task BuildSystemProxy_SelectedApplicationRuleIsAcceptedByBundledSingBox()
    {
        var options = CreateOptions() with
        {
            BypassProcessPaths = [@"C:\Program Files\Example Browser\browser.exe"]
        };

        await AssertConfigAcceptedAsync(new SingBoxConfigBuilder().BuildSystemProxy(CreateNode(), 1080, options));
    }

    [Fact]
    public async Task BuildTun_IsAcceptedByBundledSingBox()
    {
        await AssertConfigAcceptedAsync(CreateNode());
    }

    [Fact]
    public async Task BuildTun_Http2TransportIsAcceptedByBundledSingBox()
    {
        var node = CreateNode();
        node.Network = "h2";
        node.Path = "/vless";
        node.Host = "edge.example.com";

        await AssertConfigAcceptedAsync(node);
    }

    [Fact]
    public async Task BuildTun_RoscomVpnDefault_UsesBundledRuleSetsAndIsAccepted()
    {
        const string browserPath = @"C:\Program Files\Example Browser\browser.exe";
        var options = CreateOptions() with
        {
            RoutingPreset = RoutingPreset.RoscomVpnDefault,
            BypassProcessPaths = [browserPath]
        };
        var config = JObject.Parse(new SingBoxConfigBuilder().BuildTun(CreateNode(), 1080, options));
        var route = GetRoute(config);
        var dns = Assert.IsType<JObject>(config["dns"]);
        var ruleSets = GetArray(route, "rule_set").Children<JObject>().ToArray();
        var routeRules = GetArray(route, "rules").Children<JObject>().ToArray();

        Assert.Equal(22, ruleSets.Length);
        Assert.All(ruleSets, ruleSet =>
        {
            Assert.Equal("local", ruleSet.Value<string>("type"));
            Assert.Equal("binary", ruleSet.Value<string>("format"));
            Assert.True(File.Exists(ruleSet.Value<string>("path")), $"Missing rule-set: {ruleSet.Value<string>("path")}");
        });
        var blockIndex = Array.FindIndex(routeRules, rule => rule.Value<string>("outbound") == "block");
        var proxyIndex = Array.FindIndex(routeRules, rule => rule.Value<string>("outbound") == "proxy");
        var directIndex = Array.FindIndex(routeRules, rule => rule.Value<string>("outbound") == "direct" && rule["process_path"] == null);
        var applicationIndex = Array.FindIndex(routeRules, rule => rule["process_path"] != null);
        Assert.True(blockIndex >= 0 && proxyIndex >= 0 && directIndex >= 0);
        Assert.True(blockIndex < proxyIndex && proxyIndex < directIndex, "Expected block → proxy → direct order.");
        Assert.True(applicationIndex >= 0 && applicationIndex < blockIndex, "Application bypass must take precedence over preset rules.");
        Assert.Equal("direct", routeRules[applicationIndex].Value<string>("outbound"));
        Assert.Contains(browserPath, GetArray(routeRules[applicationIndex], "process_path").Values<string>());
        var processDnsRule = GetArray(dns, "rules").Children<JObject>().Single(rule => rule["process_path"] != null);
        Assert.Equal("dns-domestic", processDnsRule.Value<string>("server"));
        Assert.Contains(browserPath, GetArray(processDnsRule, "process_path").Values<string>());
        Assert.Equal("8.8.8.8", GetArray(dns, "servers").Children<JObject>()
            .Single(server => server.Value<string>("tag") == "dns-remote").Value<string>("server"));
        var domesticDns = GetArray(dns, "servers").Children<JObject>()
            .Single(server => server.Value<string>("tag") == "dns-domestic");
        Assert.Equal("77.88.8.8", domesticDns.Value<string>("server"));
        Assert.Equal("common.dot.dns.yandex.net", Assert.IsType<JObject>(domesticDns["tls"]).Value<string>("server_name"));
        var hosts = GetArray(dns, "servers").Children<JObject>()
            .Single(server => server.Value<string>("tag") == "dns-hosts");
        Assert.Equal("213.24.64.175", Assert.IsType<JObject>(hosts["predefined"]).Value<string>("lkfl2.nalog.ru"));

        await AssertConfigAcceptedAsync(CreateNode(), options);
    }

    private static Task AssertConfigAcceptedAsync(VlessNode node, XrayTunOptions? options = null) =>
        AssertConfigAcceptedAsync(new SingBoxConfigBuilder().BuildTun(node, 1080, options ?? CreateOptions()));

    private static async Task AssertConfigAcceptedAsync(string configJson)
    {
        var root = FindWorkspaceRoot();
        var executable = Path.Combine(root, "sing-box", "sing-box.exe");
        Assert.True(File.Exists(executable), "Bundled sing-box.exe was not found.");

        var configPath = Path.Combine(Path.GetTempPath(), $"elyproxy-singbox-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(configPath, configJson);

        try
        {
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = executable,
                Arguments = $"check -c \"{configPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });
            Assert.NotNull(process);

            using (process)
            {
                var output = await process.StandardOutput.ReadToEndAsync();
                var error = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();
                Assert.True(process.ExitCode == 0, $"sing-box check failed ({process.ExitCode}): {output}\n{error}");
            }
        }
        finally
        {
            File.Delete(configPath);
        }
    }

    private static XrayTunOptions CreateOptions() => new(
        "ElyTun-Test",
        1500,
        ["1.1.1.1"],
        IncludeIpv6: false,
        BypassRules: [],
        DirectIpCidrs: []);

    private static VlessNode CreateNode() => new()
    {
        Address = "198.51.100.10",
        Port = 443,
        UUID = "00000000-0000-0000-0000-000000000000",
        Flow = "xtls-rprx-vision",
        Network = "tcp",
        Security = "reality",
        PublicKey = "AED4eK-NPafy_qv24d7SIjaKAlzniOaavImjSxqgoww",
        ShortId = "abcd",
        SNI = "example.com",
        Fingerprint = "chrome"
    };

    private static JObject GetTunInbound(JObject config) =>
        GetArray(config, "inbounds").Children<JObject>().Single(inbound => inbound.Value<string>("tag") == "tun-in");

    private static JObject GetRoute(JObject config) => Assert.IsType<JObject>(config["route"]);

    private static JArray GetArray(JObject parent, string property) => Assert.IsType<JArray>(parent[property]);

    private static string FindWorkspaceRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ElyProxy.sln")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Workspace root was not found.");
    }
}
