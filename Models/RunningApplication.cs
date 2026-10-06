namespace ElyProxy.Models;

public sealed record RunningApplication(string Name, string ExecutablePath, int InstanceCount = 1)
{
    public static RunningApplication FromPath(string executablePath, int instanceCount = 1) =>
        new(System.IO.Path.GetFileNameWithoutExtension(executablePath), executablePath, instanceCount);
}
