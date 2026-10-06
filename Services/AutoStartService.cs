using System;
using System.Diagnostics;
using Microsoft.Win32;

namespace ElyProxy.Services;

public class AutoStartService
{
	private const string AppName = "ElyProxy";

	private const string RunKeyPath = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";

	public bool IsEnabled()
	{
		using RegistryKey key = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run", writable: false);
		string value = key?.GetValue("ElyProxy") as string;
		string exePath = GetExecutablePath();
		return !string.IsNullOrWhiteSpace(value) && !string.IsNullOrWhiteSpace(exePath) && value.Contains(exePath, StringComparison.OrdinalIgnoreCase);
	}

	public void SetEnabled(bool enabled)
	{
		using RegistryKey key = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run", writable: true) ?? Registry.CurrentUser.CreateSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run", writable: true);
		if (key == null)
		{
			throw new InvalidOperationException("Не удалось открыть раздел автозапуска Windows.");
		}
		if (enabled)
		{
			key.SetValue("ElyProxy", "\"" + GetExecutablePath() + "\"");
		}
		else
		{
			key.DeleteValue("ElyProxy", throwOnMissingValue: false);
		}
	}

	private static string GetExecutablePath()
	{
		return Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? throw new InvalidOperationException("Не удалось определить путь к ElyProxy.exe.");
	}
}
