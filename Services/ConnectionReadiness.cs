using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Diagnostics;
using System.Text;

namespace ElyProxy.Services;

public static class ConnectionReadiness
{
    public static async Task RequireSocksAsync(int port)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port, timeout.Token).ConfigureAwait(false);
        var stream = client.GetStream();
        await stream.WriteAsync(new byte[] { 5, 1, 0 }, timeout.Token).ConfigureAwait(false);
        byte[] reply = new byte[2];
        await stream.ReadExactlyAsync(reply, timeout.Token).ConfigureAwait(false);
        if (reply[0] != 5 || reply[1] != 0) throw new IOException("Локальный SOCKS не подтвердил готовность.");
    }

    public static async Task RequireTunAsync(string name)
    {
        for (int attempt = 0; attempt < 20; attempt++)
        {
            var adapter = NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(a => a.Name == name);
            if (adapter is { OperationalStatus: OperationalStatus.Up } && adapter.GetIPProperties().UnicastAddresses.Any())
            {
                int index = adapter.GetIPProperties().GetIPv4Properties().Index;
                var start = new ProcessStartInfo("powershell.exe")
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                start.ArgumentList.Add("-NoProfile"); start.ArgumentList.Add("-NonInteractive"); start.ArgumentList.Add("-EncodedCommand");
                start.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(
                    "$ErrorActionPreference='Stop'; Get-NetRoute -AddressFamily IPv4 -InterfaceIndex " + index + " | Select-Object -ExpandProperty DestinationPrefix")));
                using var process = Process.Start(start) ?? throw new IOException("Не удалось проверить маршруты TUN.");
                var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
                try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(4)).ConfigureAwait(false); }
                catch { if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync().ConfigureAwait(false); } throw; }
                string routes = await output.ConfigureAwait(false); await error.ConfigureAwait(false);
                if (process.ExitCode == 0 && HasDefaultRoutes(routes.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))) return;
            }
            await Task.Delay(150).ConfigureAwait(false);
        }
        throw new IOException("Собственный TUN-адаптер не подтвердил готовность: " + name);
    }

    public static bool HasDefaultRoutes(IEnumerable<string> routes)
    {
        var set = routes.ToHashSet(StringComparer.Ordinal);
        return set.Contains("0.0.0.0/0") || set.Contains("0.0.0.0/1") && set.Contains("128.0.0.0/1");
    }

    public static async ValueTask<Stream> ConnectThroughTunAsync(string name, DnsEndPoint endpoint, CancellationToken cancellation)
    {
        var adapter = NetworkInterface.GetAllNetworkInterfaces().Single(a => a.Name == name);
        var properties = adapter.GetIPProperties();
        var source = properties.UnicastAddresses.First(a => a.Address.AddressFamily == AddressFamily.InterNetwork).Address;
        foreach (var address in await Dns.GetHostAddressesAsync(endpoint.Host, cancellation).ConfigureAwait(false))
        {
            if (address.AddressFamily != AddressFamily.InterNetwork) continue;
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                socket.SetSocketOption(SocketOptionLevel.IP, (SocketOptionName)31 /* Windows IP_UNICAST_IF */,
                    IPAddress.HostToNetworkOrder(properties.GetIPv4Properties().Index));
                socket.Bind(new IPEndPoint(source, 0));
                await socket.ConnectAsync(address, endpoint.Port, cancellation).ConfigureAwait(false);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch { socket.Dispose(); if (cancellation.IsCancellationRequested) throw; }
        }
        throw new IOException("HTTPS через собственный TUN не подтверждён.");
    }
}

