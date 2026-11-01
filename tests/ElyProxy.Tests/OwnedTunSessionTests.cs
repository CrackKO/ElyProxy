using ElyProxy.Helper;
using Xunit;

namespace ElyProxy.Tests;
public class OwnedTunSessionTests
{
    [Fact]
    public void OwnershipIsReadFromQuotedCoreConfiguration()
    {
        var file = Path.Combine(Path.GetTempPath(), "elyproxy-ownership-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(file, "{\"inbounds\":[{\"type\":\"tun\",\"interface_name\":\"ElyTun-1234abcd\"}]}");
        try
        {
            var launch = new ElyProxy.Helper.Runtime.CoreLaunch("sing-box.exe", "run -c \"" + file + "\"", Path.GetTempPath());
            Assert.Equal("ElyTun-1234abcd", OwnedTunSession.FindName(launch));
            Assert.Null(OwnedTunSession.FindName(launch with { Arguments = "version" }));
        }
        finally { File.Delete(file); }
    }
    [Fact]
    public void CleanupRequiresMatchingSessionGuidAndNoLiveOwner()
    {
        var s = new OwnedTunSession.State("ElyTun-1234abcd", "guid", 1, 1);
        Assert.True(OwnedTunSession.CanClean(s, "guid", s.Name, false, false));
        Assert.False(OwnedTunSession.CanClean(s, "other-guid", s.Name, false, false));
        Assert.False(OwnedTunSession.CanClean(s, "guid", "Other VPN", false, false));
        Assert.False(OwnedTunSession.CanClean(s, "guid", s.Name, true, false));
        Assert.False(OwnedTunSession.CanClean(s, "guid", s.Name, false, true));
        Assert.False(OwnedTunSession.IsOwnedName("ElyTun-*"));
        Assert.False(OwnedTunSession.IsOwnedName("Ethernet"));
    }
}
