using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using ElyProxy.Models;

namespace ElyProxy.Services;

public class SubscriptionService : IDisposable
{
	private const string DefaultUserAgent = "ElyProxy/1.0";

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
		_httpClient.DefaultRequestHeaders.Accept.ParseAdd("text/plain, */*");
	}

	public async Task<string> FetchAsync(string url)
	{
		using HttpRequestMessage request = CreateRequest(url, null);
		using HttpResponseMessage response = await _httpClient.SendAsync(request);
		response.EnsureSuccessStatusCode();
		return await response.Content.ReadAsStringAsync();
	}

	private static HttpRequestMessage CreateRequest(
		string url,
		string? userAgent,
		string? hwid = null,
		IReadOnlyDictionary<string, string>? customHeaders = null)
	{
		HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url);
		if (customHeaders != null)
		{
			foreach (KeyValuePair<string, string> header in customHeaders)
			{
				try
				{
					request.Headers.TryAddWithoutValidation(header.Key, header.Value);
				}
				catch
				{
				}
			}
		}
		request.Headers.TryAddWithoutValidation(
			"User-Agent",
			(string.IsNullOrWhiteSpace(userAgent) ? DefaultUserAgent : userAgent.Trim()));
		if (!string.IsNullOrWhiteSpace(hwid))
		{
			request.Headers.TryAddWithoutValidation("X-Hwid", hwid.Trim());
		}
		return request;
	}

	public async Task<SubscriptionRefreshResult> FetchNodesAsync(
		string url,
		ParserService parser,
		string? userAgent = null,
		string? hwid = null,
		IReadOnlyDictionary<string, string>? customHeaders = null,
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
			using HttpRequestMessage request = CreateRequest(uri.AbsoluteUri, userAgent, hwid, customHeaders);
			using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);
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
					"Ответ не содержит корректных серверов; прежний список сохранён.");
			}

			SubscriptionUserInfo? userInfo = null;
			if (response.Headers.TryGetValues("subscription-userinfo", out IEnumerable<string>? values))
			{
				userInfo = SubscriptionUserInfo.Parse(values.FirstOrDefault());
			}

			return new SubscriptionRefreshResult(SubscriptionRefreshStatus.Success, nodes, null, userInfo);
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
