using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace ElyProxy.Core;

public static class ServerAddressResolver
{
	public static async Task<string[]> ResolveEndpointCidrsAsync(string address, TimeSpan timeout, Action<string>? warningSink = null)
	{
		List<IPAddress> addresses = new List<IPAddress>();
		if (IPAddress.TryParse(address, out IPAddress literal))
		{
			addresses.Add(literal);
		}
		else if (!string.IsNullOrWhiteSpace(address))
		{
			try
			{
				using CancellationTokenSource cancellation = new CancellationTokenSource(timeout);
				addresses.AddRange(await Dns.GetHostAddressesAsync(address, cancellation.Token));
			}
			catch (OperationCanceledException)
			{
				throw new IOException("Не удалось определить IP сервера. TUN не запущен, чтобы исключить петлю маршрутизации.");
			}
			catch (Exception ex)
			{
				throw new IOException("Не удалось определить IP сервера. TUN не запущен.", ex);
			}
		}
		return addresses
			.Where(candidate => !IPAddress.IsLoopback(candidate))
			.Select(candidate => $"{candidate}/{((candidate.AddressFamily == AddressFamily.InterNetwork) ? 32 : 128)}")
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToArray();
	}
}
