using System;
using System.Security.Cryptography;
using System.Text;

namespace ElyProxy.Core;

public static class ConfigHash
{
	public static string Compute(string? config)
	{
		if (string.IsNullOrEmpty(config))
		{
			return string.Empty;
		}
		byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(config));
		return Convert.ToHexString(hash).Substring(0, 12).ToLowerInvariant();
	}
}
