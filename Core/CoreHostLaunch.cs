using System.Diagnostics;
using System.Text;
using System.Text.Json;

#if ELYPROXY_HELPER
namespace ElyProxy.Helper.Runtime;
#else
namespace ElyProxy.Core;
#endif

public sealed record CoreLaunch(string File, string Arguments, string WorkingDirectory);
public static class CoreHostLaunch
{
    public static ProcessStartInfo Create(string helper, string file, string args, string? directory)
    {
        var info = new ProcessStartInfo(helper) { UseShellExecute = false, CreateNoWindow = false, WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        info.ArgumentList.Add("core-host");
        info.ArgumentList.Add(Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new CoreLaunch(file, args, directory ?? Path.GetDirectoryName(file) ?? "")))));
        return info;
    }
    public static bool IsCore(string file) => new[] { "xray.exe", "sing-box.exe", "tun2proxy-bin.exe", "hysteria.exe" }
        .Contains(Path.GetFileName(file), StringComparer.OrdinalIgnoreCase);
}

