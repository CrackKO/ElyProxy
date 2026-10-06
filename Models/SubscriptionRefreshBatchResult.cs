namespace ElyProxy.Models;

public sealed record SubscriptionRefreshBatchResult(
    IReadOnlyList<Subscription> Subscriptions,
    int UpdatedCount,
    int FailedCount);
