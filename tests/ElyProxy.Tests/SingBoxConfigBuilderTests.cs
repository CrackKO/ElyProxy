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

    [Fact]
    public void BuildTun_MapsHysteria2Outbound()
    {
        var node = CreateHysteria2Node();

        var config = JObject.Parse(new SingBoxConfigBuilder().BuildTun(node, 1080, CreateOptions()));
        var proxy = GetArray(config, "outbounds").Children<JObject>().Single(outbound => outbound.Value<string>("tag") == "proxy");
        var obfs = Assert.IsType<JObject>(proxy["obfs"]);
        var tls = Assert.IsType<JObject>(proxy["tls"]);

        Assert.Equal("hysteria2", proxy.Value<string>("type"));
        Assert.Equal("auth-token", proxy.Value<string>("password"));
        Assert.Equal("salamander", obfs.Value<string>("type"));
        Assert.Equal("obfs-secret", obfs.Value<string>("password"));
        Assert.Equal(["2000:3000"], GetArray(proxy, "server_ports").Values<string>());
        Assert.True(tls.Value<bool>("enabled"));
        Assert.True(tls.Value<bool>("insecure"));
        Assert.Equal("hy.example.com", tls.Value<string>("server_name"));
    }

    [Fact]
    public async Task BuildTun_Hysteria2IsAcceptedByBundledSingBox()
    {
        await AssertConfigAcceptedAsync(CreateHysteria2Node());
    }

    [Fact]
    public void BuildTun_MapsTuicOutbound()
    {
        var node = CreateTuicNode();

        var config = JObject.Parse(new SingBoxConfigBuilder().BuildTun(node, 1080, CreateOptions()));
        var proxy = GetArray(config, "outbounds").Children<JObject>().Single(outbound => outbound.Value<string>("tag") == "proxy");
        var tls = Assert.IsType<JObject>(proxy["tls"]);

        Assert.Equal("tuic", proxy.Value<string>("type"));
        Assert.Equal("44444444-4444-4444-4444-444444444444", proxy.Value<string>("uuid"));
        Assert.Equal("p@ss", proxy.Value<string>("password"));
        Assert.Equal("bbr", proxy.Value<string>("congestion_control"));
        Assert.Equal("native", proxy.Value<string>("udp_relay_mode"));
        Assert.True(tls.Value<bool>("enabled"));
        Assert.Equal(["h3"], GetArray(tls, "alpn").Values<string>());
    }

    [Fact]
    public async Task BuildTun_TuicIsAcceptedByBundledSingBox()
    {
        await AssertConfigAcceptedAsync(CreateTuicNode());
    }

    [Fact]
    public async Task BuildTun_VmessIsAcceptedByBundledSingBox()
    {
        var node = CreateNode();
        node.Protocol = "vmess";
        node.Flow = string.Empty;
        node.UUID = "33333333-3333-3333-3333-333333333333";
        node.Security = "tls";
        node.Network = "ws";
        node.Path = "/vm";
        node.Host = "cdn.example.com";

        var config = JObject.Parse(new SingBoxConfigBuilder().BuildTun(node, 1080, CreateOptions()));
        var proxy = GetArray(config, "outbounds").Children<JObject>().Single(outbound => outbound.Value<string>("tag") == "proxy");

        Assert.Equal("vmess", proxy.Value<string>("type"));
        Assert.Equal("auto", proxy.Value<string>("security"));

        await AssertConfigAcceptedAsync(node);
    }

    [Fact]
    public void BuildTun_RejectsXhttpTransportForSingBox()
    {
        var node = CreateNode();
        node.Network = "xhttp";
        node.Path = "/xhttp";

        Assert.Throws<NotSupportedException>(() => new SingBoxConfigBuilder().BuildTun(node, 1080, CreateOptions()));
    }

    [Fact]
    public void BuildTun_RejectsVlessEncryptionForSingBox()
    {
        var node = CreateNode();
        node.Encryption = "mlkem768x25519plus.native.0rtt.test";

        Assert.Throws<NotSupportedException>(() => new SingBoxConfigBuilder().BuildTun(node, 1080, CreateOptions()));
    }

    [Fact]
    public async Task BuildTun_WithFragment_AddsTlsFragmentAndIsAccepted()
    {
        var options = CreateOptions() with
        {
            FragmentEnabled = true,
            FragmentInterval = "10-20",
            RecordFragment = true
        };

        var config = JObject.Parse(new SingBoxConfigBuilder().BuildTun(CreateNode(), 1080, options));
        var proxy = GetArray(config, "outbounds").Children<JObject>().Single(outbound => outbound.Value<string>("tag") == "proxy");
        var tls = Assert.IsType<JObject>(proxy["tls"]);

        Assert.True(tls.Value<bool>("fragment"));
        Assert.Equal("10ms", tls.Value<string>("fragment_fallback_delay"));
        Assert.True(tls.Value<bool>("record_fragment"));

        await AssertConfigAcceptedAsync(CreateNode(), options);
    }

    [Fact]
    public async Task BuildTun_WireGuardUsesEndpointAndIsAccepted()
    {
        var node = new VlessNode
        {
            Protocol = "wireguard",
            Address = "198.51.100.20",
            Port = 51820
        };
        node.Extra["private-key"] = "YNXtAzepDqRv9H52osJVDQnznT5AM11eCK3ESpwSt04=";
        node.Extra["public-key"] = "Z1XXLsKYkYxuiYjJIkRvtIKFepCYHTgON+GwPq7SOV4=";
        node.Extra["allowed-ips"] = "0.0.0.0/0";
        node.Extra["address"] = "172.16.0.2/32";
        node.Extra["mtu"] = "1420";

        var config = JObject.Parse(new SingBoxConfigBuilder().BuildTun(node, 1080, CreateOptions()));
        var endpoints = GetArray(config, "endpoints");
        var endpoint = Assert.Single(endpoints.Children<JObject>());

        Assert.Equal("wireguard", endpoint.Value<string>("type"));
        Assert.Equal("proxy", endpoint.Value<string>("tag"));
        Assert.DoesNotContain(GetArray(config, "outbounds").Children<JObject>(), outbound => outbound.Value<string>("tag") == "proxy");

        await AssertConfigAcceptedAsync(config.ToString());
    }

    [Fact]
    public async Task BuildTun_AnyTlsAndSshAreAccepted()
    {
        var anytls = CreateNode();
        anytls.Protocol = "anytls";
        anytls.Password = "secret";
        anytls.Flow = string.Empty;

        var anytlsConfig = JObject.Parse(new SingBoxConfigBuilder().BuildTun(anytls, 1080, CreateOptions()));
        var anytlsProxy = GetArray(anytlsConfig, "outbounds").Children<JObject>().Single(outbound => outbound.Value<string>("tag") == "proxy");
        Assert.Equal("anytls", anytlsProxy.Value<string>("type"));
        Assert.True(Assert.IsType<JObject>(anytlsProxy["tls"]).Value<bool>("enabled"));
        await AssertConfigAcceptedAsync(anytlsConfig.ToString());

        var ssh = CreateNode();
        ssh.Protocol = "ssh";
        ssh.Security = "none";
        ssh.Username = "root";
        ssh.Password = "pw";

        var sshConfig = JObject.Parse(new SingBoxConfigBuilder().BuildTun(ssh, 1080, CreateOptions()));
        var sshProxy = GetArray(sshConfig, "outbounds").Children<JObject>().Single(outbound => outbound.Value<string>("tag") == "proxy");
        Assert.Equal("ssh", sshProxy.Value<string>("type"));
        Assert.Equal("root", sshProxy.Value<string>("user"));
        await AssertConfigAcceptedAsync(sshConfig.ToString());
    }

    [Fact]
    public async Task BuildTun_OnlySelectedApps_RoutesProcessesToProxyAndEverythingElseDirect()
    {
        var options = CreateOptions() with
        {
            BypassProcessPaths = [@"C:\Program Files\Example Browser\browser.exe"],
            PerAppMode = PerAppMode.OnlySelected
        };

        var config = JObject.Parse(new SingBoxConfigBuilder().BuildTun(CreateNode(), 1080, options));
        var route = GetRoute(config);
        var processRule = GetArray(route, "rules").Children<JObject>().Single(rule => rule["process_path"] != null);
        var dns = Assert.IsType<JObject>(config["dns"]);
        var processDnsRule = GetArray(dns, "rules").Children<JObject>().Single(rule => rule["process_path"] != null);

        Assert.Equal("proxy", processRule.Value<string>("outbound"));
        Assert.Equal("direct", route.Value<string>("final"));
        Assert.Equal("dns-remote", processDnsRule.Value<string>("server"));
        Assert.Equal("dns-direct", dns.Value<string>("final"));

        await AssertConfigAcceptedAsync(CreateNode(), options);
    }

    [Fact]
    public async Task BuildTun_WithClashApi_IsAcceptedAndExposesController()
    {
        var options = CreateOptions() with
        {
            ClashApiPort = 1082,
            ClashApiSecret = "secret-token"
        };

        var config = JObject.Parse(new SingBoxConfigBuilder().BuildTun(CreateNode(), 1080, options));
        var clashApi = Assert.IsType<JObject>(Assert.IsType<JObject>(config["experimental"])["clash_api"]);

        Assert.Equal("127.0.0.1:1082", clashApi.Value<string>("external_controller"));
        Assert.Equal("secret-token", clashApi.Value<string>("secret"));

        await AssertConfigAcceptedAsync(CreateNode(), options);
    }

    [Fact]
    public void BuildTun_NaiveUsesTlsOutbound()
    {
        var naive = new VlessNode
        {
            Protocol = "naive",
            Address = "n.example.com",
            Port = 443,
            Username = "user",
            Password = "pass",
            Security = "tls",
            SNI = "n.example.com"
        };

        var naiveConfig = JObject.Parse(new SingBoxConfigBuilder().BuildTun(naive, 1080, CreateOptions()));
        var naiveProxy = GetArray(naiveConfig, "outbounds").Children<JObject>().Single(outbound => outbound.Value<string>("tag") == "proxy");

        Assert.Equal("naive", naiveProxy.Value<string>("type"));
        Assert.Equal("user", naiveProxy.Value<string>("username"));
        Assert.True(Assert.IsType<JObject>(naiveProxy["tls"]).Value<bool>("enabled"));
    }

    [Fact]
    public async Task BuildTun_ShadowTlsChainIsAccepted()
    {
        var shadowTls = new VlessNode
        {
            Protocol = "shadowtls",
            Address = "st.example.com",
            Port = 443,
            Password = "st-pass",
            Security = "tls",
            SNI = "st.example.com"
        };
        shadowTls.SetExtra("shadowtls-version", "3");
        shadowTls.SetExtra("ss-method", "2022-blake3-aes-128-gcm");
        shadowTls.SetExtra("ss-password", "AAAAAAAAAAAAAAAAAAAAAA==");

        var shadowTlsConfig = JObject.Parse(new SingBoxConfigBuilder().BuildTun(shadowTls, 1080, CreateOptions()));
        var outbounds = GetArray(shadowTlsConfig, "outbounds").Children<JObject>().ToArray();
        var inner = outbounds.Single(outbound => outbound.Value<string>("tag") == "proxy");
        var camo = outbounds.Single(outbound => outbound.Value<string>("tag") == "proxy-shadowtls");

        Assert.Equal("shadowsocks", inner.Value<string>("type"));
        Assert.Equal("proxy-shadowtls", inner.Value<string>("detour"));
        Assert.Equal("shadowtls", camo.Value<string>("type"));
        Assert.Equal(3, camo.Value<int>("version"));
        await AssertConfigAcceptedAsync(shadowTlsConfig.ToString());
    }

    [Fact]
    public void BuildTun_ShadowTlsWithoutShadowsocksCredentialsIsRejected()
    {
        var node = new VlessNode
        {
            Protocol = "shadowtls",
            Address = "st.example.com",
            Port = 443,
            Password = "st-pass",
            Security = "tls"
        };

        Assert.Throws<NotSupportedException>(() => new SingBoxConfigBuilder().BuildTun(node, 1080, CreateOptions()));
    }

    [Fact]
    public async Task BuildTun_RoscomVpnDefault_AppliesCustomBypassRulesBeforePreset()
    {
        var options = CreateOptions() with
        {
            RoutingPreset = RoutingPreset.RoscomVpnDefault,
            BypassRules = ["*.ru", "26.0.0.0/8"]
        };

        var config = JObject.Parse(new SingBoxConfigBuilder().BuildTun(CreateNode(), 1080, options));
        var rules = GetArray(GetRoute(config), "rules").Children<JObject>().ToArray();
        var customDomainIndex = Array.FindIndex(rules, rule => rule["domain_suffix"] != null);
        var ruleSetIndex = Array.FindIndex(rules, rule => rule["rule_set"] != null);

        Assert.True(customDomainIndex >= 0, "Custom bypass domain rule was not generated.");
        Assert.True(ruleSetIndex >= 0);
        Assert.True(customDomainIndex < ruleSetIndex, "Custom bypass must take precedence over preset rule-sets.");
        Assert.Equal("direct", rules[customDomainIndex].Value<string>("outbound"));

        var dns = Assert.IsType<JObject>(config["dns"]);
        var customDnsRule = GetArray(dns, "rules").Children<JObject>().First(rule => rule["domain_suffix"] != null);
        Assert.Equal("dns-domestic", customDnsRule.Value<string>("server"));

        await AssertConfigAcceptedAsync(CreateNode(), options);
    }

    [Fact]
    public async Task BuildTun_WithTlsSpoof_AddsSpoofAndIsAccepted()
    {
        var node = CreateNode();
        node.Security = "tls";
        node.PublicKey = string.Empty;
        node.ShortId = string.Empty;
        node.SNI = "example.com";
        var options = CreateOptions() with
        {
            TlsSpoofEnabled = true,
            TlsSpoofSni = "www.google.com",
            TlsSpoofMethod = "wrong-checksum"
        };

        var config = JObject.Parse(new SingBoxConfigBuilder().BuildTun(node, 1080, options));
        var proxy = GetArray(config, "outbounds").Children<JObject>().Single(outbound => outbound.Value<string>("tag") == "proxy");
        var tls = Assert.IsType<JObject>(proxy["tls"]);

        Assert.Equal("www.google.com", tls.Value<string>("spoof"));
        Assert.Equal("wrong-checksum", tls.Value<string>("spoof_method"));

        await AssertConfigAcceptedAsync(node, options);
    }

    private static VlessNode CreateHysteria2Node() => new()
    {
        Protocol = "hysteria2",
        Address = "hy.example.com",
        Port = 443,
        Password = "auth-token",
        Security = "tls",
        SNI = "hy.example.com",
        Obfs = "salamander",
        ObfsPassword = "obfs-secret",
        ServerPorts = "2000:3000",
        AllowInsecure = true
    };

    private static VlessNode CreateTuicNode() => new()
    {
        Protocol = "tuic",
        Address = "tuic.example.com",
        Port = 443,
        UUID = "44444444-4444-4444-4444-444444444444",
        Password = "p@ss",
        Security = "tls",
        SNI = "tuic.example.com",
        Alpn = "h3",
        CongestionControl = "bbr",
        UdpRelayMode = "native"
    };

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
