using System.Collections.Generic;

namespace ElyProxy.Models;

public class AppSettings
{
	public int SocksPort { get; set; } = 1080;

	public bool MinimizeToTray { get; set; } = true;

	public bool AutoStartWithWindows { get; set; }

	public bool AutoConnect { get; set; }

	public bool AutoReconnect { get; set; }

	public bool ShowLogs { get; set; } = true;

	public bool AutoUpdate { get; set; }

	public string PingMode { get; set; } = "Tcp";

	public string ProxyPingUrl { get; set; } = "https://youtube.com";

	public int ProxyPingConcurrency { get; set; } = 10;

	public bool SystemProxyEnabled { get; set; }

	public List<string> SystemProxyBypassRules { get; set; } = new List<string>();

	public List<string> ApplicationBypassPaths { get; set; } = new List<string>();

	public bool PacModeEnabled { get; set; }

	public int PacPort { get; set; } = 18080;

	public bool ProxyProEnabled { get; set; }

	public bool ElyTunEnabled { get; set; }

	public bool ElyTunIpv6Enabled { get; set; } = true;

	public TunCoreKind TunCore { get; set; } = TunCoreKind.SingBox;

	public Hysteria2Engine Hysteria2Engine { get; set; } = Hysteria2Engine.SingBox;

	public bool UseHelperService { get; set; }

	public FragmentModeKind FragmentMode { get; set; } = FragmentModeKind.Off;

	public string FragmentPackets { get; set; } = "tlshello";

	public string FragmentLength { get; set; } = "100-200";

	public string FragmentInterval { get; set; } = "10-20";

	public bool FragmentRecord { get; set; }

	public PerAppMode PerAppMode { get; set; } = PerAppMode.Bypass;

	public string NoiseType { get; set; } = string.Empty;

	public string NoisePacket { get; set; } = string.Empty;

	public string NoiseDelay { get; set; } = "10-16";

	public bool TlsSpoofEnabled { get; set; }

	public string TlsSpoofSni { get; set; } = string.Empty;

	public string TlsSpoofMethod { get; set; } = "wrong-sequence";

	public RoutingPreset RoutingPreset { get; set; }

	public string? PreviousAutoConfigUrl { get; set; }

	public int SubscriptionUpdateIntervalMinutes { get; set; }

	public string? LastConnectedNodeId { get; set; }

	public List<VlessNode> ManualNodes { get; set; } = new List<VlessNode>();

	public string ThemeMode { get; set; } = "System";

	public string BackdropMode { get; set; } = "Auto";

	public string MotionMode { get; set; } = "System";

	public string ServerBadgeMode { get; set; } = "Flag";
}
