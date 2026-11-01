using ElyProxy.Services;

namespace ElyProxy.Tests;

public sealed class ElyTunAvailabilityPolicyTests
{
    [Theory]
    [InlineData(true, false, false, true)]
    [InlineData(true, true, true, true)]
    [InlineData(false, true, true, true)]
    [InlineData(false, true, false, false)]
    [InlineData(false, false, true, false)]
    [InlineData(false, false, false, false)]
    public void CanRunWithoutElevation_RequiresAdminOrUsableHelper(
        bool isAdministrator,
        bool useHelperService,
        bool helperServiceRunning,
        bool expected)
    {
        Assert.Equal(expected, ElyTunAvailabilityPolicy.CanRunWithoutElevation(isAdministrator, useHelperService, helperServiceRunning));
    }
}
