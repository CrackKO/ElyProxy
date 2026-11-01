using System;
using System.IO;

namespace ElyProxy.Core;

public static class CoreLocator
{
	public static string CoresRoot => Path.Combine(
		Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
		"ElyProxy",
		"cores");

	public static string GetInstallDirectory(string coreId)
	{
		return Path.Combine(CoresRoot, coreId);
	}

	public static string Resolve(string coreId, string fileName, params string[] relativeDirectories)
	{
		string local = Path.Combine(GetInstallDirectory(coreId), fileName);
		if (File.Exists(local))
		{
			return local;
		}
		string appDir = AppDomain.CurrentDomain.BaseDirectory;
		foreach (string relative in relativeDirectories)
		{
			string candidate = (string.IsNullOrWhiteSpace(relative)
				? Path.Combine(appDir, fileName)
				: Path.Combine(appDir, relative, fileName));
			if (File.Exists(candidate))
			{
				return candidate;
			}
		}
		string fallbackRelative = ((relativeDirectories.Length != 0) ? relativeDirectories[0] : string.Empty);
		return (string.IsNullOrWhiteSpace(fallbackRelative)
			? Path.Combine(appDir, fileName)
			: Path.Combine(appDir, fallbackRelative, fileName));
	}
}
