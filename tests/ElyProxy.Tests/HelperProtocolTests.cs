using System.Text.Json;
using ElyProxy.Helper;

namespace ElyProxy.Tests;

public sealed class HelperProtocolTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ElyProxy-HelperTests-" + Guid.NewGuid().ToString("N"));

    public HelperProtocolTests()
    {
        Directory.CreateDirectory(_directory);
    }

    [Theory]
    [InlineData("xray.exe")]
    [InlineData("sing-box.exe")]
    [InlineData("tun2proxy-bin.exe")]
    [InlineData("hysteria.exe")]
    public void IsAllowedExecutable_AcceptsCoreBinaries(string fileName)
    {
        var path = Path.Combine(_directory, fileName);
        File.WriteAllText(path, string.Empty);

        Assert.True(HelperProtocol.IsAllowedExecutable(path));
    }

    [Theory]
    [InlineData("cmd.exe")]
    [InlineData("powershell.exe")]
    [InlineData("evil.exe")]
    [InlineData("xray.exe.bak")]
    public void IsAllowedExecutable_RejectsEverythingElse(string fileName)
    {
        var path = Path.Combine(_directory, fileName);
        File.WriteAllText(path, string.Empty);

        Assert.False(HelperProtocol.IsAllowedExecutable(path));
    }

    [Fact]
    public void IsAllowedExecutable_RejectsMissingPathsAndEmptyInput()
    {
        Assert.False(HelperProtocol.IsAllowedExecutable(Path.Combine(_directory, "xray.exe")));
        Assert.False(HelperProtocol.IsAllowedExecutable(string.Empty));
        Assert.False(HelperProtocol.IsAllowedExecutable("   "));
    }

    [Fact]
    public void IsAllowedExecutable_RejectsDirectoryNamedLikeCore()
    {
        Directory.CreateDirectory(Path.Combine(_directory, "xray.exe"));

        Assert.False(HelperProtocol.IsAllowedExecutable(Path.Combine(_directory, "xray.exe")));
    }

    [Fact]
    public void IsAllowedExecutable_MatchesFileNameCaseInsensitively()
    {
        var path = Path.Combine(_directory, "XRAY.EXE");
        File.WriteAllText(path, string.Empty);

        Assert.True(HelperProtocol.IsAllowedExecutable(path));
    }

    [Fact]
    public void AllowedExecutables_ContainsExactlyTheFourCores()
    {
        Assert.Equal(
            new[] { "xray.exe", "sing-box.exe", "tun2proxy-bin.exe", "hysteria.exe" }.OrderBy(name => name, StringComparer.OrdinalIgnoreCase),
            HelperProtocol.AllowedExecutables.OrderBy(name => name, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void HelperRequest_DeserializesProtocolFields()
    {
        var request = JsonSerializer.Deserialize<HelperRequest>(
            """{"op":"start","id":"abc","file":"C:\\xray.exe","args":"run -c c.json","workdir":"C:\\"}""");

        Assert.NotNull(request);
        Assert.Equal("start", request.Operation);
        Assert.Equal("abc", request.Id);
        Assert.Equal(@"C:\xray.exe", request.File);
        Assert.Equal("run -c c.json", request.Arguments);
        Assert.Equal(@"C:\", request.WorkingDirectory);
    }

    [Fact]
    public void HelperRequest_MalformedJsonThrows()
    {
        Assert.ThrowsAny<JsonException>(() => JsonSerializer.Deserialize<HelperRequest>("{"));
    }

    [Fact]
    public void HelperRequest_WithoutOperationIsIgnoredByServer()
    {
        var request = JsonSerializer.Deserialize<HelperRequest>("""{"id":"abc"}""");

        Assert.NotNull(request);
        Assert.Null(request.Operation);
    }

    [Fact]
    public void HelperEvent_FactoriesProduceProtocolPayloads()
    {
        var started = HelperEvent.Started("id1", 42);
        Assert.Equal("started", started.Type);
        Assert.Equal("id1", started.Id);
        Assert.Equal(42, started.ProcessId);

        var output = HelperEvent.Output("id1", "line");
        Assert.Equal("stdout", output.Type);
        Assert.Equal("line", output.Line);

        var error = HelperEvent.Error("id1", "err");
        Assert.Equal("stderr", error.Type);
        Assert.Equal("err", error.Line);

        var exited = HelperEvent.Exited("id1", 3);
        Assert.Equal("exit", exited.Type);
        Assert.Equal(3, exited.ExitCode);

        var failure = HelperEvent.Failure("id1", "boom");
        Assert.Equal("error", failure.Type);
        Assert.Equal("boom", failure.Message);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
