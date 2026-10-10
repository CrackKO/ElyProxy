using ElyProxy.Services;

namespace ElyProxy.Tests;

public class CoreUpdateServiceTests
{
    [Theory]
    [InlineData("v26.9.30", "26.9.30")]
    [InlineData("26.9.30", "26.9.30")]
    [InlineData("app/v2.13.0", "2.13.0")]
    [InlineData("v1.14.2-alpha.3", "1.14.2")]
    [InlineData("v0.8.4", "0.8.4")]
    public void ExtractVersion_ParsesReleaseTags(string tag, string expected)
    {
        Assert.Equal(expected, CoreUpdateService.ExtractVersion(tag));
    }

    [Fact]
    public void CoreIds_ContainsAllSupportedCores()
    {
        Assert.Equal(["xray", "sing-box", "tun2proxy", "hysteria"], CoreUpdateService.CoreIds);
    }

    [Fact]
    public void GetDisplayName_ResolvesKnownCore()
    {
        Assert.Equal("Hysteria 2", CoreUpdateService.GetDisplayName("hysteria"));
        Assert.Equal("unknown", CoreUpdateService.GetDisplayName("unknown"));
    }
}
