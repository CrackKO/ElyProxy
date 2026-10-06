using ElyProxy.Models;

namespace ElyProxy.Services;

public sealed class RuntimePreflightService
{
    public RuntimePreflightResult Check(
        string xrayPath,
        string singBoxPath,
        bool isAdministrator,
        IReadOnlyCollection<string>? missingRoutingAssets = null)
    {
        bool xrayAvailable = File.Exists(xrayPath);
        bool singBoxAvailable = File.Exists(singBoxPath);
        var checks = new List<RuntimePreflightCheck>
        {
            new(
                "Xray",
                xrayAvailable,
                xrayAvailable ? "готов для SOCKS5 и Proxy Lite" : $"не найден: {xrayPath}"),
            new(
                "sing-box",
                singBoxAvailable,
                singBoxAvailable ? "готов для Proxy Pro и ElyTun" : $"не найден: {singBoxPath}; обычный SOCKS5 остаётся доступен"),
            new(
                "Права ElyTun",
                true,
                isAdministrator ? "приложение уже запущено с правами администратора" : "UAC будет запрошен при включении ElyTun"),
            new(
                "RoscomVPN DEFAULT",
                missingRoutingAssets == null || missingRoutingAssets.Count == 0,
                missingRoutingAssets == null || missingRoutingAssets.Count == 0
                    ? "встроенные rule-set файлы найдены"
                    : $"не найдены файлы: {string.Join(", ", missingRoutingAssets.Select(Path.GetFileName))}")
        };

        return new RuntimePreflightResult(checks);
    }
}
