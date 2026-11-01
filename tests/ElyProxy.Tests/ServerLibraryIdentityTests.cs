using ElyProxy.Models;
using ElyProxy.Services;

namespace ElyProxy.Tests;

public sealed class ServerLibraryIdentityTests
{
    private static VlessNode Node(string name) => new() { Name = name, Address = "188.72.103.4", Port = 443, UUID = "same", Security = "tls" };

    [Fact]
    public void SharedEndpointEntriesRemainIndependentAfterReorder()
    {
        var old = new[] { Node("Germany"), Node("Finland") };
        var fresh = new[] { Node("Finland"), Node("Germany") };
        ServerLibraryIdentity.Reconcile(old, fresh);
        Assert.NotEqual(ServerLibraryIdentity.Key(old[0]), ServerLibraryIdentity.Key(old[1]));
        Assert.Equal(ServerLibraryIdentity.Key(old[0]), ServerLibraryIdentity.Key(fresh[1]));
        Assert.Equal(ServerLibraryIdentity.Key(old[1]), ServerLibraryIdentity.Key(fresh[0]));
    }

    [Fact]
    public void UniqueRenameKeepsIdentityButChangedTransportDoesNot()
    {
        var old = Node("Old");
        var renamed = Node("New");
        ServerLibraryIdentity.Reconcile(new[] { old }, new[] { renamed });
        Assert.Equal(old.LibraryId, renamed.LibraryId);
        var changed = Node("New");
        changed.Path = "/different";
        ServerLibraryIdentity.Reconcile(new[] { renamed }, new[] { changed });
        Assert.NotEqual(renamed.LibraryId, changed.LibraryId);
    }

    [Fact]
    public void LegacyCollisionNeverFavoritesAllEntries()
    {
        var nodes = new[] { Node("Germany"), Node("Finland") };
        var legacy = ServerLibraryIdentity.LegacyKey(nodes[0]);
        Assert.Empty(ServerLibraryIdentity.Migrate(new[] { legacy }, nodes));
        Assert.Equal(ServerLibraryIdentity.Key(nodes[0]), Assert.Single(ServerLibraryIdentity.Migrate(new[] { ServerLibraryIdentity.Key(nodes[0]) }, nodes)));
        Assert.Equal(ServerLibraryIdentity.Key(nodes[0]), Assert.Single(ServerLibraryIdentity.Migrate(new[] { legacy }, new[] { nodes[0] })));
    }
}
