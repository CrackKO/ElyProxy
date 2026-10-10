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
		case "install":
			Environment.ExitCode = Install();
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

	private static int Install()
	{
		string executable = Environment.ProcessPath ?? throw new InvalidOperationException("Не удалось определить путь ElyProxy.Helper.exe.");
		RunSc($"stop {ServiceName}");
		RunSc($"delete {ServiceName}");
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
