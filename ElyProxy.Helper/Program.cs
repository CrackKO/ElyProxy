using System.Diagnostics;
using System.ServiceProcess;

namespace ElyProxy.Helper;

internal sealed class HelperWindowsService : ServiceBase
{
	private HelperServer? _server;

	public HelperWindowsService()
	{
		ServiceName = "ElyProxyHelper";
	}

	protected override void OnStart(string[] args)
	{
		_server = new HelperServer();
		_server.Start();
	}

	protected override void OnStop()
	{
		_server?.Stop();
		_server = null;
	}
}

internal static class Program
{
	private const string ServiceName = "ElyProxyHelper";

	private static void Main(string[] args)
	{
		string command = (args.Length > 0 ? args[0].ToLowerInvariant() : string.Empty);
		switch (command)
		{
		case "zapret-enable":
		case "zapret-restore":
			Environment.ExitCode = ZapretCompatibility.Run(command == "zapret-restore");
			return;
		case "install":
			Environment.ExitCode = Install(
				args.Length > 1 ? args[1] : null,
				args.Length > 2 ? args[2] : null);
			return;
		case "uninstall":
			Environment.ExitCode = Uninstall();
			return;
		case "console":
			RunConsole();
			return;
		default:
			if (Environment.UserInteractive)
			{
				RunConsole();
				return;
			}
			ServiceBase.Run(new HelperWindowsService());
			return;
		}
	}

	private static void RunConsole()
	{
		Console.WriteLine("ElyProxy.Helper: консольный режим. Ctrl+C — выход.");
		HelperServer server = new HelperServer();
		server.Start();
		using ManualResetEventSlim stop = new ManualResetEventSlim(false);
		Console.CancelKeyPress += (_, eventArgs) =>
		{
			eventArgs.Cancel = true;
			stop.Set();
		};
		stop.Wait();
		server.Stop();
	}

	private static int Install(string? sourceDirectory, string? targetDirectory)
	{
		string ownExecutable = Environment.ProcessPath ?? throw new InvalidOperationException("Не удалось определить путь ElyProxy.Helper.exe.");
		string executable = ownExecutable;
		RunSc($"stop {ServiceName}");
		RunSc($"delete {ServiceName}");
		// The service must not run from the ElyProxy installation folder: while
		// it runs, its files are locked and the updater cannot replace them.
		// After the old instance stopped, refresh the payload from the source
		// directory into the target (usually %LocalAppData%\ElyProxy\service).
		if (!string.IsNullOrWhiteSpace(sourceDirectory)
			&& !string.IsNullOrWhiteSpace(targetDirectory)
			&& Directory.Exists(sourceDirectory))
		{
			Directory.CreateDirectory(targetDirectory);
			CopyDirectory(sourceDirectory, targetDirectory);
			executable = Path.Combine(targetDirectory, Path.GetFileName(ownExecutable));
		}
		if (!RunSc($"create {ServiceName} binPath= \"\\\"{executable}\\\" run\" start= auto DisplayName= \"ElyProxy Helper Service\""))
		{
			Console.Error.WriteLine("Не удалось создать службу " + ServiceName + ".");
			return 1;
		}
		RunSc($"description {ServiceName} \"Запускает TUN-ядра ElyProxy с правами системы, чтобы клиенту не требовался UAC.\"");
		if (!RunSc($"start {ServiceName}"))
		{
			Console.Error.WriteLine("Служба создана, но не запустилась.");
			return 2;
		}
		Console.WriteLine("Служба ElyProxyHelper установлена.");
		return 0;
	}

	private static void CopyDirectory(string source, string destination)
	{
		Directory.CreateDirectory(destination);
		foreach (string file in Directory.EnumerateFiles(source))
		{
			string target = Path.Combine(destination, Path.GetFileName(file));
			try
			{
				File.Copy(file, target, overwrite: true);
			}
			catch (IOException)
			{
				// A file can still be locked for a moment; the service keeps
				// working with the previous copy.
			}
			catch (UnauthorizedAccessException)
			{
			}
		}
		foreach (string directory in Directory.EnumerateDirectories(source))
		{
			CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
		}
	}

	private static int Uninstall()
	{
		RunSc($"stop {ServiceName}");
		if (!RunSc($"delete {ServiceName}"))
		{
			Console.Error.WriteLine("Не удалось удалить службу " + ServiceName + ".");
			return 1;
		}
		Console.WriteLine("Служба ElyProxyHelper удалена.");
		return 0;
	}

	private static bool RunSc(string arguments)
	{
		using Process process = Process.Start(new ProcessStartInfo
		{
			FileName = "sc.exe",
			Arguments = arguments,
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true
		}) ?? throw new InvalidOperationException("Не удалось запустить sc.exe.");
		string output = process.StandardOutput.ReadToEnd();
		string error = process.StandardError.ReadToEnd();
		process.WaitForExit();
		if (process.ExitCode != 0)
		{
			Console.Error.WriteLine($"sc.exe {arguments}: {output} {error}".Trim());
			return false;
		}
		return true;
	}
}
