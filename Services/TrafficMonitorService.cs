using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace ElyProxy.Services;

public sealed class TrafficMonitorService : IDisposable
{
	private readonly HttpClient _httpClient = new HttpClient
	{
		Timeout = Timeout.InfiniteTimeSpan
	};

	private CancellationTokenSource? _cancellation;
	private Task? _loop;

	public event Action<long, long>? TrafficUpdated;

	public bool IsRunning => _loop is { IsCompleted: false };

	public void Start(int port, string secret)
	{
		Stop();
		_cancellation = new CancellationTokenSource();
		CancellationToken token = _cancellation.Token;
		_loop = Task.Run(() => LoopAsync(port, secret, token));
	}

	public void Stop()
	{
		try
		{
			_cancellation?.Cancel();
		}
		catch
		{
		}
		_cancellation?.Dispose();
		_cancellation = null;
		_loop = null;
	}

	private async Task LoopAsync(int port, string secret, CancellationToken cancellationToken)
	{
		try
		{
			using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{port}/traffic");
			if (!string.IsNullOrEmpty(secret))
			{
				request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secret);
			}
			using HttpResponseMessage response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
			response.EnsureSuccessStatusCode();
			await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
			using StreamReader reader = new StreamReader(stream);
			while (!cancellationToken.IsCancellationRequested)
			{
				string? line = await reader.ReadLineAsync(cancellationToken);
				if (line == null)
				{
					break;
				}
				Publish(line);
			}
		}
		catch (OperationCanceledException)
		{
		}
		catch
		{
		}
	}

	private void Publish(string line)
	{
		try
		{
			JObject payload = JObject.Parse(line);
			long upload = ((long?)payload["up"] ?? 0);
			long download = ((long?)payload["down"] ?? 0);
			TrafficUpdated?.Invoke(upload, download);
		}
		catch
		{
		}
	}

	public void Dispose()
	{
		Stop();
		_httpClient.Dispose();
		GC.SuppressFinalize(this);
	}
}
