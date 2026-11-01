using ElyProxy.Models;
using ElyProxy.Services;
namespace ElyProxy.Core;
public sealed record CoreCandidate(ProxyCoreKind Core, int Score, string? Rejection)
{
    public bool Compatible => Rejection == null;
    public IReadOnlyList<string> Reasons { get; init; } = Array.Empty<string>();
    public CorePerformanceRecord? HistoricalData { get; init; }
}
public static class CoreCandidateSelector
{
    public static IReadOnlyList<CoreCandidate> Evaluate(VlessNode node, ProxyCoreKind requested,
        bool singBoxInstalled, bool xrayInstalled, bool requiresXray = false, bool requiresSingBox = false,
        CorePerformanceHistoryService? history = null, string mode = "Proxy", bool hysteriaInstalled = false,
        bool allowHysteria = false, DateTimeOffset? now = null)
    {
        var cores = requested == ProxyCoreKind.Auto
            ? allowHysteria ? new[] { ProxyCoreKind.SingBox, ProxyCoreKind.Xray, ProxyCoreKind.Hysteria } : new[] { ProxyCoreKind.SingBox, ProxyCoreKind.Xray }
            : new[] { requested };
        var time = now ?? DateTimeOffset.UtcNow;
        var records = cores.ToDictionary(c => c, c => history?.Get(CorePerformanceHistoryService.Fingerprint(node, mode, c.ToString())));
        var lastSuccess = records.Where(p => p.Value?.LastSuccess != null).OrderByDescending(p => p.Value!.LastSuccess).Select(p => (ProxyCoreKind?)p.Key).FirstOrDefault();
        return cores.Select(core =>
        {
            string? reason = core switch {
                ProxyCoreKind.SingBox => !singBoxInstalled ? "sing-box не установлен" : requiresXray ? "Настройки требуют Xray" : ProxyProtocolSupport.DescribeSingBoxLimitation(node),
                ProxyCoreKind.Xray => !xrayInstalled ? "Xray не установлен" : requiresSingBox ? "Настройки требуют sing-box" : ProxyProtocolSupport.DescribeXrayLimitation(node),
                ProxyCoreKind.Hysteria => !hysteriaInstalled ? "Hysteria не установлен" : node.Protocol.ToLowerInvariant() is not ("hysteria2" or "hy2") ? "Требуется Hysteria2" : requiresXray || requiresSingBox || !allowHysteria ? "Настройки требуют другое ядро" : null,
                _ => "Неизвестное ядро" };
            int score = core == ProxyCoreKind.SingBox ? 50 : core == ProxyCoreKind.Hysteria ? 65 : 45;
            var reasons = new List<string> { $"static preference +{score}" };
            if (core == ProxyCoreKind.Xray && node.Security.Equals("reality", StringComparison.OrdinalIgnoreCase) && node.Flow.Contains("vision", StringComparison.OrdinalIgnoreCase)) { score += 15; reasons.Add("REALITY/Vision suitability +15"); }
            var data = records[core];
            if (data != null) {
                int reliability = (int)Math.Round((data.Reliability - .5) * 30 + Math.Min(15, data.Successes * 2));
                double age = data.LastFailure.HasValue ? Math.Max(0, (time - data.LastFailure.Value).TotalHours) : 10000;
                int penalty = (int)Math.Round(Math.Min(40, data.ConsecutiveFailures * 12) * Math.Exp(-age / 48));
                int speed = data.Successes > 0 ? (int)Math.Round(8 * Math.Clamp(1 - data.AverageStartupMs / 5000, 0, 1)) : 0;
                int last = lastSuccess == core ? 8 : 0;
                score += reliability - penalty + speed + last;
                reasons.Add($"reliability {reliability:+0;-0;0}; recent failures -{penalty}; startup +{speed}; last success +{last}");
            }
            return new CoreCandidate(core, reason == null ? score : 0, reason) { Reasons = reasons, HistoricalData = data };
        }).OrderByDescending(c => c.Compatible).ThenByDescending(c => c.Score).ToArray();
    }
}

