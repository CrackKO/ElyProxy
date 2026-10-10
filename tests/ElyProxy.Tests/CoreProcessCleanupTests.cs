using ElyProxy.Core;

namespace ElyProxy.Tests;

public sealed class CoreProcessCleanupTests
{
    [Theory]
    [InlineData(@"C:\Apps\ElyProxy\xray\xray.exe", @"C:\Apps\ElyProxy", @"C:\Users\me\AppData\Local\ElyProxy\cores", true)]
    [InlineData(@"C:\Users\me\AppData\Local\ElyProxy\cores\xray\xray.exe", @"C:\Apps\ElyProxy", @"C:\Users\me\AppData\Local\ElyProxy\cores", true)]
    [InlineData(@"c:\apps\elyproxy\XRAY\XRAY.EXE", @"C:\Apps\ElyProxy", @"C:\Users\me\AppData\Local\ElyProxy\cores", true)]
    [InlineData(@"C:\Apps\ElyProxy2\xray\xray.exe", @"C:\Apps\ElyProxy", @"C:\Users\me\AppData\Local\ElyProxy\cores", false)]
    [InlineData(@"C:\Other\xray.exe", @"C:\Apps\ElyProxy", @"C:\Users\me\AppData\Local\ElyProxy\cores", false)]
    [InlineData("", @"C:\Apps\ElyProxy", @"C:\Users\me\AppData\Local\ElyProxy\cores", false)]
    public void IsOurCorePath_MatchesOnlyAppAndCoresDirectories(
        string path,
        string appDirectory,
        string coresRoot,
        bool expected)
    {
        Assert.Equal(expected, CoreProcessCleanup.IsOurCorePath(path, appDirectory, coresRoot));
    }
}
