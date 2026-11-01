using System.Collections.Generic;
using ElyProxy.Models;

namespace ElyProxy.Services;

/// <summary>
/// Popular VPN/proxy client User-Agents. Many subscription providers return
/// different server sets (or unlock features/HWID modes) based on this header.
/// </summary>
public static class UserAgentCatalog
{
	public static IReadOnlyList<UserAgentPreset> Presets { get; } = new UserAgentPreset[]
	{
		new("Свой вариант (по умолчанию ElyProxy)", "ElyProxy/1.0"),
		new("Happ 4.4.8 (Windows)", "Happ/4.4.8"),
		new("Happ 3.13.0 (Android)", "Happ/3.13.0"),
		new("Happ Lite (iOS)", "Happ/3.13.0"),
		new("v2rayN 7.14 (Windows)", "v2rayN/7.14"),
		new("v2rayNG 1.9.16 (Android)", "v2rayNG/1.9.16"),
		new("v2rayNG 1.8.5 (Android)", "v2rayNG/1.8.5"),
		new("v2rayA (Web)", "v2rayA/2.2.6"),
		new("V2RayX (macOS)", "V2RayX/1.5.0"),
		new("V2rayU 4.2.4 (macOS)", "V2rayU/4.2.4"),
		new("Qv2ray 2.7.0", "Qv2ray/2.7.0"),
		new("Nekoray 3.26 (Windows)", "NekoRay/3.26"),
		new("NekoBox 1.3.8 (Android)", "NekoBox/1.3.8"),
		new("Hiddify 2.5.7", "Hiddify/2.5.7"),
		new("Karing 1.1.0", "Karing/1.1.0"),
		new("sing-box 1.14.2 (CLI)", "sing-box/1.14.2"),
		new("SFA 1.14.2 (Android)", "SFA/1.14.2"),
		new("SFI 1.14.2 (iOS)", "SFI/1.14.2"),
		new("SFM 1.14.2 (macOS)", "SFM/1.14.2"),
		new("Clash Verge Rev 2.3.0", "clash-verge/v2.3.0"),
		new("Mihomo 1.19.2", "mihomo/1.19.2"),
		new("Clash Meta 1.19.2", "Clash.Meta/1.19.2"),
		new("Clash for Windows 0.20.39", "ClashforWindows/0.20.39"),
		new("ClashX Pro (macOS)", "ClashX Pro/1.95.1"),
		new("Clash 1.18.0", "clash/1.18.0"),
		new("Shadowrocket 2.2.59 (iOS)", "Shadowrocket/2.2.59"),
		new("Stash 2.7.0 (iOS)", "Stash/2.7.0"),
		new("Streisand 2.3.0 (iOS/macOS)", "Streisand/2.3.0"),
		new("Loon 3.2.1 (iOS)", "Loon/3.2.1"),
		new("Surge 5.9.0 (iOS/macOS)", "Surge/5.9.0"),
		new("Quantumult X 1.4.0 (iOS)", "Quantumult%20X/1.4.0"),
		new("FoXray 1.2.0 (iOS)", "FoXray/1.2.0"),
		new("V2Box 1.0.0 (iOS)", "V2Box/1.0.0"),
		new("Passwall 4.78 (OpenWrt)", "passwall/4.78"),
		new("Xray 26.9.30 (CLI)", "Xray/26.9.30"),
		new("Clash Meta for Android 2.11.6", "ClashMetaForAndroid/2.11.6"),
		new("Husi 1.5.0 (Android)", "Husi/1.5.0"),
		new("Exclave 1.0.1 (Android)", "Exclave/1.0.1")
	};
}
