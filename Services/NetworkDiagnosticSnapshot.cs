using System.Net.NetworkInformation;
using Microsoft.Win32;

namespace ElyProxy.Services;

public static class NetworkDiagnosticSnapshot
{
    // Read-only, best effort. No endpoint resolution, recovery, or changes to Windows.
    public static void Write(string operation)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings");
            LifecycleLog.Write(operation, $"proxyEnabled={key?.GetValue("ProxyEnable")} pacPresent={!string.IsNullOrEmpty(key?.GetValue("AutoConfigURL") as string)}");
            foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces().Where(a => a.OperationalStatus == OperationalStatus.Up))
            {
                var properties = adapter.GetIPProperties();
                LifecycleLog.Write(operation, $"adapter={adapter.Id} type={adapter.NetworkInterfaceType} dns={string.Join(",", properties.DnsAddresses)} gateways={string.Join(",", properties.GatewayAddresses.Select(g => g.Address))}");
            }
        }
        catch (Exception ex) { LifecycleLog.Write(operation, "snapshotUnavailable=" + ex.GetType().Name); }
    }
}
