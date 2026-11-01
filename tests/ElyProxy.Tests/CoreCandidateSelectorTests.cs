using ElyProxy.Core;
using ElyProxy.Models;
using Xunit;

namespace ElyProxy.Tests;

public class CoreCandidateSelectorTests
{
    [Theory]
    [InlineData("vless", 2)]
    [InlineData("vmess", 2)]
    [InlineData("trojan", 2)]
    [InlineData("shadowsocks", 2)]
    [InlineData("tuic", 1)]
    [InlineData("anytls", 1)]
    [InlineData("wireguard", 1)]
    [InlineData("unknown", 0)]
    public void FiltersProtocols(string protocol, int count)
    {
        var candidates = CoreCandidateSelector.Evaluate(new VlessNode { Protocol = protocol }, ProxyCoreKind.Auto, true, true);
        Assert.Equal(count, candidates.Count(c => c.Compatible));
    }

    [Fact]
    public void ManualNeverAddsAnAlternative()
    {
        var candidates = CoreCandidateSelector.Evaluate(new VlessNode { Protocol = "tuic" }, ProxyCoreKind.Xray, true, true);
        Assert.Single(candidates);
        Assert.False(candidates[0].Compatible);
    }

    [Fact]
    public void MissingBinaryHasAReason()
    {
        var candidates = CoreCandidateSelector.Evaluate(new VlessNode(), ProxyCoreKind.Auto, false, true);
        Assert.Equal(ProxyCoreKind.Xray, candidates[0].Core);
        Assert.Contains("не установлен", candidates[1].Rejection!);
    }
}
