using System;
using System.IO;
using ElyProxy.Models;

namespace ElyProxy.Core;

public static class ProxyProtocolSupport
{
	public static bool NaiveAvailable()
	{
		string singBoxPath = CoreLocator.Resolve("sing-box", "sing-box.exe", "sing-box", Path.Combine("bin", "sing-box"), string.Empty);
		string? directory = Path.GetDirectoryName(singBoxPath);
		return directory != null && File.Exists(Path.Combine(directory, "libcronet.dll"));
	}

	public static bool SupportsXray(VlessNode node)
	{
		string protocol = (node.Protocol ?? "vless").ToLowerInvariant();
		if (protocol is not ("vless" or "vmess" or "trojan" or "shadowsocks" or "ss" or "socks" or "http"))
		{
			return false;
		}
		return SupportsXrayTransport(node);
	}

	public static bool SupportsSingBox(VlessNode node)
	{
		string protocol = (node.Protocol ?? "vless").ToLowerInvariant();
		if (protocol is "hysteria2" or "hy2" or "tuic" or "vless" or "vmess" or "trojan" or "shadowsocks" or "ss" or "socks" or "http" or "anytls" or "ssh" or "wireguard" or "naive" or "shadowtls")
		{
			if (protocol is "vless" && !string.IsNullOrWhiteSpace(node.Encryption) && !string.Equals(node.Encryption, "none", StringComparison.OrdinalIgnoreCase))
			{
				return false;
			}
			if (protocol == "naive" && !NaiveAvailable())
			{
				return false;
			}
			if (protocol == "shadowtls" && (string.IsNullOrWhiteSpace(node.GetExtra("ss-method")) || string.IsNullOrWhiteSpace(node.GetExtra("ss-password"))))
			{
				return false;
			}
			return SupportsSingBoxTransport(node);
		}
		return false;
	}

	/// <summary>Returns a human-readable reason when the selected node cannot run on the given core, otherwise null.</summary>
	public static string? DescribeXrayLimitation(VlessNode node) => SupportsXray(node) ? null : DescribeCoreLimitation(node, "Xray");

	public static string? DescribeSingBoxLimitation(VlessNode node) => SupportsSingBox(node) ? null : DescribeCoreLimitation(node, "sing-box");

	private static string? DescribeCoreLimitation(VlessNode node, string core)
	{
		string protocol = (node.Protocol ?? "vless").ToLowerInvariant();
		if (protocol is "hysteria2" or "hy2" or "tuic")
		{
			return core == "Xray"
				? $"Протокол {node.ProtocolDisplay} не поддерживается ядром Xray. Выберите sing-box (Proxy Pro или ElyTun) или tun2proxy."
				: null;
		}
		if (protocol is "vless" && core == "sing-box" && !string.IsNullOrWhiteSpace(node.Encryption) && !string.Equals(node.Encryption, "none", StringComparison.OrdinalIgnoreCase))
		{
			return "VLESS Encryption (пост-квантовое шифрование) поддерживается только ядром Xray. Выберите Proxy Lite или Xray TUN.";
		}
		string network = (node.Network ?? "tcp").ToLowerInvariant();
		if (network is "xhttp" or "splithttp")
		{
			return $"{core} не поддерживает транспорт xhttp. Выберите ядро Xray.";
		}
		if (network is "kcp" or "mkcp")
		{
			return $"{core} не поддерживает транспорт mKCP. Выберите ядро Xray.";
		}
		if (protocol == "naive" && core == "sing-box")
		{
			return "NaiveProxy требует libcronet.dll рядом с sing-box.exe. Обновите ядро sing-box: Настройки → Ядра → Обновить.";
		}
		if (protocol == "shadowtls" && core == "sing-box")
		{
			return "Для ShadowTLS нужна цепочка Shadowsocks (method/password). Импортируйте узел из sing-box JSON с outbound'ами shadowsocks + shadowtls.";
		}
		return $"Протокол {node.ProtocolDisplay} не поддерживается ядром {core}.";
	}

	private static bool SupportsXrayTransport(VlessNode node)
	{
		return true;
	}

	private static bool SupportsSingBoxTransport(VlessNode node)
	{
		string network = (node.Network ?? "tcp").ToLowerInvariant();
		return network is not ("xhttp" or "splithttp" or "kcp" or "mkcp");
	}
}
