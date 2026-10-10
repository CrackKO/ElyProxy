using System;
using System.Globalization;

namespace ElyProxy.Models;

public sealed record SubscriptionUserInfo(long UploadBytes, long DownloadBytes, long TotalBytes, DateTimeOffset? ExpiresAt)
{
	public long UsedBytes => UploadBytes + DownloadBytes;

	public static SubscriptionUserInfo? Parse(string? headerValue)
	{
		if (string.IsNullOrWhiteSpace(headerValue))
		{
			return null;
		}
		long upload = 0;
		long download = 0;
		long total = 0;
		DateTimeOffset? expires = null;
		foreach (string part in headerValue.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
		{
			int separator = part.IndexOf('=');
			if (separator <= 0)
			{
				continue;
			}
			string key = part.Substring(0, separator).Trim().ToLowerInvariant();
			string value = part.Substring(separator + 1).Trim();
			switch (key)
			{
			case "upload":
				long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out upload);
				break;
			case "download":
				long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out download);
				break;
			case "total":
				long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out total);
				break;
			case "expire":
				if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long unixSeconds) && unixSeconds > 0)
				{
					expires = DateTimeOffset.FromUnixTimeSeconds(unixSeconds).ToLocalTime();
				}
				break;
			}
		}
		if (upload == 0 && download == 0 && total == 0 && !expires.HasValue)
		{
			return null;
		}
		return new SubscriptionUserInfo(upload, download, total, expires);
	}
}
