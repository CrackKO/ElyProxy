namespace ElyProxy.Services;

/// <summary>
/// Decides whether ElyTun can run without an elevated relaunch: either the
/// process is already elevated, or the helper service will start the cores
/// (the opt-in setting is enabled and the service is actually running).
/// </summary>
public static class ElyTunAvailabilityPolicy
{
    public static bool CanRunWithoutElevation(bool isAdministrator, bool useHelperService, bool helperServiceRunning) =>
        isAdministrator || (useHelperService && helperServiceRunning);
}
