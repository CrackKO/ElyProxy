using ElyProxy.Models;
using ElyProxy.ViewModels;
using Newtonsoft.Json;

namespace ElyProxy.Tests;

public sealed class ServerLibraryTests
{
    [Fact]
    public void Identity_SurvivesSubscriptionRefreshAndRename()
    {
        var node = new VlessNode { Address = "server.example", Port = 443, UUID = "account", Name = "Old" };
        var refreshed = JsonConvert.DeserializeObject<VlessNode>(JsonConvert.SerializeObject(node))!;
        refreshed.Name = "New";
        refreshed.Latency = 42;
        Assert.Equal(MainViewModel.LibraryKey(node), MainViewModel.LibraryKey(refreshed));
    }

    [Theory]
    [InlineData("password")]
    [InlineData("protocol")]
    [InlineData("port")]
    [InlineData("extra")]
    public void Identity_DistinguishesDifferentAccountsAndEndpoints(string change)
    {
        var first = new VlessNode { Address = "shared.example", Port = 443, Password = "first" };
        var other = new VlessNode { Address = "shared.example", Port = 443, Password = "first" };
        if (change == "password") other.Password = "second";
        if (change == "protocol") other.Protocol = "trojan";
        if (change == "port") other.Port = 8443;
        if (change == "extra") other.SetExtra("private_key", "different-wireguard-key");
        Assert.NotEqual(MainViewModel.LibraryKey(first), MainViewModel.LibraryKey(other));
        Assert.Matches("^[A-F0-9]{64}$", MainViewModel.LibraryKey(first));
    }

    [Theory]
    [InlineData("🇩🇪 Германия · Frankfurt", "Германия · Frankfurt")]
    [InlineData("🇫🇮Helsinki", "Helsinki")]
    [InlineData("DE Frankfurt", "DE Frankfurt")]
    [InlineData("🚀 Fast server", "🚀 Fast server")]
    public void DisplayName_RemovesOnlyLeadingCountryEmoji(string name, string expected) =>
        Assert.Equal(expected, MainViewModel.ServerDisplayName(name));

    [Fact]
    public void Settings_RoundTripFavoritesAndRecentWithoutStoringCredentials()
    {
        var key = MainViewModel.LibraryKey(new VlessNode { Password = "secret-password", Address = "private.example" });
        var settings = new AppSettings { FavoriteServerIds = new() { key }, RecentServerIds = new() { key } };
        var json = JsonConvert.SerializeObject(settings);
        var restored = JsonConvert.DeserializeObject<AppSettings>(json)!;
        Assert.Equal(key, Assert.Single(restored.FavoriteServerIds));
        Assert.Equal(key, Assert.Single(restored.RecentServerIds));
        Assert.DoesNotContain("secret-password", json);
        Assert.DoesNotContain("private.example", json);
    }
}
