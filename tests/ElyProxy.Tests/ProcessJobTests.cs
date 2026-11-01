using System.Diagnostics;
using ElyProxy.Core;

namespace ElyProxy.Tests;

public sealed class ProcessJobTests
{
    [Fact]
    public void Assign_PutsProcessIntoKillOnCloseJob()
    {
        using var process = Process.Start(new ProcessStartInfo("ping.exe", "-n 30 127.0.0.1")
        {
            UseShellExecute = false,
            CreateNoWindow = true
        });
        Assert.NotNull(process);

        ProcessJob.Assign(process);

        Assert.True(ProcessJob.IsAssigned(process));
        process.Kill(entireProcessTree: true);
    }
}
