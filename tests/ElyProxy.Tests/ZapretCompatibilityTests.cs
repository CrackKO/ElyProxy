using ElyProxy.Helper;

namespace ElyProxy.Tests;

public sealed class ZapretCompatibilityTests
{
    private static string Build(string original, IEnumerable<int> indices) =>
        ZapretCompatibility.BuildImagePath(original, indices, "outbound and tcp.DstPort == 443");

    private const string Original = "\"C:\\Zapret folder\\bin\\winws.exe\" --wf-tcp=80,443 --wf-udp=443,50000-50100 --hostlist=\"C:\\Zapret folder\\lists\\list.txt\" --dpi-desync=fake --new --filter-udp=443";

    [Fact]
    public void Filter_PreservesStrategiesAndLimitsBothTcpAndUdpToHardwareInterfaces()
    {
        string configured = Build(Original, [12, 7, 12]);
        Assert.StartsWith(Original + " --wf-raw=\"((outbound and tcp.DstPort == 443) and (ifIdx == 7 or ifIdx == 12)", configured);
        Assert.Contains("not ip or (ip.SrcAddr != 172.19.0.1 and ip.DstAddr != 172.19.0.1)", configured);
        Assert.Contains("not ipv6 or (ipv6.SrcAddr != fdfe:dcba:9876::1 and ipv6.DstAddr != fdfe:dcba:9876::1)", configured);
        var command = ZapretCompatibility.ParseCommand(configured);
        Assert.Equal(@"C:\Zapret folder\bin\winws.exe", command.Executable);
        Assert.Contains("--wf-udp=443,50000-50100", command.Arguments);
    }

    [Fact]
    public void Filter_PreservesExistingPartialFilter()
    {
        string original = Original + " --wf-raw-part=\"outbound\"";
        Assert.StartsWith(original, Build(original, [7]));
    }

    [Fact]
    public void Filter_ConstrainsTheWholeOriginalTcpOrUdpExpression()
    {
        string target = ZapretCompatibility.BuildImagePath(Original, [7], "outbound and (tcp.DstPort == 443 or udp.DstPort == 443)\r\n");
        Assert.Contains("--wf-raw=\"((outbound and (tcp.DstPort == 443 or udp.DstPort == 443)) and (ifIdx == 7)", target);
        Assert.DoesNotContain("--wf-raw-part", target);
        Assert.DoesNotContain('\n', target);
        Assert.DoesNotContain('\r', target);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("tcp\" --other-argument")]
    public void Filter_RefusesEmptyFilterOrCommandLineInjection(string captureFilter)
    {
        Assert.Throws<NotSupportedException>(() => ZapretCompatibility.BuildImagePath(Original, [7], captureFilter));
    }

    [Theory]
    [InlineData(" --wf-raw=\"tcp\"")]
    [InlineData(" --wf-raw @filter.txt")]
    [InlineData(" --wf-save=filter.txt")]
    public void Filter_RefusesOptionsWhichOverrideOrDisableCapture(string option)
    {
        Assert.Throws<NotSupportedException>(() => Build(Original + option, [7]));
    }

    [Theory]
    [InlineData("cmd.exe /c zapret.bat")]
    [InlineData("\"C:\\Windows\\System32\\cmd.exe\" /c zapret.bat")]
    [InlineData("winws.exe --wf-tcp=443")]
    [InlineData("\"C:\\Zapret folder\\bin\\winws2.exe\" --wf-tcp=443")]
    [InlineData("")]
    public void Filter_RefusesWrappersRelativePathsAndOtherExecutables(string command)
    {
        Assert.Throws<NotSupportedException>(() => Build(command, [7]));
    }

    [Fact]
    public void Filter_RefusesEmptyOrInvalidHardwareInterfaceList()
    {
        Assert.Throws<NotSupportedException>(() => Build(Original, []));
        Assert.Throws<NotSupportedException>(() => Build(Original, [0, 7]));
        Assert.Throws<NotSupportedException>(() => Build(Original, [-1]));
    }

    [Fact]
    public void Command_ParsesUnquotedExecutableWithoutChangingArguments()
    {
        var command = ZapretCompatibility.ParseCommand(@"C:\zapret\winws.exe --wf-tcp=443");
        Assert.Equal(@"C:\zapret\winws.exe", command.Executable);
        Assert.Equal(" --wf-tcp=443", command.Arguments);
    }

    [Theory]
    [InlineData("original", "", "", true)]
    [InlineData("applied", "original", "applied", true)]
    [InlineData("original", "original", "applied", true)]
    [InlineData("external-update", "original", "applied", false)]
    [InlineData("external-update", "original", "", false)]
    [InlineData("applied", "", "applied", false)]
    public void Backup_DoesNotOverwriteExternallyChangedOrIncompleteState(string current, string original, string applied, bool expected)
    {
        Assert.Equal(expected, ZapretCompatibility.CanChange(current, original, applied));
    }
}
