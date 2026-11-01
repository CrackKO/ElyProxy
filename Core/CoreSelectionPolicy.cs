using ElyProxy.Models;

namespace ElyProxy.Core;

public enum ProxyCoreKind { Auto, SingBox, Xray }
public sealed record ResolvedConnectionPlan(ProxyCoreKind Core, string Reason);

/// <summary>Pure, deterministic selection. Does not probe the network or change settings.</summary>
public static class CoreSelectionPolicy
{
    // Probe configuration has no system routing or TUN mode. Legacy mode-dependent selection
    // must not leak into these local-only probes. Explicit proxy core and DPI remain respected.
    public static ResolvedConnectionPlan ResolveProbe(VlessNode node, ProxyCoreKind? requested,
        DpiSettings dpi, bool singBoxInstalled, bool xrayInstalled)
    {
        bool xray = dpi.Xray.Fragment || !string.IsNullOrEmpty(dpi.Xray.Noise) || !string.IsNullOrEmpty(dpi.Xray.Fingerprint);
        bool singBox = dpi.SingBox.Fragment || dpi.SingBox.Records || dpi.SingBox.Spoof || !string.IsNullOrEmpty(dpi.SingBox.Fingerprint);
        return Resolve(node, requested ?? ProxyCoreKind.Auto, singBoxInstalled, xrayInstalled,
            requiresXray: xray && !singBox, requiresSingBox: singBox && !xray);
    }

    public static ResolvedConnectionPlan Resolve(VlessNode node, ProxyCoreKind requested,
        bool singBoxInstalled, bool xrayInstalled, bool requiresXray = false, bool requiresSingBox = false)
    {
        string? Limitation(ProxyCoreKind core) => core == ProxyCoreKind.SingBox
            ? !singBoxInstalled ? "sing-box не установлен" : requiresXray ? "Настройки требуют Xray" : ProxyProtocolSupport.DescribeSingBoxLimitation(node)
            : !xrayInstalled ? "Xray не установлен" : requiresSingBox ? "Настройки требуют sing-box" : ProxyProtocolSupport.DescribeXrayLimitation(node);
        var candidates = requested == ProxyCoreKind.Auto
            ? new[] { ProxyCoreKind.SingBox, ProxyCoreKind.Xray } : new[] { requested };
        var reasons = new List<string>();
        foreach (var core in candidates)
        {
            string? limitation = Limitation(core);
            if (limitation == null) return new(core, requested == ProxyCoreKind.Auto
                ? $"Auto: {(core == ProxyCoreKind.SingBox ? "sing-box" : "Xray")} — совместим с сервером и настройками" : "Ядро выбрано вручную");
            reasons.Add(limitation);
        }
        throw new NotSupportedException(string.Join("; ", reasons.Distinct()));
    }
}
