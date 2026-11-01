using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using ElyProxy.Helper.Runtime;

namespace ElyProxy.Helper;

internal static class CoreHost
{
    private delegate bool Handler(uint signal);
    private static readonly Handler IgnoreInHost = _ => true;
    public static async Task<int> RunAsync(string encoded)
    {
        var launch = JsonSerializer.Deserialize<CoreLaunch>(Encoding.UTF8.GetString(Convert.FromBase64String(encoded)))
            ?? throw new InvalidDataException("Missing core launch.");
        if (!HelperProtocol.IsAllowedExecutable(launch.File)) throw new InvalidDataException("Invalid core.");
        // Capture redirected streams BEFORE AllocConsole replaces native standard handles.
        using var input = new StreamReader(Console.OpenStandardInput(), Encoding.UTF8);
        using var output = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true };
        using var error = new StreamWriter(Console.OpenStandardError(), new UTF8Encoding(false)) { AutoFlush = true };
        FreeConsole();
        if (!AllocConsole()) throw new InvalidOperationException("Cannot create isolated core console.");
        ShowWindow(GetConsoleWindow(), 0);
        SetConsoleCtrlHandler(IgnoreInHost, true);
        var session = await OwnedTunSession.PrepareAsync(launch).ConfigureAwait(false);
        using var process = Process.Start(new ProcessStartInfo(launch.File, launch.Arguments)
        {
            WorkingDirectory = launch.WorkingDirectory, UseShellExecute = false,
            CreateNoWindow = false, WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        }) ?? throw new IOException("Cannot start core.");
        var safeOutput = TextWriter.Synchronized(output);
        var safeError = TextWriter.Synchronized(error);
        try
        {
            ProcessJob.Assign(process);
            if (!ProcessJob.IsAssigned(process)) throw new IOException("Core ownership job could not be established.");
            var stdout = PumpAsync(process.StandardOutput, safeOutput);
            var stderr = PumpAsync(process.StandardError, safeError);
            await session.TrackAsync(process).ConfigureAwait(false);
            if (process.HasExited) throw new IOException("Core exited before host ready.");
            await safeOutput.WriteLineAsync("[elyproxy-host-ready]").ConfigureAwait(false);
            var exited = process.WaitForExitAsync();
            var command = input.ReadLineAsync();
            await Task.WhenAny(exited, command).ConfigureAwait(false);
            bool requestedStop = !process.HasExited;
            if (!process.HasExited)
            {
                // Only this host and its child share this console. Never signal the user's terminal.
                SetConsoleCtrlHandler(IgnoreInHost, true);
                GenerateConsoleCtrlEvent(0, 0);
                try { await exited.WaitAsync(TimeSpan.FromSeconds(4)).ConfigureAwait(false); }
                catch (TimeoutException) { await OwnedProcessStopper.StopAsync(process).ConfigureAwait(false); }
            }
            await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
            await session.CleanupAsync().ConfigureAwait(false);
            await safeOutput.WriteLineAsync($"[sys] Core PID {process.Id} exited, code {process.ExitCode}; session cleaned").ConfigureAwait(false);
            return requestedStop ? 0 : process.ExitCode;
        }
        finally
        {
            if (!process.HasExited) await OwnedProcessStopper.StopAsync(process).ConfigureAwait(false);
            await session.CleanupAsync().ConfigureAwait(false);
        }
    }
    private static async Task PumpAsync(StreamReader source, TextWriter target)
    {
        while (await source.ReadLineAsync().ConfigureAwait(false) is { } line)
            await target.WriteLineAsync(line).ConfigureAwait(false);
    }
    [DllImport("kernel32.dll")] private static extern bool AllocConsole();
    [DllImport("kernel32.dll")] private static extern bool FreeConsole();
    [DllImport("kernel32.dll")] private static extern IntPtr GetConsoleWindow();
    [DllImport("kernel32.dll")] private static extern bool SetConsoleCtrlHandler(Handler handler, bool add);
    [DllImport("kernel32.dll")] private static extern bool GenerateConsoleCtrlEvent(uint signal, uint group);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
}
