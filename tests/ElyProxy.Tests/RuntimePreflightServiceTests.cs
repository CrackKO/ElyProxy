using ElyProxy.Services;

namespace ElyProxy.Tests;

public sealed class RuntimePreflightServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ElyProxy-PreflightTests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Check_ReportsMissingEnginesAndExplainsElevation()
    {
        var service = new RuntimePreflightService();

        var result = service.Check(
            Path.Combine(_directory, "xray.exe"),
            Path.Combine(_directory, "sing-box.exe"),
            isAdministrator: false,
            missingRoutingAssets: []);

        Assert.True(result.HasBlockingIssue);
        Assert.Contains(result.Checks, check => check.Name == "Xray" && !check.Ready);
        Assert.Contains(result.Checks, check => check.Name == "sing-box" && !check.Ready);
        Assert.Contains(result.Checks, check => check.Name == "Права ElyTun" && check.Ready && check.Details.Contains("UAC"));
    }

    [Fact]
    public void Check_ReportsAvailableEnginesAndMissingRoutingFiles()
    {
        Directory.CreateDirectory(_directory);
        string xrayPath = Path.Combine(_directory, "xray.exe");
        string singBoxPath = Path.Combine(_directory, "sing-box.exe");
        File.WriteAllText(xrayPath, string.Empty);
        File.WriteAllText(singBoxPath, string.Empty);

        var result = new RuntimePreflightService().Check(
            xrayPath,
            singBoxPath,
            isAdministrator: true,
            missingRoutingAssets: ["missing.srs"]);

        Assert.True(result.HasBlockingIssue);
        Assert.Contains(result.Checks, check => check.Name == "Xray" && check.Ready);
        Assert.Contains(result.Checks, check => check.Name == "sing-box" && check.Ready);
        Assert.Contains("missing.srs", Assert.Single(result.Checks, check => check.Name == "RoscomVPN DEFAULT").Details);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
