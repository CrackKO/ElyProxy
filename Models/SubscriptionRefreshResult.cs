namespace ElyProxy.Models;

public enum SubscriptionRefreshStatus
{
    Success,
    Empty,
    InvalidContent,
    RequestFailed
}

public sealed record SubscriptionRefreshResult(
    SubscriptionRefreshStatus Status,
    IReadOnlyList<VlessNode> Nodes,
    string? ErrorMessage = null)
{
    public bool IsSuccess => Status == SubscriptionRefreshStatus.Success;

    public int NodeCount => Nodes.Count;
}
