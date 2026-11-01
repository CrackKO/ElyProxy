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

    public async Task<SubscriptionRefreshResult> RefreshOneAsync(
        Subscription subscription,
        CancellationToken cancellationToken = default)
    {
        SubscriptionRefreshResult result = await _subscriptionService.FetchNodesAsync(
            subscription.Url,
            _parserService,
            subscription.UserAgent,
            subscription.Hwid,
            subscription.CustomHeaders,
            cancellationToken);
        if (!result.IsSuccess)
        {
            return result;
        }

        IReadOnlyList<VlessNode> filtered = ApplyFilterAndSort(result.Nodes, subscription);
        if (filtered.Count == 0 && result.Nodes.Count > 0)
        {
            return result with
            {
                Status = SubscriptionRefreshStatus.InvalidContent,
                Nodes = result.Nodes,
                ErrorMessage = $"Фильтр «{subscription.ServerFilter}» не оставил ни одного сервера; прежний список сохранён."
            };
        }

        ServerLibraryIdentity.Reconcile(subscription.Nodes, filtered);
        return result with { Nodes = filtered };
    }

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
                ApplyUserInfo(snapshot, refresh.UserInfo);
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

    public static void ApplyUserInfo(Subscription target, SubscriptionUserInfo? info)
    {
        if (info == null)
        {
            return;
        }

        target.UploadBytes = info.UploadBytes;
        target.DownloadBytes = info.DownloadBytes;
        target.TotalBytes = info.TotalBytes;
        target.ExpiresAt = info.ExpiresAt;
    }

    public static IReadOnlyList<VlessNode> ApplyFilterAndSort(IReadOnlyList<VlessNode> nodes, Subscription subscription)
    {
        IEnumerable<VlessNode> filtered = nodes;
        string filter = (subscription.ServerFilter ?? string.Empty).Trim();
        if (filter.Length > 0)
        {
            filtered = filtered.Where(node => node.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase));
        }

        return subscription.SortMode switch
        {
            "Name" => filtered.OrderBy(node => node.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList(),
            "Latency" => filtered.OrderBy(node => node.Latency ?? int.MaxValue).ToList(),
            _ => filtered.ToList()
        };
    }

    private static Subscription Clone(Subscription source) => new()
    {
        Id = source.Id,
        Name = source.Name,
        Url = source.Url,
        LastUpdated = source.LastUpdated,
        Nodes = source.Nodes.ToList(),
        LastUpdateError = source.LastUpdateError,
        UploadBytes = source.UploadBytes,
        DownloadBytes = source.DownloadBytes,
        TotalBytes = source.TotalBytes,
        ExpiresAt = source.ExpiresAt,
        UserAgent = source.UserAgent,
        UpdateIntervalMinutes = source.UpdateIntervalMinutes,
        ServerFilter = source.ServerFilter,
        SortMode = source.SortMode,
        Hwid = source.Hwid,
        CustomHeaders = new Dictionary<string, string>(source.CustomHeaders ?? new Dictionary<string, string>())
    };
}
