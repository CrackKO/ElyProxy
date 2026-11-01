using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using ElyProxy.Services;

namespace ElyProxy.Tests;

public sealed class TrafficMonitorServiceTests
{
    [Fact]
    public async Task Start_ReceivesTrafficLinesAndSendsBearerSecret()
    {
        using var server = new FakeClashServer(new[] { "{\"up\":100,\"down\":200}", "{\"up\":300,\"down\":400}" });
        using var service = new TrafficMonitorService();
        var events = new ConcurrentQueue<(long Up, long Down)>();
        service.TrafficUpdated += (up, down) => events.Enqueue((up, down));

        service.Start(server.Port, "secret-token");

        Assert.True(await WaitForAsync(() => events.Count >= 2));
        var received = events.ToArray();
        Assert.Equal((100L, 200L), received[0]);
        Assert.Equal((300L, 400L), received[1]);
        Assert.True(server.RequestText?.Contains("Authorization: Bearer secret-token") == true);
        Assert.True(server.RequestText?.StartsWith("GET /traffic") == true);
    }

    [Fact]
    public async Task Start_SkipsMalformedLinesAndKeepsReading()
    {
        using var server = new FakeClashServer(new[] { "not json", "{\"up\":7,\"down\":8}" });
        using var service = new TrafficMonitorService();
        var events = new ConcurrentQueue<(long Up, long Down)>();
        service.TrafficUpdated += (up, down) => events.Enqueue((up, down));

        service.Start(server.Port, string.Empty);

        Assert.True(await WaitForAsync(() => events.Count >= 1));
        await Task.Delay(200);
        Assert.Equal(new[] { (7L, 8L) }, events.ToArray());
    }

    [Fact]
    public async Task Start_DefaultsMissingCountersToZero()
    {
        using var server = new FakeClashServer(new[] { "{\"up\":5}" });
        using var service = new TrafficMonitorService();
        var events = new ConcurrentQueue<(long Up, long Down)>();
        service.TrafficUpdated += (up, down) => events.Enqueue((up, down));

        service.Start(server.Port, string.Empty);

        Assert.True(await WaitForAsync(() => events.Count >= 1));
        Assert.Equal((5L, 0L), events.ToArray()[0]);
    }

    [Fact]
    public async Task Start_HttpErrorIsSwallowedAndLoopStops()
    {
        using var server = new FakeClashServer(Array.Empty<string>(), statusCode: 503);
        using var service = new TrafficMonitorService();
        var events = new ConcurrentQueue<(long Up, long Down)>();
        service.TrafficUpdated += (up, down) => events.Enqueue((up, down));

        service.Start(server.Port, string.Empty);

        Assert.True(await WaitForAsync(() => server.RequestText != null));
        await Task.Delay(300);
        Assert.Empty(events);
        Assert.False(service.IsRunning);
    }

    [Fact]
    public async Task Stop_EndsTheLoopAndStopsEvents()
    {
        using var server = new FakeClashServer(new[] { "{\"up\":1,\"down\":1}" });
        using var service = new TrafficMonitorService();
        var events = new ConcurrentQueue<(long Up, long Down)>();
        service.TrafficUpdated += (up, down) => events.Enqueue((up, down));

        service.Start(server.Port, string.Empty);
        Assert.True(await WaitForAsync(() => events.Count >= 1));

        service.Stop();
        Assert.False(service.IsRunning);
    }

    private static async Task<bool> WaitForAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(25);
        }

        return condition();
    }

    /// <summary>
    /// Minimal loopback HTTP server that answers GET /traffic with the given
    /// newline-delimited JSON lines and then keeps the connection open, like
    /// the real sing-box Clash API streaming endpoint.
    /// </summary>
    private sealed class FakeClashServer : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _cancellation = new();
        private readonly Task _acceptLoop;
        private readonly string[] _lines;
        private readonly int _statusCode;

        public FakeClashServer(string[] lines, int statusCode = 200)
        {
            _lines = lines;
            _statusCode = statusCode;
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _acceptLoop = Task.Run(() => AcceptLoopAsync(_cancellation.Token));
        }

        public int Port { get; }

        public string? RequestText { get; private set; }

        private async Task AcceptLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync(cancellationToken);
                }
                catch
                {
                    return;
                }

                _ = HandleAsync(client, cancellationToken);
            }
        }

        private async Task HandleAsync(TcpClient client, CancellationToken cancellationToken)
        {
            using (client)
            {
                try
                {
                    var stream = client.GetStream();
                    var requestBuilder = new StringBuilder();
                    var buffer = new byte[4096];
                    while (!requestBuilder.ToString().Contains("\r\n\r\n", StringComparison.Ordinal))
                    {
                        var read = await stream.ReadAsync(buffer, cancellationToken);
                        if (read <= 0)
                        {
                            return;
                        }

                        requestBuilder.Append(Encoding.ASCII.GetString(buffer, 0, read));
                    }

                    RequestText = requestBuilder.ToString();

                    if (_statusCode != 200)
                    {
                        var error = Encoding.ASCII.GetBytes($"HTTP/1.1 {_statusCode} Error\r\nContent-Length: 0\r\n\r\n");
                        await stream.WriteAsync(error, cancellationToken);
                        return;
                    }

                    var header = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: application/json\r\n\r\n");
                    await stream.WriteAsync(header, cancellationToken);
                    foreach (var line in _lines)
                    {
                        await stream.WriteAsync(Encoding.ASCII.GetBytes(line + "\n"), cancellationToken);
                        await stream.FlushAsync(cancellationToken);
                    }

                    await Task.Delay(Timeout.Infinite, cancellationToken);
                }
                catch
                {
                }
            }
        }

        public void Dispose()
        {
            _cancellation.Cancel();
            _listener.Stop();
            try
            {
                _acceptLoop.Wait(TimeSpan.FromSeconds(2));
            }
            catch
            {
            }

            _cancellation.Dispose();
        }
    }
}
