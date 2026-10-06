using ElyProxy.Models;

namespace ElyProxy.Services;

public sealed class SubscriptionRefreshCoordinator
{
    private readonly SubscriptionService _subscriptionService;
    private readonly ParserService _parserService;

    public SubscriptionRefreshCoordinator(SubscriptionService subscriptionService, ParserService parserService)
    {
        _subscriptionService = subscriptionService;
        _parserService = parserService;
    }

    public Task<SubscriptionRefreshResult> RefreshOneAsync(
        Subscription subscription,
        CancellationToken cancellationToken = default) =>
        _subscriptionService.FetchNodesAsync(subscription.Url, _parserService, cancellationToken);

    public async Task<SubscriptionRefreshBatchResult> RefreshAllAsync(
        IEnumerable<Subscription> subscriptions,
        CancellationToken cancellationToken = default)
    {
        var snapshots = new List<Subscription>();
        int updatedCount = 0;
        int failedCount = 0;

        foreach (Subscription source in subscriptions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Subscription snapshot = Clone(source);
            SubscriptionRefreshResult refresh = await RefreshOneAsync(source, cancellationToken);
            if (refresh.IsSuccess)
            {
                snapshot.Nodes = refresh.Nodes.ToList();
                snapshot.LastUpdated = DateTime.Now;
                snapshot.LastUpdateError = null;
                updatedCount++;
            }
            else
            {
                snapshot.LastUpdateError = refresh.ErrorMessage;
                failedCount++;
            }

            snapshots.Add(snapshot);
        }

        return new SubscriptionRefreshBatchResult(snapshots, updatedCount, failedCount);
    }

    private static Subscription Clone(Subscription source) => new()
    {
        Id = source.Id,
        Name = source.Name,
        Url = source.Url,
        LastUpdated = source.LastUpdated,
        Nodes = source.Nodes.ToList(),
        LastUpdateError = source.LastUpdateError
    };
}
