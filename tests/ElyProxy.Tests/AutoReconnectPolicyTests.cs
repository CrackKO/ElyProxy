using ElyProxy.Services;

namespace ElyProxy.Tests;

public sealed class AutoReconnectPolicyTests
{
    [Fact]
    public void TryBeginAttempt_AllowsOnlyConfiguredAttemptsWithinWindow()
    {
        var policy = new AutoReconnectPolicy(3, TimeSpan.FromMinutes(2));
        DateTime start = DateTime.UtcNow;

        Assert.True(policy.TryBeginAttempt(start));
        Assert.True(policy.TryBeginAttempt(start.AddSeconds(10)));
        Assert.True(policy.TryBeginAttempt(start.AddSeconds(20)));
        Assert.False(policy.TryBeginAttempt(start.AddSeconds(30)));
        Assert.True(policy.TryBeginAttempt(start.AddMinutes(2).AddSeconds(31)));
    }

    [Fact]
    public void Reset_AllowsFreshAttemptsAfterManualReconnect()
    {
        var policy = new AutoReconnectPolicy(1, TimeSpan.FromMinutes(2));
        DateTime now = DateTime.UtcNow;

        Assert.True(policy.TryBeginAttempt(now));
        Assert.False(policy.TryBeginAttempt(now.AddSeconds(1)));

        policy.Reset();

        Assert.True(policy.TryBeginAttempt(now.AddSeconds(2)));
    }
}
