using ElyProxy.Updater;

namespace ElyProxy.Tests;

public sealed class UpdaterCliTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ElyProxy-UpdaterCliTests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Parse_AcceptsAllArguments()
    {
        var options = UpdaterArguments.Parse(new[]
        {
            "--install-dir", @"C:\apps\ElyProxy",
            "--package", @"C:\temp\ElyProxy-1.3.9.zip",
            "--wait-pid", "1234"
        });

        Assert.Equal(@"C:\apps\ElyProxy", options.InstallDirectory);
        Assert.Equal(@"C:\temp\ElyProxy-1.3.9.zip", options.PackagePath);
        Assert.Equal(1234, options.WaitPid);
    }

    [Fact]
    public void Parse_IsOrderAndCaseInsensitive()
    {
        var options = UpdaterArguments.Parse(new[]
        {
            "--PACKAGE", "package.zip",
            "--Wait-Pid", "42",
            "--Install-Dir", "app"
        });

        Assert.Equal("app", options.InstallDirectory);
        Assert.Equal("package.zip", options.PackagePath);
        Assert.Equal(42, options.WaitPid);
    }

    [Fact]
    public void Parse_IgnoresUnknownPairs()
    {
        var options = UpdaterArguments.Parse(new[]
        {
            "--extra", "ignored",
            "--install-dir", "app",
            "--package", "pkg.zip",
            "--wait-pid", "7"
        });

        Assert.Equal(7, options.WaitPid);
    }

    [Theory]
    [InlineData()]
    [InlineData("--install-dir", "app")]
    [InlineData("--install-dir", "app", "--package", "pkg.zip")]
    [InlineData("--package", "pkg.zip", "--wait-pid", "1")]
    public void Parse_RejectsMissingArguments(params string[] args)
    {
        Assert.Throws<ArgumentException>(() => UpdaterArguments.Parse(args));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("abc")]
    [InlineData("")]
    public void Parse_RejectsInvalidProcessId(string processId)
    {
        Assert.Throws<ArgumentException>(() => UpdaterArguments.Parse(new[]
        {
            "--install-dir", "app",
            "--package", "pkg.zip",
            "--wait-pid", processId
        }));
    }

    [Fact]
    public void Validate_RejectsDriveRoot()
    {
        var root = Path.GetPathRoot(Path.GetFullPath(_directory));

        Assert.Throws<InvalidOperationException>(() => InstallPreflight.Validate(root!, Path.Combine(_directory, "pkg.zip")));
    }

    [Fact]
    public void Validate_RejectsInstallDirectoryWithoutClientExecutable()
    {
        Directory.CreateDirectory(_directory);

        Assert.Throws<FileNotFoundException>(() => InstallPreflight.Validate(_directory, Path.Combine(_directory, "pkg.zip")));
    }

    [Fact]
    public void Validate_RejectsMissingPackage()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "ElyProxy.exe"), string.Empty);

        Assert.Throws<FileNotFoundException>(() => InstallPreflight.Validate(_directory, Path.Combine(_directory, "missing.zip")));
    }

    [Fact]
    public void Validate_ReturnsNormalizedPaths()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "ElyProxy.exe"), string.Empty);
        var package = Path.Combine(_directory, "pkg.zip");
        File.WriteAllText(package, string.Empty);

        var (installDirectory, packagePath) = InstallPreflight.Validate(_directory + Path.DirectorySeparatorChar, package);

        Assert.Equal(Path.GetFullPath(_directory), installDirectory);
        Assert.Equal(Path.GetFullPath(package), packagePath);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
