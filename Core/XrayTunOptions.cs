using System.Collections.Generic;
using ElyProxy.Models;

namespace ElyProxy.Core;

public sealed record XrayTunOptions(
	string InterfaceName,
	int Mtu,
	IReadOnlyList<string> DnsServers,
	bool IncludeIpv6,
	IReadOnlyList<string> BypassRules,
	IReadOnlyList<string> DirectIpCidrs,
	RoutingPreset RoutingPreset = RoutingPreset.Custom,
	IReadOnlyList<string>? BypassProcessPaths = null);
