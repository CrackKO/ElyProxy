namespace ElyProxy.Core;

public sealed record XrayTunOptions(
    string InterfaceName,
    int Mtu,
    IReadOnlyList<string> DnsServers,
    bool IncludeIpv6);
