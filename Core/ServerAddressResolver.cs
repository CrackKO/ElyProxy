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
				warningSink?.Invoke("[warn] Не удалось вовремя определить IP сервера: " + address + ". Запуск продолжается.");
			}
			catch (Exception ex)
			{
				warningSink?.Invoke($"[warn] Не удалось определить IP сервера: {address} ({ex.GetType().Name}). Запуск продолжается.");
			}
		}
		return addresses
			.Where(candidate => !IPAddress.IsLoopback(candidate))
			.Select(candidate => $"{candidate}/{((candidate.AddressFamily == AddressFamily.InterNetwork) ? 32 : 128)}")
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToArray();
	}
}
