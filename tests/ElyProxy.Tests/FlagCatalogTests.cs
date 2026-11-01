using ElyProxy.Services;

namespace ElyProxy.Tests;

public class FlagCatalogTests
{
    [Theory]
    [InlineData("\U0001F1F8\U0001F1EA Швеция", "SE")]
    [InlineData("\U0001F1E9\U0001F1EA Германия-2 EXTRA", "DE")]
    [InlineData("\U0001F1EC\U0001F1E7 Британия-1", "GB")]
    [InlineData("nl Amsterdam", "NL")]
    [InlineData("  us-1", "US")]
    [InlineData("UK London", "UK")]
    public void ParseCountryCode_reads_flag_emoji_or_leading_code(string name, string expected)
    {
        Assert.Equal(expected, FlagCatalog.ParseCountryCode(name));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("США")]
    [InlineData("7")]
    [InlineData("123")]
    public void ParseCountryCode_returns_null_without_code(string name)
    {
        Assert.Null(FlagCatalog.ParseCountryCode(name));
    }

    [Theory]
    [InlineData("uk", "GB")]
    [InlineData("UK", "GB")]
    [InlineData("el", "GR")]
    [InlineData("de", "DE")]
    [InlineData(" SE ", "SE")]
    public void Normalize_maps_aliases_and_case(string code, string expected)
    {
        Assert.Equal(expected, FlagCatalog.Normalize(code));
    }

    [Theory]
    [InlineData("")]
    [InlineData("usa")]
    [InlineData("1x")]
    [InlineData("?")]
    public void Normalize_rejects_invalid_codes(string code)
    {
        Assert.Null(FlagCatalog.Normalize(code));
    }

    [Fact]
    public void Bundled_flags_cover_common_countries()
    {
        var flags = Directory.GetFiles(Path.Combine(FindWorkspaceRoot(), "Assets", "Flags"), "*.png");

        Assert.True(flags.Length >= 250, $"expected at least 250 flag assets, found {flags.Length}");

        var names = flags.Select(Path.GetFileNameWithoutExtension).ToHashSet(StringComparer.Ordinal);

        foreach (var code in new[] { "ru", "us", "de", "nl", "gb", "fr", "fi", "pl", "vn", "ae", "es", "se", "eu", "xk", "un" })
        {
            Assert.Contains(code, names);
        }

        Assert.All(names, name => Assert.Matches("^[a-z]{2}$", name));
    }

    private static string FindWorkspaceRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ElyProxy.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("ElyProxy.sln not found above " + AppContext.BaseDirectory);
    }
}
