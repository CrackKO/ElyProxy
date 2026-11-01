using ElyProxy.Models;

namespace ElyProxy.Services;

public static class ActiveServerIdentity
{
    public static bool Matches(VlessNode active, VlessNode candidate) =>
        !string.IsNullOrEmpty(active.LibraryId) && active.LibraryId == candidate.LibraryId
        && ServerLibraryIdentity.ConnectionKey(active) == ServerLibraryIdentity.ConnectionKey(candidate);
}
