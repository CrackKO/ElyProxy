using System.IO.Compression;
using ElyProxy.Updater;

namespace ElyProxy.Tests;

public sealed class PackageExtractorTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ElyProxy-PackageTests-" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData("ElyProxy.exe")]
    [InlineData("sub/app.dll")]
    [InlineData("sub/../ok.txt")]
    [InlineData("updater/ElyProxy.Updater.exe")]
    public void IsEntrySafe_AcceptsEntriesInsideDestination(string entryName)
    {
        Assert.True(PackageExtractor.IsEntrySafe(entryName, _directory));
    }

    [Theory]
    [InlineData("../evil.txt")]
    [InlineData("..\\evil.txt")]
    [InlineData("sub/../../evil.txt")]
    [InlineData("/absolute.txt")]
    [InlineData("C:\\absolute.txt")]
    [InlineData("")]
    public void IsEntrySafe_RejectsTraversalAndAbsoluteEntries(string entryName)
    {
        Assert.False(PackageExtractor.IsEntrySafe(entryName, _directory));
    }

    [Fact]
    public void ExtractVerified_RejectsTraversalEntryAndWritesNothingOutside()
    {
        Directory.CreateDirectory(_directory);
        var zipPath = Path.Combine(_directory, "evil.zip");
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("../evil.txt");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("pwned");
        }

        var staging = Path.Combine(_directory, "staging");
        var escapedPath = Path.Combine(_directory, "evil.txt");

        Assert.Throws<InvalidDataException>(() => PackageExtractor.ExtractVerified(zipPath, staging));
        Assert.False(File.Exists(escapedPath));
    }

    [Fact]
    public void ExtractVerified_ExtractsNormalPackage()
    {
        Directory.CreateDirectory(_directory);
        var zipPath = Path.Combine(_directory, "package.zip");
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            WriteEntry(archive, "ElyProxy.exe", "client");
            WriteEntry(archive, "updater/ElyProxy.Updater.exe", "updater");
            WriteEntry(archive, "sub/data.txt", "data");
        }

        var staging = Path.Combine(_directory, "staging");
        PackageExtractor.ExtractVerified(zipPath, staging);

        Assert.Equal("client", File.ReadAllText(Path.Combine(staging, "ElyProxy.exe")));
        Assert.Equal("updater", File.ReadAllText(Path.Combine(staging, "updater", "ElyProxy.Updater.exe")));
        Assert.Equal("data", File.ReadAllText(Path.Combine(staging, "sub", "data.txt")));
    }

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
