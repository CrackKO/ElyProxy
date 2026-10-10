using System;
using System.Collections.Generic;
using System.Linq;

namespace ElyProxy.Services;

public static class SubscriptionHeaderParser
{
	private static readonly string[] ReservedHeaders = new string[4] { "host", "content-length", "user-agent", "x-hwid" };

	public static Dictionary<string, string> Parse(string? text)
	{
		Dictionary<string, string> headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		if (string.IsNullOrWhiteSpace(text))
		{
			return headers;
		}
		foreach (string line in text.Split('\n'))
		{
			string trimmed = line.Trim();
			if (trimmed.Length == 0 || trimmed.StartsWith('#'))
			{
				continue;
			}
			int colon = trimmed.IndexOf(':');
			if (colon <= 0)
			{
				continue;
			}
			string key = trimmed.Substring(0, colon).Trim();
			string value = trimmed.Substring(colon + 1).Trim();
			if (key.Length == 0 || value.Length == 0)
			{
				continue;
			}
			if (ReservedHeaders.Contains(key, StringComparer.OrdinalIgnoreCase))
			{
				continue;
			}
			headers[key] = value;
		}
		return headers;
	}

	public static string Format(IReadOnlyDictionary<string, string>? headers)
	{
		if (headers == null || headers.Count == 0)
		{
			return string.Empty;
		}
		return string.Join(Environment.NewLine, headers.Select(pair => pair.Key + ": " + pair.Value));
	}
}
