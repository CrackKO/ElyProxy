namespace ElyProxy.Models;

public class SocksNode
{
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public int Port { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;

    public string DisplayName => string.IsNullOrEmpty(Name)
        ? $"SOCKS {Address}:{Port}"
        : Name;
}
