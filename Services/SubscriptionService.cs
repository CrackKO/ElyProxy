using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using ElyProxy.Models;

namespace ElyProxy.Services;

public class SubscriptionService : IDisposable
{
	private readonly HttpClient _httpClient;
	private readonly bool _ownsHttpClient;

	public SubscriptionService()
	{
		HttpClientHandler handler = new HttpClientHandler
		{
			AutomaticDecompression = DecompressionMethods.All
		};
		_httpClient = new HttpClient(handler)
		{
			Timeout = TimeSpan.FromSeconds(30.0)
		};
		_ownsHttpClient = true;
		ConfigureClient();
	}

	public SubscriptionService(HttpClient httpClient)
	{
		_httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
		ConfigureClient();
	}

	private void ConfigureClient()
	{
		_httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("ElyProxy/1.0");
		_httpClient.DefaultRequestHeaders.Accept.ParseAdd("text/plain, */*");
	}

	public async Task<string> FetchAsync(string url)
	{
		using HttpResponseMessage response = await _httpClient.GetAsync(url);
		response.EnsureSuccessStatusCode();
		return await response.Content.ReadAsStringAsync();
	}

	public async Task<SubscriptionRefreshResult> FetchNodesAsync(
		string url,
		ParserService parser,
		CancellationToken cancellationToken = default)
	{
		if (!Uri.TryCreate(url?.Trim(), UriKind.Absolute, out Uri? uri)
			|| (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
		{
			return new SubscriptionRefreshResult(
				SubscriptionRefreshStatus.RequestFailed,
				Array.Empty<VlessNode>(),
				"Ссылка должна начинаться с http:// или https://.");
		}

		try
		{
			using HttpResponseMessage response = await _httpClient.GetAsync(uri, cancellationToken);
			if (!response.IsSuccessStatusCode)
			{
				return new SubscriptionRefreshResult(
					SubscriptionRefreshStatus.RequestFailed,
					Array.Empty<VlessNode>(),
					$"Сервер подписки вернул HTTP {(int)response.StatusCode}.");
			}

			string content = await response.Content.ReadAsStringAsync(cancellationToken);
			string contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
			string prefix = content.AsSpan().TrimStart().ToString();
			if (contentType.Equals("text/html", StringComparison.OrdinalIgnoreCase)
				|| prefix.StartsWith("<!doctype html", StringComparison.OrdinalIgnoreCase)
				|| prefix.StartsWith("<html", StringComparison.OrdinalIgnoreCase))
			{
				return new SubscriptionRefreshResult(
					SubscriptionRefreshStatus.InvalidContent,
					Array.Empty<VlessNode>(),
					"Вместо списка серверов сервер подписки вернул HTML-страницу.");
			}

			if (string.IsNullOrWhiteSpace(content))
			{
				return new SubscriptionRefreshResult(
					SubscriptionRefreshStatus.Empty,
					Array.Empty<VlessNode>(),
					"Ответ подписки пустой.");
			}

			List<VlessNode> nodes = parser.ParseSubscriptionContent(content);
			if (nodes.Count == 0)
			{
				return new SubscriptionRefreshResult(
					SubscriptionRefreshStatus.InvalidContent,
					nodes,
					"Ответ не содержит корректных VLESS-серверов; прежний список сохранён.");
			}

			return new SubscriptionRefreshResult(SubscriptionRefreshStatus.Success, nodes);
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			return new SubscriptionRefreshResult(
				SubscriptionRefreshStatus.RequestFailed,
				Array.Empty<VlessNode>(),
				"Истекло время ожидания ответа подписки.");
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (HttpRequestException)
		{
			return new SubscriptionRefreshResult(
				SubscriptionRefreshStatus.RequestFailed,
				Array.Empty<VlessNode>(),
				"Не удалось загрузить подписку из-за сетевой ошибки.");
		}
		catch (Exception ex)
		{
			return new SubscriptionRefreshResult(
				SubscriptionRefreshStatus.InvalidContent,
				Array.Empty<VlessNode>(),
				$"Не удалось обработать ответ подписки ({ex.GetType().Name}).");
		}
	}

	public void Dispose()
	{
		if (_ownsHttpClient)
		{
			_httpClient.Dispose();
		}
		GC.SuppressFinalize(this);
	}
}
