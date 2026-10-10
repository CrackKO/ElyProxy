using System.Net;
using System.Net.Sockets;
using ElyProxy.Services;

namespace ElyProxy.Tests;

public class PacServerServiceTests
{
    [Fact]
    public async Task StartAsync_ServesNormalizedRulesAndUpdatesAnExistingServer()
    {
        using var server = new PacServerService();
        using var http = new HttpClient();

        var port = await StartServerWithRetryAsync(server, 1080, ["*.рф", "example.com"]);
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
        using var server = new PacServerService();
        using var http = new HttpClient();

        int port = await StartServerWithRetryAsync(server, 1080, null);
        using var response = await http.GetAsync($"http://127.0.0.1:{port}/not-found");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task StartAsync_KeepsCurrentServerWhenReplacementPortIsBusy()
    {
        using var occupiedListener = new TcpListener(IPAddress.Loopback, 0);
        occupiedListener.Start();
        int replacementPort = ((IPEndPoint)occupiedListener.LocalEndpoint).Port;
        using var server = new PacServerService();
        using var http = new HttpClient();

        await StartServerWithRetryAsync(server, 1080, null);

        await Assert.ThrowsAsync<SocketException>(async () => await server.StartAsync(replacementPort, 2080, null));
        string pac = await http.GetStringAsync(server.PacUrl);

        Assert.Contains("SOCKS5 127.0.0.1:1080", pac);
    }

    /// <summary>
    /// The port is reserved-then-released, so another process can steal it
    /// between ReservePort and StartAsync; retry with a fresh port instead of
    /// failing the suite on an unrelated race.
    /// </summary>
    private static async Task<int> StartServerWithRetryAsync(PacServerService server, int socksPort, string[]? rules)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var port = ReservePort();
            try
            {
                await server.StartAsync(port, socksPort, rules);
                return port;
            }
            catch (SocketException)
            {
            }
        }

        throw new InvalidOperationException("Не удалось занять loopback-порт для PAC-сервера.");
    }

    private static int ReservePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
