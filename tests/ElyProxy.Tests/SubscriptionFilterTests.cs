using ElyProxy.Models;
using ElyProxy.Services;

namespace ElyProxy.Tests;

public class SubscriptionFilterTests
{
    [Fact]
    public void ApplyFilterAndSort_KeepsOnlyMatchingNames()
    {
        var subscription = new Subscription { ServerFilter = "er" };
        var nodes = new List<VlessNode>
        {
            new() { Name = "Germany 1", Address = "a", Port = 443 },
            new() { Name = "Netherlands", Address = "b", Port = 443 },
            new() { Name = "Japan", Address = "c", Port = 443 }
        };

        var filtered = SubscriptionRefreshCoordinator.ApplyFilterAndSort(nodes, subscription);

        Assert.Equal(2, filtered.Count);
        Assert.Contains(filtered, node => node.Name == "Germany 1");
        Assert.Contains(filtered, node => node.Name == "Netherlands");
    }

    [Fact]
    public void ApplyFilterAndSort_ReturnsEmptyWhenNothingMatches()
    {
        var subscription = new Subscription { ServerFilter = "zzz-not-found" };
        var nodes = new List<VlessNode>
        {
            new() { Name = "Alpha", Address = "a", Port = 443 }
        };

        var filtered = SubscriptionRefreshCoordinator.ApplyFilterAndSort(nodes, subscription);

        Assert.Empty(filtered);
    }

    [Fact]
    public void ApplyFilterAndSort_SortsByNameAndLatency()
    {
        var nodes = new List<VlessNode>
        {
            new() { Name = "Charlie", Address = "a", Port = 443, Latency = 50 },
            new() { Name = "alpha", Address = "b", Port = 443, Latency = 10 },
            new() { Name = "Bravo", Address = "c", Port = 443 }
        };

        var byName = SubscriptionRefreshCoordinator.ApplyFilterAndSort(nodes, new Subscription { SortMode = "Name" });
        var byLatency = SubscriptionRefreshCoordinator.ApplyFilterAndSort(nodes, new Subscription { SortMode = "Latency" });

        Assert.Equal(["alpha", "Bravo", "Charlie"], byName.Select(node => node.Name));
        Assert.Equal("alpha", byLatency.First().Name);
        Assert.Equal("Bravo", byLatency.Last().Name);
    }
}
