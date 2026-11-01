using System.Text.RegularExpressions;
using System.Threading.Channels;

namespace ElyProxy.Services;

public static class LifecycleLog
{
    private static readonly object Gate = new();
    private static readonly Channel<(string? Line, TaskCompletionSource? Completion)> Pending = Channel.CreateBounded<(string?, TaskCompletionSource?)>(new BoundedChannelOptions(8192)
    { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
    private static readonly Task Writer = Task.Run(async () =>
    {
        await foreach (var entry in Pending.Reader.ReadAllAsync())
        {
            if (entry.Line != null) Persist(entry.Line);
            entry.Completion?.TrySetResult();
        }
    });
    public static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ElyProxy", "lifecycle.log");
    public static string Redact(string text)
    {
        string safe = Regex.Replace(text, @"\b(?:https?|socks5?|vless|vmess|trojan|ss|ssh|hy2|hysteria2|tuic|anytls|wireguard|naive(?:\+https)?|shadowtls)://[^\s]+", "[URL]", RegexOptions.IgnoreCase);
        return Regex.Replace(safe, """(?i)(password|passwd|token|authorization|private_key|privateKey|uuid)(["'\s:=]+)[^\s,;"']+""", "$1=[REDACTED]");
    }
    public static void Write(string operation, string message)
    {
        Pending.Writer.TryWrite(($"{DateTimeOffset.Now:O} pid={Environment.ProcessId} thread={Environment.CurrentManagedThreadId} {operation} {Redact(message)}\n", null));
    }
    public static async Task FlushAsync()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await Pending.Writer.WriteAsync((null, completion)).ConfigureAwait(false);
        await completion.Task.ConfigureAwait(false);
    }
    private static void Persist(string line)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                if (File.Exists(FilePath) && new FileInfo(FilePath).Length > 10 * 1024 * 1024)
                    File.Move(FilePath, FilePath + ".previous", true);
                File.AppendAllText(FilePath, line);
            }
        }
        catch { /* Diagnostic IO must never break network teardown. */ }
    }
}
