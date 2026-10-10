using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.ServiceProcess;
using System.Threading.Tasks;

namespace ElyProxy.Services;

public static class HelperServiceInstaller
{
	public const string ServiceName = "ElyProxyHelper";

	/// <summary>
	/// Private copy of the service payload. The service must not run from the
	/// ElyProxy installation folder: while it runs, its files are locked and
	/// the updater cannot replace them.
	/// </summary>
	public static string ServiceDirectory => Path.Combine(
		Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
		"ElyProxy",
		"service");

	public static string GetHelperPath()
	{
		string appDir = AppDomain.CurrentDomain.BaseDirectory;
		string[] candidates = new string[2]
		{
			Path.Combine(appDir, "helper", "ElyProxy.Helper.exe"),
			Path.Combine(appDir, "ElyProxy.Helper.exe")
		};
		return candidates.FirstOrDefault(File.Exists) ?? candidates[0];
	}

	public static bool IsHelperAvailable()
	{
		return File.Exists(GetHelperPath());
	}

	public static bool IsServiceInstalled()
	{
		try
		{
			using ServiceController controller = new ServiceController(ServiceName);
			controller.Refresh();
			_ = controller.Status;
			return true;
		}
		catch
		{
			return false;
		}
	}

	public static bool IsServiceRunning()
	{
		try
		{
			using ServiceController controller = new ServiceController(ServiceName);
			controller.Refresh();
			return controller.Status == ServiceControllerStatus.Running;
		}
		catch
		{
			return false;
		}
	}

	public static async Task<bool> InstallAsync()
	{
		string helperPath = GetHelperPath();
		if (!File.Exists(helperPath))
		{
			throw new FileNotFoundException("ElyProxy.Helper.exe не найден. Переустановите ElyProxy.", helperPath);
		}

		// Run the install from a temp copy so the elevated process can refresh
		// the service payload (source -> %LocalAppData%\ElyProxy\service) after
		// stopping the old instance, without locking itself in the process.
		string sourceDirectory = Path.GetDirectoryName(helperPath)
			?? throw new InvalidOperationException("Не удалось определить папку службы-помощника.");
		string staging = Path.Combine(Path.GetTempPath(), "ElyProxy.Helper-" + Guid.NewGuid().ToString("N"));
		CopyPayload(sourceDirectory, staging);
		try
		{
			string stagedHelper = Path.Combine(staging, "ElyProxy.Helper.exe");
			if (!File.Exists(stagedHelper))
			{
				throw new FileNotFoundException("ElyProxy.Helper.exe не найден. Переустановите ElyProxy.", stagedHelper);
			}

			await RunElevatedAsync(stagedHelper, $"install \"{staging}\" \"{ServiceDirectory}\"");
		}
		finally
		{
			try
			{
				if (Directory.Exists(staging))
				{
					Directory.Delete(staging, recursive: true);
				}
			}
			catch
			{
			}
		}

		return await WaitForRunningAsync(TimeSpan.FromSeconds(20.0));
	}

	private static void CopyPayload(string sourceDirectory, string destination)
	{
		Directory.CreateDirectory(destination);
		foreach (string file in Directory.EnumerateFiles(sourceDirectory))
		{
			string target = Path.Combine(destination, Path.GetFileName(file));
			try
			{
				File.Copy(file, target, overwrite: true);
			}
			catch (IOException)
			{
			}
			catch (UnauthorizedAccessException)
			{
			}
		}
	}

	public static async Task<bool> UninstallAsync()
	{
		string helperPath = GetHelperPath();
		if (!File.Exists(helperPath))
		{
			return false;
		}
		await RunElevatedAsync(helperPath, "uninstall");
		for (int attempt = 0; attempt < 20; attempt++)
		{
			await Task.Delay(500);
			if (!IsServiceInstalled())
			{
				return true;
			}
		}
		return false;
	}

	private static async Task RunElevatedAsync(string executablePath, string arguments)
	{
		using Process process = Process.Start(new ProcessStartInfo
		{
			FileName = executablePath,
			Arguments = arguments,
			UseShellExecute = true,
			Verb = "runas",
			WindowStyle = ProcessWindowStyle.Hidden
		}) ?? throw new InvalidOperationException("Не удалось запустить ElyProxy.Helper.exe.");
		await process.WaitForExitAsync();
		if (process.ExitCode != 0)
		{
			throw new InvalidOperationException($"Служба не установилась: ElyProxy.Helper.exe завершился с кодом {process.ExitCode}. Проверьте права администратора и повторите.");
		}
	}

	private static async Task<bool> WaitForRunningAsync(TimeSpan timeout)
	{
		DateTime deadline = DateTime.UtcNow + timeout;
		while (DateTime.UtcNow < deadline)
		{
			if (IsServiceRunning())
			{
				return true;
			}
			await Task.Delay(500);
		}
		return IsServiceRunning();
	}
}
