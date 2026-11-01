using System.Diagnostics;
using ElyProxy.Core;
using ElyProxy.Models;

namespace ElyProxy.Tests;

/// <summary>
/// Contract tests: every generated Xray config must be accepted by the real
/// bundled Xray binary (`xray run -test`). The repo-root xray/ folder is a
/// local untracked artifact, so the test silently returns when it is missing
/// (CI covers the sing-box contract through SingBoxConfigBuilderTests).
/// </summary>
public class XrayConfigContractTests
{
    [Fact]
    public async Task GeneratedConfigs_AreAcceptedByBundledXray()
    {
        var root = FindWorkspaceRoot();
        var xrayPath = Path.Combine(root, "xray", "xray.exe");
        if (!File.Exists(xrayPath))
        {
            return;
        }

        var builder = new ConfigBuilder();
        var tunOptions = new XrayTunOptions("ElyTun", 1500, new[] { "1.1.1.1" }, true, Array.Empty<string>(), Array.Empty<string>());
        var fragmentOptions = tunOptions with
        {
            FragmentEnabled = true,
            FragmentPackets = "tlshello",
            FragmentLength = "100-200",
            FragmentInterval = "10-20",
            NoiseType = "rand",
            NoisePacket = "50-150",
            NoiseDelay = "10-16"
        };
        var onlySelectedOptions = tunOptions with
        {
            BypassProcessPaths = new[] { @"C:\Program Files\Example Browser\browser.exe" },
            PerAppMode = PerAppMode.OnlySelected
        };

        var xhttpExtraNode = RealityNode("99999999-9999-9999-9999-999999999999");
        xhttpExtraNode.Network = "xhttp";
        xhttpExtraNode.Path = "/x";
        xhttpExtraNode.Mode = "packet-up";
        xhttpExtraNode.SetExtra("xhttp-extra", """{"scMaxEachPostBytes":1000000,"xPaddingBytes":"100-1000"}""");

        var cases = new (string Name, string Json)[]
        {
            ("vless-reality", builder.Build(RealityNode("11111111-1111-1111-1111-111111111111"))),
            ("vless-ws-cdn", builder.Build(new VlessNode
            {
                Address = "cdn.example.com",
                Port = 443,
                UUID = "22222222-2222-2222-2222-222222222222",
                Network = "ws",
                Security = "tls",
                SNI = "cdn.example.com",
                Host = "cdn.example.com",
                Path = "/ws",
                Alpn = "h2,http/1.1"
            })),
            ("vless-xhttp", builder.Build(new VlessNode
            {
                Address = "x.example.com",
                Port = 443,
                UUID = "33333333-3333-3333-3333-333333333333",
                Network = "xhttp",
                Security = "tls",
                SNI = "x.example.com",
                Path = "/x",
                Host = "x.example.com",
                Mode = "packet-up"
            })),
            ("vless-mkcp", builder.Build(new VlessNode
            {
                // Xray 26.9.30 rejects VLESS without TLS unless the address is a
                // private IP or localhost (see the negative test below), so the
                // accepted-shape case uses a private address.
                Address = "10.0.0.1",
                Port = 443,
                UUID = "44444444-4444-4444-4444-444444444444",
                Network = "mkcp",
                Security = "none",
                HeaderType = "dtls",
                Seed = "seed123"
            })),
            ("vless-grpc", builder.Build(new VlessNode
            {
                Address = "g.example.com",
                Port = 443,
                UUID = "4A4A4A4A-4A4A-4A4A-4A4A-4A4A4A4A4A4A",
                Network = "grpc",
                Security = "tls",
                SNI = "g.example.com",
                Path = "grpc-service"
            })),
            ("vless-httpupgrade", builder.Build(new VlessNode
            {
                Address = "h.example.com",
                Port = 443,
                UUID = "4B4B4B4B-4B4B-4B4B-4B4B-4B4B4B4B4B4B",
                Network = "httpupgrade",
                Security = "tls",
                SNI = "h.example.com",
                Path = "/hu",
                Host = "h.example.com"
            })),
            ("vmess-ws", builder.Build(new VlessNode
            {
                Address = "vm.example.com",
                Port = 443,
                Protocol = "vmess",
                UUID = "55555555-5555-5555-5555-555555555555",
                Network = "ws",
                Security = "tls",
                SNI = "vm.example.com",
                Path = "/vm",
                Host = "vm.example.com",
                VmessSecurity = "auto",
                AlterId = "0"
            })),
            ("trojan-tls", builder.Build(new VlessNode
            {
                Address = "t.example.com",
                Port = 443,
                Protocol = "trojan",
                Password = "secret",
                Network = "tcp",
                Security = "tls",
                SNI = "t.example.com",
                Fingerprint = "chrome"
            })),
            ("shadowsocks", builder.Build(new VlessNode
            {
                Address = "ss.example.com",
                Port = 8388,
                Protocol = "shadowsocks",
                Method = "2022-blake3-aes-128-gcm",
                Password = "AAAAAAAAAAAAAAAAAAAAAA==",
                Network = "tcp",
                Security = "none"
            })),
            ("vless-tun", builder.Build(RealityNode("66666666-6666-6666-6666-666666666666"), 1080, tunOptions)),
            ("vless-fragment", builder.Build(RealityNode("77777777-7777-7777-7777-777777777777"), 1080, fragmentOptions, includeTunInbound: false)),
            ("vless-only-selected", builder.Build(RealityNode("AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA"), 1080, onlySelectedOptions, includeTunInbound: false)),
            ("vless-xhttp-extra", builder.Build(xhttpExtraNode))
        };

        var directory = Path.Combine(Path.GetTempPath(), "ElyProxy-XrayContract-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            foreach (var (name, json) in cases)
            {
                var configPath = Path.Combine(directory, name + ".json");
                await File.WriteAllTextAsync(configPath, json);
                var (exitCode, output) = await RunXrayTestAsync(xrayPath, configPath);

                Assert.True(exitCode == 0, $"{name}: xray run -test завершился с кодом {exitCode}\n{output}");
                Assert.Contains("Configuration OK", output);
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// Documents the bundled Xray policy: VLESS without TLS/encryption is only
    /// allowed for private IPs or localhost. Public-domain nodes without TLS
    /// fail at core start with a clear error, so the app surfaces the core's
    /// message instead of silently producing an invalid tunnel.
    /// </summary>
    [Fact]
    public async Task VlessWithoutTlsOnPublicDomain_IsRejectedWithClearError()
    {
        var root = FindWorkspaceRoot();
        var xrayPath = Path.Combine(root, "xray", "xray.exe");
        if (!File.Exists(xrayPath))
        {
            return;
        }

        var json = new ConfigBuilder().Build(new VlessNode
        {
            Address = "public.example.com",
            Port = 443,
            UUID = "44444444-4444-4444-4444-444444444444",
            Network = "mkcp",
            Security = "none",
            HeaderType = "dtls",
            Seed = "seed123"
        });

        var directory = Path.Combine(Path.GetTempPath(), "ElyProxy-XrayContract-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var configPath = Path.Combine(directory, "vless-none-public.json");
            await File.WriteAllTextAsync(configPath, json);
            var (exitCode, output) = await RunXrayTestAsync(xrayPath, configPath);

            Assert.NotEqual(0, exitCode);
            Assert.Contains("prohibited", output);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task<(int ExitCode, string Output)> RunXrayTestAsync(string xrayPath, string configPath)
    {
        var startInfo = new ProcessStartInfo(xrayPath, $"run -test -c \"{configPath}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            // Geo data (geoip.dat/geosite.dat) resolves relative to the xray folder.
            WorkingDirectory = Path.GetDirectoryName(xrayPath) ?? AppContext.BaseDirectory
        };

        using var process = Process.Start(startInfo);
        Assert.NotNull(process);
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
        return (process.ExitCode, stdout + stderr);
    }

    private static VlessNode RealityNode(string uuid) => new()
    {
        Address = "198.51.100.10",
        Port = 443,
        UUID = uuid,
        Flow = "xtls-rprx-vision",
        Network = "tcp",
        Security = "reality",
        PublicKey = "AED4eK-NPafy_qv24d7SIjaKAlzniOaavImjSxqgoww",
        ShortId = "abcd",
        SNI = "example.com",
        Fingerprint = "chrome"
    };

    private static string FindWorkspaceRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ElyProxy.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("ElyProxy.sln not found above " + AppContext.BaseDirectory);
    }
}
