namespace ElyProxy.Models;

public class ProxyProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public List<VlessNode> Nodes { get; set; } = new();
}
