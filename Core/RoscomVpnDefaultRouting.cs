using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ElyProxy.Core;

internal static class RoscomVpnDefaultRouting
{
	internal sealed record RuleSetAsset(string Tag, string FileName);

	public const string Name = "RoscomVPN DEFAULT";

	private const string AssetRelativePath = "Assets\\RoscomVPN";

	public static readonly string[] DirectSiteTags = new string[12]
	{
		"roscom-geosite-private", "roscom-geosite-category-ru", "roscom-geosite-whitelist", "roscom-geosite-microsoft", "roscom-geosite-apple", "roscom-geosite-epicgames", "roscom-geosite-riot", "roscom-geosite-escapefromtarkov", "roscom-geosite-steam", "roscom-geosite-twitch",
		"roscom-geosite-pinterest", "roscom-geosite-faceit"
	};

	public static readonly string[] ProxySiteTags = new string[5] { "roscom-geosite-google-play", "roscom-geosite-github", "roscom-geosite-twitch-ads", "roscom-geosite-youtube", "roscom-geosite-telegram" };

	public static readonly string[] BlockSiteTags = new string[3] { "roscom-geosite-win-spy", "roscom-geosite-torrent", "roscom-geosite-category-ads" };

	public static readonly string[] DirectIpTags = new string[2] { "roscom-geoip-private", "roscom-geoip-direct" };

	public static readonly IReadOnlyList<RuleSetAsset> RuleSets = new _003C_003Ez__ReadOnlyArray<RuleSetAsset>(new RuleSetAsset[22]
	{
		new RuleSetAsset("roscom-geosite-private", "geosite-private.srs"),
		new RuleSetAsset("roscom-geosite-category-ru", "geosite-category-ru.srs"),
		new RuleSetAsset("roscom-geosite-whitelist", "geosite-whitelist.srs"),
		new RuleSetAsset("roscom-geosite-microsoft", "geosite-microsoft.srs"),
		new RuleSetAsset("roscom-geosite-apple", "geosite-apple.srs"),
		new RuleSetAsset("roscom-geosite-epicgames", "geosite-epicgames.srs"),
		new RuleSetAsset("roscom-geosite-riot", "geosite-riot.srs"),
		new RuleSetAsset("roscom-geosite-escapefromtarkov", "geosite-escapefromtarkov.srs"),
		new RuleSetAsset("roscom-geosite-steam", "geosite-steam.srs"),
		new RuleSetAsset("roscom-geosite-twitch", "geosite-twitch.srs"),
		new RuleSetAsset("roscom-geosite-pinterest", "geosite-pinterest.srs"),
		new RuleSetAsset("roscom-geosite-faceit", "geosite-faceit.srs"),
		new RuleSetAsset("roscom-geosite-google-play", "geosite-google-play.srs"),
		new RuleSetAsset("roscom-geosite-github", "geosite-github.srs"),
		new RuleSetAsset("roscom-geosite-twitch-ads", "geosite-twitch-ads.srs"),
		new RuleSetAsset("roscom-geosite-youtube", "geosite-youtube.srs"),
		new RuleSetAsset("roscom-geosite-telegram", "geosite-telegram.srs"),
		new RuleSetAsset("roscom-geosite-win-spy", "geosite-win-spy.srs"),
		new RuleSetAsset("roscom-geosite-torrent", "geosite-torrent.srs"),
		new RuleSetAsset("roscom-geosite-category-ads", "geosite-category-ads.srs"),
		new RuleSetAsset("roscom-geoip-private", "geoip-private.srs"),
		new RuleSetAsset("roscom-geoip-direct", "geoip-direct.srs")
	});

	public static string GetAssetPath(string fileName)
	{
		return Path.Combine(GetAssetDirectory(), fileName);
	}

	public static IReadOnlyList<string> GetMissingAssetPaths()
	{
		return (from ruleSet in RuleSets
			select GetAssetPath(ruleSet.FileName) into path
			where !File.Exists(path)
			select path).ToArray();
	}

	private static string GetAssetDirectory()
	{
		string appDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets\\RoscomVPN");
		if (Directory.Exists(appDirectory))
		{
			return appDirectory;
		}
		for (DirectoryInfo current = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory); current != null; current = current.Parent)
		{
			if (File.Exists(Path.Combine(current.FullName, "ElyProxy.sln")))
			{
				return Path.Combine(current.FullName, "Assets\\RoscomVPN");
			}
		}
		return appDirectory;
	}
}
