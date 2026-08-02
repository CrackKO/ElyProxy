using System.Net;
using System.Net.Sockets;
using ElyProxy.Services;

namespace ElyProxy.Tests;

public class PacServerServiceTests
{
    [Fact]
    public async Task StartAsync_ServesNormalizedRulesAndUpdatesAnExistingServer()
    {
        var port = ReservePort();
        using var server = new PacServerService();
        using var http = new HttpClient();

        await server.StartAsync(port, 1080, ["*.рф", "example.com"]);
        var firstPac = await http.GetStringAsync(server.PacUrl);

        Assert.Contains("SOCKS5 127.0.0.1:1080", firstPac);
        Assert.Contains("xn--p1ai", firstPac);
        Assert.Contains("example.com", firstPac);

        await server.StartAsync(port, 2080, ["internal.example"]);
        var updatedPac = await http.GetStringAsync(server.PacUrl);

        Assert.Contains("SOCKS5 127.0.0.1:2080", updatedPac);
        Assert.Contains("internal.example", updatedPac);
        Assert.DoesNotContain("example.com", updatedPac);
    }

    [Fact]
    public async Task StartAsync_ReturnsNotFoundForAnyPathExceptProxyPac()
    {
        var port = ReservePort();
        using var server = new PacServerService();
        using var http = new HttpClient();

        await server.StartAsync(port, 1080, null);
        using var response = await http.GetAsync($"http://127.0.0.1:{port}/not-found");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static int ReservePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
