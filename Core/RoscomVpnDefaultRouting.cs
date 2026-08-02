using ElyProxy.Models;

namespace ElyProxy.Core;

/// <summary>
/// Local sing-box rule-sets copied from hydraponique/roscomvpn-routing and its
/// RoscomVPN GeoIP/GeoSite dependencies. They are intentionally local: the
/// client must not download routing data while a user enables the preset.
/// </summary>
internal static class RoscomVpnDefaultRouting
{
    public const string Name = "RoscomVPN DEFAULT";
    private const string AssetRelativePath = "Assets\\RoscomVPN";

    public static readonly string[] DirectSiteTags =
    [
        "roscom-geosite-private",
        "roscom-geosite-category-ru",
        "roscom-geosite-whitelist",
        "roscom-geosite-microsoft",
        "roscom-geosite-apple",
        "roscom-geosite-epicgames",
        "roscom-geosite-riot",
        "roscom-geosite-escapefromtarkov",
        "roscom-geosite-steam",
        "roscom-geosite-twitch",
        "roscom-geosite-pinterest",
        "roscom-geosite-faceit"
    ];

    public static readonly string[] ProxySiteTags =
    [
        "roscom-geosite-google-play",
        "roscom-geosite-github",
        "roscom-geosite-twitch-ads",
        "roscom-geosite-youtube",
        "roscom-geosite-telegram"
    ];

    public static readonly string[] BlockSiteTags =
    [
        "roscom-geosite-win-spy",
        "roscom-geosite-torrent",
        "roscom-geosite-category-ads"
    ];

    public static readonly string[] DirectIpTags =
    [
        "roscom-geoip-private",
        "roscom-geoip-direct"
    ];

    public static readonly IReadOnlyList<RuleSetAsset> RuleSets =
    [
        new("roscom-geosite-private", "geosite-private.srs"),
        new("roscom-geosite-category-ru", "geosite-category-ru.srs"),
        new("roscom-geosite-whitelist", "geosite-whitelist.srs"),
        new("roscom-geosite-microsoft", "geosite-microsoft.srs"),
        new("roscom-geosite-apple", "geosite-apple.srs"),
        new("roscom-geosite-epicgames", "geosite-epicgames.srs"),
        new("roscom-geosite-riot", "geosite-riot.srs"),
        new("roscom-geosite-escapefromtarkov", "geosite-escapefromtarkov.srs"),
        new("roscom-geosite-steam", "geosite-steam.srs"),
        new("roscom-geosite-twitch", "geosite-twitch.srs"),
        new("roscom-geosite-pinterest", "geosite-pinterest.srs"),
        new("roscom-geosite-faceit", "geosite-faceit.srs"),
        new("roscom-geosite-google-play", "geosite-google-play.srs"),
        new("roscom-geosite-github", "geosite-github.srs"),
        new("roscom-geosite-twitch-ads", "geosite-twitch-ads.srs"),
        new("roscom-geosite-youtube", "geosite-youtube.srs"),
        new("roscom-geosite-telegram", "geosite-telegram.srs"),
        new("roscom-geosite-win-spy", "geosite-win-spy.srs"),
        new("roscom-geosite-torrent", "geosite-torrent.srs"),
        new("roscom-geosite-category-ads", "geosite-category-ads.srs"),
        new("roscom-geoip-private", "geoip-private.srs"),
        new("roscom-geoip-direct", "geoip-direct.srs")
    ];

    public static string GetAssetPath(string fileName) => Path.Combine(GetAssetDirectory(), fileName);

    public static IReadOnlyList<string> GetMissingAssetPaths() => RuleSets
        .Select(ruleSet => GetAssetPath(ruleSet.FileName))
        .Where(path => !File.Exists(path))
        .ToArray();

    private static string GetAssetDirectory()
    {
        var appDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, AssetRelativePath);
        if (Directory.Exists(appDirectory))
            return appDirectory;

        // Test projects reference the application assembly from another output
        // directory. Walking to the solution root is test-only fallback.
        var current = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "ElyProxy.sln")))
                return Path.Combine(current.FullName, AssetRelativePath);

            current = current.Parent;
        }

        return appDirectory;
    }

    internal sealed record RuleSetAsset(string Tag, string FileName);
}
