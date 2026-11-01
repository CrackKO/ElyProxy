using ElyProxy.Updater;

namespace ElyProxy.Tests;

public sealed class DirectoryReplacerTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ElyProxy-ReplacerTests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void CopyOver_ReplacesFilesAndSkipsLockedOnes()
    {
        var staging = Path.Combine(_directory, "staging");
        var install = Path.Combine(_directory, "install");
        Directory.CreateDirectory(staging);
        Directory.CreateDirectory(install);
        File.WriteAllText(Path.Combine(staging, "app.exe"), "new");
        File.WriteAllText(Path.Combine(staging, "helper.dll"), "new-helper");
        File.WriteAllText(Path.Combine(install, "app.exe"), "old");
        var locked = Path.Combine(install, "helper.dll");
        File.WriteAllText(locked, "old-helper");

        List<string> skipped;
        using (new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            skipped = DirectoryReplacer.CopyOver(staging, install).ToList();
        }

        Assert.Equal("new", File.ReadAllText(Path.Combine(install, "app.exe")));
        Assert.Contains(skipped, path => path.EndsWith("helper.dll", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("old-helper", File.ReadAllText(locked));
    }

    [Fact]
    public void RemoveStale_DeletesLeftoversAndKeepsLockedFiles()
    {
        var staging = Path.Combine(_directory, "staging");
        var install = Path.Combine(_directory, "install");
        Directory.CreateDirectory(staging);
        Directory.CreateDirectory(install);
        File.WriteAllText(Path.Combine(staging, "app.exe"), "x");
        File.WriteAllText(Path.Combine(install, "app.exe"), "x");
        File.WriteAllText(Path.Combine(install, "old.dll"), "stale");
        var locked = Path.Combine(install, "locked.dll");
        File.WriteAllText(locked, "stale-locked");

        List<string> skipped;
        using (new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            skipped = DirectoryReplacer.RemoveStale(install, staging).ToList();
        }

        Assert.False(File.Exists(Path.Combine(install, "old.dll")));
        Assert.Contains(skipped, path => path.EndsWith("locked.dll", StringComparison.OrdinalIgnoreCase));
        Assert.True(File.Exists(locked));
    }

    [Fact]
    public void Clear_DeletesEverythingExceptLockedFiles()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "free.txt"), "x");
        var locked = Path.Combine(_directory, "locked.txt");
        File.WriteAllText(locked, "x");

        List<string> skipped;
        using (new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            skipped = DirectoryReplacer.Clear(_directory).ToList();
        }

        Assert.False(File.Exists(Path.Combine(_directory, "free.txt")));
        Assert.Contains(skipped, path => path.EndsWith("locked.txt", StringComparison.OrdinalIgnoreCase));
        Assert.True(File.Exists(locked));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
