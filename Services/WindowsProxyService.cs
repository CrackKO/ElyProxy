using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace ElyProxy.Services;

public class WindowsProxyService : IWindowsProxyService
{
	private const string InternetSettingsPath = "Software\\Microsoft\\Windows\\CurrentVersion\\Internet Settings";

	private const string AutoConfigUrlValue = "AutoConfigURL";

	private const string ProxyEnableValue = "ProxyEnable";

	private const string ProxyServerValue = "ProxyServer";

	private const string ProxyOverrideValue = "ProxyOverride";

	private const int InternetOptionSettingsChanged = 39;

	private const int InternetOptionRefresh = 37;

	public string? GetAutoConfigUrl()
	{
		using RegistryKey key = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Internet Settings", writable: false);
		return key?.GetValue("AutoConfigURL") as string;
	}

	public bool IsPacEnabled(string pacUrl)
	{
		return string.Equals(GetAutoConfigUrl(), pacUrl, StringComparison.OrdinalIgnoreCase);
	}

	public void EnablePac(string pacUrl)
	{
		using RegistryKey key = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Internet Settings", writable: true) ?? Registry.CurrentUser.CreateSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Internet Settings", writable: true);
		if (key == null)
		{
			throw new InvalidOperationException("Не удалось открыть настройки прокси Windows.");
		}
		key.SetValue("AutoConfigURL", pacUrl, RegistryValueKind.String);
		NotifySettingsChanged();
	}

	public void DisablePac(string pacUrl, string? restoreAutoConfigUrl)
	{
		using RegistryKey key = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Internet Settings", writable: true) ?? Registry.CurrentUser.CreateSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Internet Settings", writable: true);
		if (key == null)
		{
			throw new InvalidOperationException("Не удалось открыть настройки прокси Windows.");
		}
		if (string.Equals(key.GetValue("AutoConfigURL") as string, pacUrl, StringComparison.OrdinalIgnoreCase))
		{
			if (string.IsNullOrWhiteSpace(restoreAutoConfigUrl))
			{
				key.DeleteValue("AutoConfigURL", throwOnMissingValue: false);
			}
			else
			{
				key.SetValue("AutoConfigURL", restoreAutoConfigUrl, RegistryValueKind.String);
			}
			NotifySettingsChanged();
		}
	}

	public WindowsProxySnapshot CaptureManualProxySettings()
	{
		return CaptureCurrentSettings();
	}

	public WindowsProxySnapshot CaptureCurrentSettings()
	{
		using RegistryKey? key = Registry.CurrentUser.OpenSubKey(InternetSettingsPath, writable: false);
		return WindowsProxySnapshot.Capture(key);
	}

	public bool IsOwnedByPac(WindowsProxySnapshot current, IEnumerable<string> ownedPacUrls)
	{
		string? activeUrl = current.AutoConfigUrl?.StringValue;
		return !string.IsNullOrWhiteSpace(activeUrl)
			&& ownedPacUrls.Any(url => string.Equals(activeUrl, url, StringComparison.OrdinalIgnoreCase));
	}

	public bool IsOwnedByManualProxy(WindowsProxySnapshot current, int port, IEnumerable<string> bypassRules)
	{
		return current.ProxyEnable?.Kind == RegistryValueKind.DWord
			&& current.ProxyEnable.DwordValue == 1
			&& string.Equals(current.ProxyServer?.StringValue, BuildProxyServer(port), StringComparison.OrdinalIgnoreCase)
			&& string.Equals(current.ProxyOverride?.StringValue, BuildProxyOverride(bypassRules), StringComparison.OrdinalIgnoreCase)
			&& current.AutoConfigUrl == null;
	}

	public void EnableManualProxy(int port, IEnumerable<string> bypassRules)
	{
		using RegistryKey key = OpenWritableKey();
		key.SetValue("ProxyEnable", 1, RegistryValueKind.DWord);
		key.SetValue("ProxyServer", BuildProxyServer(port), RegistryValueKind.String);
		key.SetValue("ProxyOverride", BuildProxyOverride(bypassRules), RegistryValueKind.String);
		key.DeleteValue("AutoConfigURL", throwOnMissingValue: false);
		NotifySettingsChanged();
	}

	public void RestoreManualProxySettings(WindowsProxySnapshot snapshot)
	{
		using RegistryKey key = OpenWritableKey();
		RestoreValue(key, "ProxyEnable", snapshot.ProxyEnable);
		RestoreValue(key, "ProxyServer", snapshot.ProxyServer);
		RestoreValue(key, "ProxyOverride", snapshot.ProxyOverride);
		RestoreValue(key, "AutoConfigURL", snapshot.AutoConfigUrl);
		NotifySettingsChanged();
	}

	public void RestorePacSettings(WindowsProxySnapshot snapshot)
	{
		using RegistryKey key = OpenWritableKey();
		RestoreValue(key, "AutoConfigURL", snapshot.AutoConfigUrl);
		NotifySettingsChanged();
	}

	private static RegistryKey OpenWritableKey()
	{
		return Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Internet Settings", writable: true) ?? Registry.CurrentUser.CreateSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Internet Settings", writable: true) ?? throw new InvalidOperationException("Не удалось открыть настройки прокси Windows.");
	}

	private static string BuildProxyServer(int port) =>
		$"http=127.0.0.1:{port};https=127.0.0.1:{port};socks=127.0.0.1:{port}";

	private static string BuildProxyOverride(IEnumerable<string> bypassRules)
	{
		IEnumerable<string> bypass = bypassRules
			.Select(rule => rule.Trim())
			.Where(rule => !string.IsNullOrWhiteSpace(rule) && !rule.Contains('/'))
			.Append("<local>")
			.Distinct(StringComparer.OrdinalIgnoreCase);
		return string.Join(';', bypass);
	}

	private static void RestoreValue(RegistryKey key, string name, WindowsProxyValueSnapshot? value)
	{
		if (value == null)
		{
			key.DeleteValue(name, throwOnMissingValue: false);
		}
		else
		{
			key.SetValue(name, value.ToRegistryValue(), value.Kind);
		}
	}

	private static void NotifySettingsChanged()
	{
		InternetSetOption(IntPtr.Zero, 39, IntPtr.Zero, 0);
		InternetSetOption(IntPtr.Zero, 37, IntPtr.Zero, 0);
	}

	[DllImport("wininet.dll", SetLastError = true)]
	private static extern bool InternetSetOption(nint hInternet, int dwOption, nint lpBuffer, int dwBufferLength);
}
