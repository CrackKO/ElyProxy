using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ElyProxy.Core;

namespace ElyProxy.Services;

public static class CoreVersionService
{
	public static string GetCorePath(string coreId)
	{
		return coreId switch
		{
			"xray" => CoreLocator.Resolve("xray", "xray.exe", "xray", Path.Combine("bin", "xray"), string.Empty),
			"sing-box" => CoreLocator.Resolve("sing-box", "sing-box.exe", "sing-box", Path.Combine("bin", "sing-box"), string.Empty),
			"tun2proxy" => CoreLocator.Resolve("tun2proxy", "tun2proxy-bin.exe", "tun2proxy", Path.Combine("bin", "tun2proxy"), string.Empty),
			"hysteria" => CoreLocator.Resolve("hysteria", "hysteria.exe", "hysteria", Path.Combine("bin", "hysteria"), string.Empty),
			_ => string.Empty
		};
	}

	public static async Task<string> GetInstalledVersionAsync(string coreId, CancellationToken cancellationToken = default)
	{
		string path = GetCorePath(coreId);
		if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
		{
			return "не установлено";
		}
		string arguments = ((coreId == "tun2proxy") ? "--version" : "version");
		try
		{
			using Process process = new Process
			{
				StartInfo = new ProcessStartInfo
				{
					FileName = path,
					Arguments = arguments,
					UseShellExecute = false,
					CreateNoWindow = true,
					RedirectStandardOutput = true,
					RedirectStandardError = true
				}
			};
			if (!process.Start())
			{
				return "неизвестно";
			}
			Task<string> outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
			Task<string> errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
			using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			timeout.CancelAfter(TimeSpan.FromSeconds(5.0));
			try
			{
				await process.WaitForExitAsync(timeout.Token);
			}
			catch (OperationCanceledException)
			{
				try
				{
					process.Kill(entireProcessTree: true);
				}
				catch
				{
				}
				return "неизвестно";
			}
			string text = string.Join(" ", new string[2] { await outputTask, await errorTask }.Where(value => !string.IsNullOrWhiteSpace(value)));
			Match match = Regex.Match(text, @"\d+\.\d+\.\d+");
			return (match.Success ? match.Value : "неизвестно");
		}
		catch
		{
			return "неизвестно";
		}
	}
}
