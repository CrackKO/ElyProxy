namespace ElyProxy.Models;

public class AppSettings
{
    public int SocksPort { get; set; } = 1080;
    public bool MinimizeToTray { get; set; } = true;
    public bool AutoConnect { get; set; }
    public string? LastConnectedNodeId { get; set; }
    public List<VlessNode> ManualNodes { get; set; } = new();
}
