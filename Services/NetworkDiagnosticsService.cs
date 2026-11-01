using System.Net;
using System.Net.NetworkInformation;
using System.Text;

namespace ElyProxy.Services;

public static class NetworkDiagnosticsService
{
    public static async Task<string> CaptureAsync(int port)
    {
        var text = new StringBuilder("Проверка не изменяет настройки сети. Запросы идут по текущему маршруту.\n\n");
        text.AppendLine($"Локальный порт {port}: {PortOwnerService.DescribeTcpPortOwner(port)}");
        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces().Where(a => a.OperationalStatus == OperationalStatus.Up))
        {
            var properties = adapter.GetIPProperties();
            text.AppendLine($"{adapter.Name}: DNS {string.Join(", ", properties.DnsAddresses)}");
        }
        const string host = "elysiumhub.org";
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(host).WaitAsync(TimeSpan.FromSeconds(5));
            text.AppendLine($"DNS сервера обновлений: {string.Join(", ", addresses.Select(a => a.ToString()))}");
        }
        catch (Exception ex) { text.AppendLine(NetworkErrorMessage.Describe(ex)); }
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            using var response = await client.GetAsync("https://elysiumhub.org/updates/elyproxy/update.json", HttpCompletionOption.ResponseHeadersRead);
            text.AppendLine($"HTTPS сервера обновлений: HTTP {(int)response.StatusCode}");
        }
        catch (Exception ex) { text.AppendLine(NetworkErrorMessage.Describe(ex)); }
        text.AppendLine("\nЕсли ошибка появилась после отключения VPN, повторите отключение и сохраните lifecycle.log из папки журналов ElyProxy.");
        return text.ToString();
    }
}
