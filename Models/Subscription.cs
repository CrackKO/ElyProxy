namespace ElyProxy.Models;

public class Subscription
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public DateTime LastUpdated { get; set; }
    public List<VlessNode> Nodes { get; set; } = new();
}
