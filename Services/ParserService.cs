using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Text;
using System.Web;
using ElyProxy.Models;

namespace ElyProxy.Services;

public class ParserService
{
	public List<VlessNode> ParseSubscriptionContent(string content)
	{
		string raw = content.Trim();
		List<string> list = (from l in (TryBase64Decode(raw) ?? raw).Split(new char[2] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
			select l.Trim() into l
			where l.StartsWith("vless://", StringComparison.OrdinalIgnoreCase)
			select l).ToList();
		List<VlessNode> nodes = new List<VlessNode>();
		foreach (string line in list)
		{
			VlessNode node = ParseVlessUri(line);
			if (node != null)
			{
				nodes.Add(node);
			}
		}
		return nodes;
	}

	public VlessNode? ParseVlessUri(string uri)
	{
		try
		{
			if (!uri.StartsWith("vless://", StringComparison.OrdinalIgnoreCase))
			{
				return null;
			}
			string text = uri;
			string body = text.Substring(8, text.Length - 8);
			string name = string.Empty;
			int fragmentIdx = body.LastIndexOf('#');
			int num;
			if (fragmentIdx >= 0)
			{
				text = body;
				num = fragmentIdx + 1;
				name = Uri.UnescapeDataString(text.Substring(num, text.Length - num));
				body = body.Substring(0, fragmentIdx);
			}
			string queryString = string.Empty;
			int queryIdx = body.IndexOf('?');
			if (queryIdx >= 0)
			{
				text = body;
				num = queryIdx + 1;
				queryString = text.Substring(num, text.Length - num);
				body = body.Substring(0, queryIdx);
			}
			int atIdx = body.IndexOf('@');
			if (atIdx < 0)
			{
				return null;
			}
			string uuid = body.Substring(0, atIdx);
			text = body;
			num = atIdx + 1;
			string hostPort = text.Substring(num, text.Length - num);
			if (string.IsNullOrWhiteSpace(uuid) || string.IsNullOrWhiteSpace(hostPort))
			{
				return null;
			}
			string address;
			int port;
			if (hostPort.StartsWith('['))
			{
				int closeBracket = hostPort.IndexOf(']');
				if (closeBracket < 0)
				{
					return null;
				}
				address = hostPort.Substring(1, closeBracket - 1);
				text = hostPort;
				num = closeBracket + 1;
				string portPart = text.Substring(num, text.Length - num);
				if (!portPart.StartsWith(':'))
				{
					return null;
				}
				text = portPart;
				port = int.Parse(text.Substring(1, text.Length - 1));
			}
			else
			{
				int lastColon = hostPort.LastIndexOf(':');
				if (lastColon < 0)
				{
					return null;
				}
				address = hostPort.Substring(0, lastColon);
				text = hostPort;
				num = lastColon + 1;
				port = int.Parse(text.Substring(num, text.Length - num));
			}
			bool flag = string.IsNullOrWhiteSpace(address);
			if (!flag)
			{
				bool flag2 = ((port < 1 || port > 65535) ? true : false);
				flag = flag2;
			}
			if (flag)
			{
				return null;
			}
			NameValueCollection qp = HttpUtility.ParseQueryString(queryString);
			return new VlessNode
			{
				Name = name,
				Address = address,
				Port = port,
				UUID = uuid,
				Flow = (qp["flow"] ?? string.Empty),
				Security = (qp["security"] ?? "none"),
				Network = (qp["type"] ?? "tcp"),
				SNI = (qp["sni"] ?? string.Empty),
				PublicKey = (qp["pbk"] ?? string.Empty),
				ShortId = (qp["sid"] ?? string.Empty),
				Fingerprint = (qp["fp"] ?? "chrome"),
				Alpn = (qp["alpn"] ?? string.Empty),
				Path = (qp["path"] ?? string.Empty),
				Host = (qp["host"] ?? string.Empty),
				HeaderType = (qp["headerType"] ?? string.Empty),
				SpiderX = (qp["spx"] ?? string.Empty),
				Encryption = (qp["encryption"] ?? "none")
			};
		}
		catch
		{
			return null;
		}
	}

	private static string? TryBase64Decode(string input)
	{
		try
		{
			string sanitized = input.Replace('-', '+').Replace('_', '/');
			switch (sanitized.Length % 4)
			{
			case 2:
				sanitized += "==";
				break;
			case 3:
				sanitized += "=";
				break;
			}
			byte[] bytes = Convert.FromBase64String(sanitized);
			string result = Encoding.UTF8.GetString(bytes);
			if (result.Contains("vless://", StringComparison.OrdinalIgnoreCase))
			{
				return result;
			}
			return null;
		}
		catch
		{
			return null;
		}
	}
}
