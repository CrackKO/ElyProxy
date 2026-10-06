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

	public string TransportDisplay
	{
		get
		{
			string sec = ((string.IsNullOrEmpty(Security) || Security == "none") ? "" : Security.ToUpperInvariant());
			string net = (string.IsNullOrEmpty(Network) ? "tcp" : Network);
			if (!string.IsNullOrEmpty(sec))
			{
				return net + "+" + sec;
			}
			return net;
		}
	}
}
