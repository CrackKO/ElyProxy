using ElyProxy.Core;
using ElyProxy.Models;
using ElyProxy.Services;

namespace ElyProxy.Tests;

public class SubscriptionHeadersAndHashTests
{
    [Fact]
    public void HeaderParser_ParsesLinesAndSkipsReservedHeaders()
    {
        const string text = """
            # комментарий
            X-Device-Os: Android
            X-Ver-Os:15
            User-Agent: Evil/1.0
            X-Hwid: abc
            broken-line
            Host: example.com
            """;

        var headers = SubscriptionHeaderParser.Parse(text);

        Assert.Equal(2, headers.Count);
        Assert.Equal("Android", headers["X-Device-Os"]);
        Assert.Equal("15", headers["X-Ver-Os"]);
        Assert.False(headers.ContainsKey("User-Agent"));
        Assert.False(headers.ContainsKey("X-Hwid"));
        Assert.False(headers.ContainsKey("Host"));
    }

    [Fact]
    public void HeaderParser_FormatsBackToText()
    {
        var headers = new Dictionary<string, string>
        {
            ["X-Device-Os"] = "Android",
            ["X-Ver-Os"] = "15"
        };

        var text = SubscriptionHeaderParser.Format(headers);

        Assert.Contains("X-Device-Os: Android", text);
        Assert.Contains("X-Ver-Os: 15", text);
        Assert.Equal(2, SubscriptionHeaderParser.Parse(text).Count);
    }

    [Fact]
    public void UserAgentCatalog_HasUniquePresetsWithValues()
    {
        Assert.True(UserAgentCatalog.Presets.Count >= 20);
        Assert.All(UserAgentCatalog.Presets, preset =>
        {
            Assert.False(string.IsNullOrWhiteSpace(preset.Name));
            Assert.False(string.IsNullOrWhiteSpace(preset.Value));
        });
        Assert.Equal(
            UserAgentCatalog.Presets.Count,
            UserAgentCatalog.Presets.Select(preset => preset.Name).Distinct().Count());
    }

    [Fact]
    public void ConfigHash_IsStableAndShort()
    {
        var first = ConfigHash.Compute("""{"a":1}""");
        var second = ConfigHash.Compute("""{"a":1}""");
        var other = ConfigHash.Compute("""{"a":2}""");

        Assert.Equal(first, second);
        Assert.NotEqual(first, other);
        Assert.Equal(12, first.Length);
        Assert.Equal(string.Empty, ConfigHash.Compute(null));
    }
}
