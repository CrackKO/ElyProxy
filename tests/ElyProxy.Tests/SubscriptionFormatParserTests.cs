using ElyProxy.Models;
using ElyProxy.Services;

namespace ElyProxy.Tests;

public class SubscriptionFormatParserTests
{
    private readonly ParserService _parser = new();

    [Fact]
    public void ParseSubscriptionContent_ParsesClashYamlProxies()
    {
        const string yaml = """
            proxies:
              - name: "reality"
                type: vless
                server: r.example.com
                port: 443
                uuid: 11111111-1111-1111-1111-111111111111
                network: tcp
                tls: true
                servername: www.example.com
                flow: xtls-rprx-vision
                reality-opts:
                  public-key: PBK
                  short-id: ab12
                client-fingerprint: chrome
              - name: hy2
                type: hysteria2
                server: h.example.com
                port: 443
                password: auth
                obfs: salamander
                obfs-password: op
                sni: h.example.com
                skip-cert-verify: true
              - name: wg
                type: wireguard
                server: 1.2.3.4
                port: 51820
                private-key: PRIV
                public-key: PUB
                ip: 172.16.0.2/32
                mtu: 1420
            proxy-groups:
              - name: auto
                type: url-test
            """;

        var nodes = _parser.ParseSubscriptionContent(yaml);

        Assert.Equal(3, nodes.Count);
        var vless = nodes[0];
        Assert.Equal("vless", vless.Protocol);
        Assert.Equal("reality", vless.Security);
        Assert.Equal("PBK", vless.PublicKey);
        Assert.Equal("ab12", vless.ShortId);
        Assert.Equal("xtls-rprx-vision", vless.Flow);
        var hy2 = nodes[1];
        Assert.Equal("hysteria2", hy2.Protocol);
        Assert.Equal("auth", hy2.Password);
        Assert.Equal("salamander", hy2.Obfs);
        Assert.Equal("op", hy2.ObfsPassword);
        Assert.True(hy2.AllowInsecure);
        var wg = nodes[2];
        Assert.Equal("wireguard", wg.Protocol);
        Assert.Equal("PRIV", wg.Extra["private-key"]);
        Assert.Equal("PUB", wg.Extra["public-key"]);
        Assert.Equal("172.16.0.2/32", wg.Extra["address"]);
        Assert.Equal("1420", wg.Extra["mtu"]);
    }

    [Fact]
    public void ParseSubscriptionContent_ParsesXrayJsonOutbounds()
    {
        const string json = """
            {"outbounds":[
              {"tag":"proxy","protocol":"vless","settings":{"vnext":[{"address":"x.example.com","port":443,"users":[{"id":"22222222-2222-2222-2222-222222222222","flow":"xtls-rprx-vision","encryption":"none"}]}]},"streamSettings":{"network":"ws","security":"tls","tlsSettings":{"serverName":"x.example.com","fingerprint":"chrome"},"wsSettings":{"path":"/ws","headers":{"Host":"x.example.com"}}}},
              {"tag":"direct","protocol":"freedom"}
            ]}
            """;

        var nodes = _parser.ParseSubscriptionContent(json);

        var node = Assert.Single(nodes);
        Assert.Equal("vless", node.Protocol);
        Assert.Equal("x.example.com", node.Address);
        Assert.Equal(443, node.Port);
        Assert.Equal("ws", node.Network);
        Assert.Equal("tls", node.Security);
        Assert.Equal("/ws", node.Path);
        Assert.Equal("x.example.com", node.Host);
    }

    [Fact]
    public void ParseSubscriptionContent_ParsesSingBoxJsonOutbounds()
    {
        const string json = """
            {"outbounds":[
              {"type":"hysteria2","tag":"proxy","server":"h.example.com","server_port":443,"password":"auth","obfs":{"type":"salamander","password":"op"},"tls":{"enabled":true,"server_name":"h.example.com","insecure":true}},
              {"type":"wireguard","tag":"wg","server":"1.2.3.4","server_port":51820,"private_key":"PRIV","peer_public_key":"PUB","local_address":["172.16.0.2/32"]},
              {"type":"direct","tag":"direct"}
            ]}
            """;

        var nodes = _parser.ParseSubscriptionContent(json);

        Assert.Equal(2, nodes.Count);
        Assert.Equal("hysteria2", nodes[0].Protocol);
        Assert.Equal("auth", nodes[0].Password);
        Assert.Equal("salamander", nodes[0].Obfs);
        Assert.True(nodes[0].AllowInsecure);
        Assert.Equal("wireguard", nodes[1].Protocol);
        Assert.Equal("PRIV", nodes[1].Extra["private-key"]);
        Assert.Equal("PUB", nodes[1].Extra["public-key"]);
    }

    [Fact]
    public void ParseSubscriptionContent_ParsesClashJsonProxies()
    {
        const string json = """
            {"proxies":[{"name":"ss","type":"ss","server":"s.example.com","port":8388,"cipher":"aes-256-gcm","password":"secret"}]}
            """;

        var nodes = _parser.ParseSubscriptionContent(json);

        var node = Assert.Single(nodes);
        Assert.Equal("shadowsocks", node.Protocol);
        Assert.Equal("aes-256-gcm", node.Method);
        Assert.Equal("secret", node.Password);
    }

    [Fact]
    public void ParseSubscriptionContent_ParsesArrayOfFullXrayConfigsWithRemarks()
    {
        const string json = """
            [
              {
                "remarks": "Германия",
                "outbounds": [
                  {"tag":"proxy-a","protocol":"vless","settings":{"vnext":[{"address":"de.example.com","port":443,"users":[{"id":"11111111-1111-1111-1111-111111111111"}]}]},"streamSettings":{"network":"tcp","security":"reality","realitySettings":{"serverName":"example.com","publicKey":"PBK","shortId":"ab"}}},
                  {"tag":"direct","protocol":"freedom"}
                ]
              },
              {
                "remarks": "Все локации",
                "outbounds": [
                  {"tag":"proxy-a","protocol":"vless","settings":{"vnext":[{"address":"de.example.com","port":443,"users":[{"id":"11111111-1111-1111-1111-111111111111"}]}]},"streamSettings":{"network":"tcp","security":"reality","realitySettings":{"serverName":"example.com","publicKey":"PBK","shortId":"ab"}}},
                  {"tag":"proxy-b","protocol":"vless","settings":{"vnext":[{"address":"nl.example.com","port":8443,"users":[{"id":"22222222-2222-2222-2222-222222222222"}]}]},"streamSettings":{"network":"ws","security":"tls","tlsSettings":{"serverName":"nl.example.com"},"wsSettings":{"path":"/ws","headers":{"Host":"nl.example.com"}}}},
                  {"tag":"block","protocol":"blackhole"}
                ]
              }
            ]
            """;

        var nodes = _parser.ParseSubscriptionContent(json);

        Assert.Equal(2, nodes.Count);
        Assert.Equal("Германия", nodes[0].Name);
        Assert.Equal("de.example.com", nodes[0].Address);
        Assert.Equal("vless", nodes[0].Protocol);
        Assert.Equal("reality", nodes[0].Security);
        Assert.Equal("Все локации · nl", nodes[1].Name);
        Assert.Equal("nl.example.com", nodes[1].Address);
        Assert.Equal("ws", nodes[1].Network);
    }

    [Fact]
    public void ParseUri_ParsesAnyTlsSshAndHttpProxyLinks()
    {
        var anytls = _parser.ParseUri("anytls://secret@a.example.com:443?sni=a.example.com&insecure=1#Any");
        var ssh = _parser.ParseUri("ssh://root:pw@b.example.com:22#SSH");
        var http = _parser.ParseUri("http://user:pass@c.example.com:3128#HTTP");
        var https = _parser.ParseUri("https://user:pass@d.example.com:8443#HTTPS");

        Assert.NotNull(anytls);
        Assert.Equal("anytls", anytls.Protocol);
        Assert.Equal("secret", anytls.Password);
        Assert.True(anytls.AllowInsecure);
        Assert.NotNull(ssh);
        Assert.Equal("ssh", ssh.Protocol);
        Assert.Equal("root", ssh.Username);
        Assert.Equal("pw", ssh.Password);
        Assert.NotNull(http);
        Assert.Equal("http", http.Protocol);
        Assert.Equal("user", http.Username);
        Assert.Equal("none", http.Security);
        Assert.NotNull(https);
        Assert.Equal("tls", https.Security);
        Assert.Null(_parser.ParseUri("http://example.com/no-credentials"));
    }

    [Fact]
    public void ParseUri_ParsesNaiveLinks()
    {
        var naive = _parser.ParseUri("naive+https://user:pass@n.example.com:443?sni=n.example.com&insecure=1#Naive");

        Assert.NotNull(naive);
        Assert.Equal("naive", naive.Protocol);
        Assert.Equal("user", naive.Username);
        Assert.Equal("pass", naive.Password);
        Assert.Equal("tls", naive.Security);
        Assert.True(naive.AllowInsecure);
    }

    [Fact]
    public void ParseSubscriptionContent_PairsSingBoxShadowTlsChain()
    {
        const string json = """
            {"outbounds":[
              {"type":"shadowsocks","tag":"ss-out","server":"st.example.com","server_port":443,"method":"2022-blake3-aes-128-gcm","password":"AAAAAAAAAAAAAAAAAAAAAA==","detour":"st-out"},
              {"type":"shadowtls","tag":"st-out","server":"st.example.com","server_port":443,"version":3,"password":"st-pass","tls":{"enabled":true,"server_name":"st.example.com"}},
              {"type":"direct","tag":"direct"}
            ]}
            """;

        var nodes = _parser.ParseSubscriptionContent(json);

        var node = Assert.Single(nodes);
        Assert.Equal("shadowtls", node.Protocol);
        Assert.Equal("st-pass", node.Password);
        Assert.Equal("3", node.Extra["shadowtls-version"]);
        Assert.Equal("2022-blake3-aes-128-gcm", node.Extra["ss-method"]);
        Assert.Equal("AAAAAAAAAAAAAAAAAAAAAA==", node.Extra["ss-password"]);
    }
}
