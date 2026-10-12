namespace ElyProxy.Models;

public class VlessNode
{
    // Stable per-entry identity; subscription refresh reconciles it with previous entries.
    public string LibraryId { get; set; } = Guid.NewGuid().ToString("N");

	public string Name { get; set; } = string.Empty;

	/// <summary>Proxy protocol: vless, vmess, trojan, shadowsocks, hysteria2, tuic, socks, http.</summary>
	public string Protocol { get; set; } = "vless";

	public string Address { get; set; } = string.Empty;

	public int Port { get; set; }

	public string UUID { get; set; } = string.Empty;

	public string Username { get; set; } = string.Empty;

	public string Password { get; set; } = string.Empty;

	/// <summary>Shadowsocks cipher method (aes-256-gcm, 2022-blake3-aes-128-gcm, ...).</summary>
	public string Method { get; set; } = string.Empty;

	public string AlterId { get; set; } = "0";

	public string VmessSecurity { get; set; } = "auto";

	public string Obfs { get; set; } = string.Empty;

	public string ObfsPassword { get; set; } = string.Empty;

	public bool AllowInsecure { get; set; }

	public string CongestionControl { get; set; } = string.Empty;

	public string UdpRelayMode { get; set; } = string.Empty;

	public string PinnedPeerCertificateChainSha256 { get; set; } = string.Empty;

	/// <summary>Hysteria2 port hopping range, e.g. "1000:2000" or "1000:2000,3000".</summary>
	public string ServerPorts { get; set; } = string.Empty;

	/// <summary>Hysteria2 port hopping interval, e.g. "30s".</summary>
	public string HopInterval { get; set; } = string.Empty;

	public string UpMbps { get; set; } = string.Empty;

	public string DownMbps { get; set; } = string.Empty;

	/// <summary>mKCP seed.</summary>
	public string Seed { get; set; } = string.Empty;

	/// <summary>xhttp mode (auto, packet-up, stream-up, stream-one).</summary>
	public string Mode { get; set; } = string.Empty;

	/// <summary>Protocol-specific fields (WireGuard keys, SSH credentials, AnyTLS options, ...).</summary>
	public Dictionary<string, string> Extra { get; set; } = new Dictionary<string, string>();

	public string GetExtra(string key)
	{
		return (Extra.TryGetValue(key, out string? value) ? (value ?? string.Empty) : string.Empty);
	}

	public void SetExtra(string key, string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			Extra.Remove(key);
		}
		else
		{
			Extra[key] = value!;
		}
	}

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

	public string DisplayName
	{
		get
		{
			if (!string.IsNullOrEmpty(Name))
			{
				return Name;
			}
			return $"{Address}:{Port}";
		}
	}

	public string LatencyDisplay
	{
		get
		{
			if (string.IsNullOrWhiteSpace(PingDetails))
			{
				if (!Latency.HasValue)
				{
					return "—";
				}
				return $"{Latency.Value} ms";
			}
			return PingDetails;
		}
	}

	public string ProtocolDisplay
	{
		get
		{
			switch (Protocol?.ToLowerInvariant())
			{
			case "vless":
				return "VLESS";
			case "vmess":
				return "VMess";
			case "trojan":
				return "Trojan";
			case "shadowsocks":
			case "ss":
				return "Shadowsocks";
			case "hysteria2":
				return "Hysteria2";
			case "tuic":
				return "TUIC";
			case "socks":
				return "SOCKS";
			case "http":
				return "HTTP";
			case "anytls":
				return "AnyTLS";
			case "ssh":
				return "SSH";
			case "wireguard":
				return "WireGuard";
			default:
				return string.IsNullOrEmpty(Protocol) ? "VLESS" : Protocol.ToUpperInvariant();
			}
		}
	}

	public string TransportDisplay
	{
		get
		{
			string sec = ((string.IsNullOrEmpty(Security) || Security == "none") ? "" : Security.ToUpperInvariant());
			string net = (string.IsNullOrEmpty(Network) ? "tcp" : Network);
			string transport = string.IsNullOrEmpty(sec) ? net : net + "+" + sec;
			string protocol = Protocol?.ToLowerInvariant() ?? "vless";
			if (protocol == "vless")
			{
				return transport;
			}
			if (protocol is "hysteria2" or "tuic")
			{
				return ProtocolDisplay;
			}
			return ProtocolDisplay + " " + transport;
		}
	}
}
