using ElyProxy.Core;
using ElyProxy.Models;
namespace ElyProxy.Services;
/// <summary>Missing storage gets Auto; deserialized legacy null stays mode-dependent manual.</summary>
public static class CoreSettingsMigration
{
    public static AppSettings NewInstallation() => new() { ProxyCore = ProxyCoreKind.Auto, TunCore = TunCoreKind.Auto };
    public static ProxyCoreKind ResolveLegacyProxy(AppSettings settings) => settings.ProxyCore
        ?? (settings.ProxyProEnabled ? ProxyCoreKind.SingBox : ProxyCoreKind.Xray);
}
