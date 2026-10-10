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
	IReadOnlyList<string>? BypassProcessPaths = null,
	bool FragmentEnabled = false,
	string FragmentPackets = "tlshello",
	string FragmentLength = "100-200",
	string FragmentInterval = "10-20",
	bool RecordFragment = false,
	string NoiseType = "",
	string NoisePacket = "",
	string NoiseDelay = "10-16",
	PerAppMode PerAppMode = PerAppMode.Bypass,
	int ClashApiPort = 0,
	string ClashApiSecret = "",
	bool TlsSpoofEnabled = false,
	string TlsSpoofSni = "",
	string TlsSpoofMethod = "wrong-sequence");
