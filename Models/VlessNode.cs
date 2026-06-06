namespace ElyProxy.Models;

public class VlessNode
{
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public int Port { get; set; }
    public string UUID { get; set; } = string.Empty;
    public string Flow { get; set; } = string.Empty;
    public string Security { get; set; } = string.Empty;
    public string Network { get; set; } = "tcp";
    public string SNI { get; set; } = string.Empty;
    public string PublicKey { get; set; } = string.Empty;
    public string ShortId { get; set; } = string.Empty;
    public string Fingerprint { get; set; } = "chrome";
    public string Alpn { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public string HeaderType { get; set; } = string.Empty;
    public string SpiderX { get; set; } = string.Empty;
    public string Encryption { get; set; } = "none";
    public int? Latency { get; set; }
    public string PingDetails { get; set; } = string.Empty;

    public string DisplayName => string.IsNullOrEmpty(Name)
        ? $"{Address}:{Port}"
        : Name;

    public string LatencyDisplay => !string.IsNullOrWhiteSpace(PingDetails)
        ? PingDetails
        : Latency.HasValue
        ? $"{Latency.Value} ms"
        : "—";

    public string TransportDisplay
    {
        get
        {
            var sec = string.IsNullOrEmpty(Security) || Security == "none" ? "" : Security.ToUpperInvariant();
            var net = string.IsNullOrEmpty(Network) ? "tcp" : Network;
            return string.IsNullOrEmpty(sec) ? net : $"{net}+{sec}";
        }
    }
}
