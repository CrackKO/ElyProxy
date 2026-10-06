using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading.Tasks;
using ElyProxy.Core;
using ElyProxy.Models;

namespace ElyProxy.Services;

public sealed class ElyHardService
{
	public sealed record ElyHardTarget(string Name, string Url);

	public sealed record ElyHardOptions(int AttemptsPerTarget, TimeSpan StartupTimeout, TimeSpan RequestTimeout);

	public sealed class ElyHardServiceResult
	{
		public string Name { get; }

		public int Attempts { get; }

		public IReadOnlyList<int> SuccessfulLatencies { get; }

		public string? LastError { get; }

		public int SuccessCount => SuccessfulLatencies.Count;

		public bool IsStable => SuccessCount == Attempts;

		public bool IsReachable => SuccessCount > 0;

		public int? AverageLatencyMs
		{
			get
			{
				if (!IsReachable)
				{
					return null;
				}
				return (int)Math.Round(SuccessfulLatencies.Average());
			}
		}

		public string CompactDisplay
		{
			get
			{
				if (IsReachable)
				{
					return $"{Name} {SuccessCount}/{Attempts} {AverageLatencyMs} ms";
				}
				return $"{Name} 0/{Attempts}";
			}
		}

		public ElyHardServiceResult(string name, int attempts, IReadOnlyList<int> successfulLatencies, string? lastError)
		{
			Name = name;
			Attempts = attempts;
			SuccessfulLatencies = successfulLatencies;
			LastError = lastError;
		}
	}

	public sealed class ElyHardNodeResult
	{
		public VlessNode Node { get; }

		public IReadOnlyList<ElyHardServiceResult> Services { get; }

		public int TotalServices => Services.Count;

		public int StableServices => Services.Count((ElyHardServiceResult service) => service.IsStable);

		public int ReachableServices => Services.Count((ElyHardServiceResult service) => service.IsReachable);

		public int TotalSuccessfulAttempts => Services.Sum((ElyHardServiceResult service) => service.SuccessCount);

		public int TotalAttempts => Services.Sum((ElyHardServiceResult service) => service.Attempts);

		public int? AverageLatencyMs
		{
			get
			{
				if (ReachableServices <= 0)
				{
					return null;
				}
				return (int)Math.Round(Services.Where((ElyHardServiceResult service) => service.AverageLatencyMs.HasValue).Average((ElyHardServiceResult service) => service.AverageLatencyMs.Value));
			}
		}

		public int? WorstLatencyMs
		{
			get
			{
				if (ReachableServices <= 0)
				{
					return null;
				}
				return Services.Where((ElyHardServiceResult service) => service.AverageLatencyMs.HasValue).Max((ElyHardServiceResult service) => service.AverageLatencyMs.Value);
			}
		}

		public ElyHardNodeResult(VlessNode node, IReadOnlyList<ElyHardServiceResult> services)
		{
			Node = node;
			Services = services;
		}

		public static ElyHardNodeResult Empty(VlessNode node, IReadOnlyList<ElyHardTarget> targets)
		{
			return new ElyHardNodeResult(node, targets.Select((ElyHardTarget target) => new ElyHardServiceResult(target.Name, 2, Array.Empty<int>(), "not started")).ToList());
		}
	}

	private static readonly ElyHardTarget[] Targets = new ElyHardTarget[6]
	{
		new ElyHardTarget("Discord", "https://discord.com/api/v9/experiments"),
		new ElyHardTarget("Telegram", "https://telegram.org/"),
		new ElyHardTarget("YouTube", "https://www.youtube.com/"),
		new ElyHardTarget("Spotify", "https://open.spotify.com/"),
		new ElyHardTarget("GitHub", "https://github.com/"),
		new ElyHardTarget("Gmail", "https://mail.google.com/mail/u/0/")
	};

	public async Task<ElyHardNodeResult> CheckNodeAsync(VlessNode node, ElyHardOptions options)
	{
		int probePort = GetFreeTcpPort();
		using XrayManager xray = new XrayManager();
		ElyHardNodeResult result;
		try
		{
			_ = 2;
			try
			{
				await xray.StartAsync(node, probePort);
				if (!(await WaitForLocalPortAsync(probePort, options.StartupTimeout)))
				{
					result = ElyHardNodeResult.Empty(node, Targets);
				}
				else
				{
					using HttpClientHandler handler = new HttpClientHandler
					{
						Proxy = new WebProxy($"socks5://127.0.0.1:{probePort}"),
						UseProxy = true,
						AllowAutoRedirect = true,
						MaxAutomaticRedirections = 5
					};
					HttpClient http = new HttpClient(handler)
					{
						Timeout = options.RequestTimeout
					};
					try
					{
						http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 ElyProxy/ElyHard");
						result = new ElyHardNodeResult(node, await Task.WhenAll(Targets.Select((ElyHardTarget target) => CheckTargetAsync(http, target, options.AttemptsPerTarget))));
					}
					finally
					{
						if (http != null)
						{
							((IDisposable)http).Dispose();
						}
					}
				}
			}
			catch
			{
				result = ElyHardNodeResult.Empty(node, Targets);
			}
		}
		finally
		{
			await xray.StopAsync();
		}
		return result;
	}

	private static async Task<ElyHardServiceResult> CheckTargetAsync(HttpClient http, ElyHardTarget target, int attempts)
	{
		List<int> latencies = new List<int>(attempts);
		string lastError = null;
		for (int i = 0; i < attempts; i++)
		{
			try
			{
				Stopwatch sw = Stopwatch.StartNew();
				using HttpResponseMessage response = await http.GetAsync(target.Url, HttpCompletionOption.ResponseHeadersRead);
				sw.Stop();
				int code = (int)response.StatusCode;
				if (code >= 200 && code < 500)
				{
					latencies.Add((int)sw.ElapsedMilliseconds);
					continue;
				}
				lastError = $"HTTP {code}";
			}
			catch (Exception ex)
			{
				lastError = ex.GetType().Name;
			}
		}
		return new ElyHardServiceResult(target.Name, attempts, latencies, lastError);
	}

	private static int GetFreeTcpPort()
	{
		TcpListener tcpListener = new TcpListener(IPAddress.Loopback, 0);
		tcpListener.Start();
		int port = ((IPEndPoint)tcpListener.LocalEndpoint).Port;
		tcpListener.Stop();
		return port;
	}

	private static async Task<bool> WaitForLocalPortAsync(int port, TimeSpan timeout)
	{
		DateTime deadline = DateTime.UtcNow + timeout;
		while (DateTime.UtcNow < deadline)
		{
			try
			{
				using TcpClient client = new TcpClient();
				await client.ConnectAsync(IPAddress.Loopback, port).WaitAsync(TimeSpan.FromMilliseconds(250.0));
				return true;
			}
			catch
			{
				await Task.Delay(100);
			}
		}
		return false;
	}
}
