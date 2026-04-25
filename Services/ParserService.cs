using System.Text;
using System.Web;
using ElyProxy.Models;

namespace ElyProxy.Services;

public class ParserService
{
    public List<VlessNode> ParseSubscriptionContent(string content)
    {
        var raw = content.Trim();
        var decoded = TryBase64Decode(raw);

        var lines = (decoded ?? raw)
            .Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => l.StartsWith("vless://", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var nodes = new List<VlessNode>();
        foreach (var line in lines)
        {
            var node = ParseVlessUri(line);
            if (node != null)
                nodes.Add(node);
        }
        return nodes;
    }

    public VlessNode? ParseVlessUri(string uri)
    {
        try
        {
            if (!uri.StartsWith("vless://", StringComparison.OrdinalIgnoreCase))
                return null;

            var body = uri[8..];

            string name = string.Empty;
            var fragmentIdx = body.LastIndexOf('#');
            if (fragmentIdx >= 0)
            {
                name = Uri.UnescapeDataString(body[(fragmentIdx + 1)..]);
                body = body[..fragmentIdx];
            }

            string queryString = string.Empty;
            var queryIdx = body.IndexOf('?');
            if (queryIdx >= 0)
            {
                queryString = body[(queryIdx + 1)..];
                body = body[..queryIdx];
            }

            var atIdx = body.IndexOf('@');
            if (atIdx < 0) return null;

            var uuid = body[..atIdx];
            var hostPort = body[(atIdx + 1)..];

            string address;
            int port;

            if (hostPort.StartsWith('['))
            {
                var closeBracket = hostPort.IndexOf(']');
                if (closeBracket < 0) return null;
                address = hostPort[1..closeBracket];
                var portPart = hostPort[(closeBracket + 1)..];
                if (!portPart.StartsWith(':')) return null;
                port = int.Parse(portPart[1..]);
            }
            else
            {
                var lastColon = hostPort.LastIndexOf(':');
                if (lastColon < 0) return null;
                address = hostPort[..lastColon];
                port = int.Parse(hostPort[(lastColon + 1)..]);
            }

            var qp = HttpUtility.ParseQueryString(queryString);

            return new VlessNode
            {
                Name = name,
                Address = address,
                Port = port,
                UUID = uuid,
                Flow = qp["flow"] ?? string.Empty,
                Security = qp["security"] ?? "none",
                Network = qp["type"] ?? "tcp",
                SNI = qp["sni"] ?? string.Empty,
                PublicKey = qp["pbk"] ?? string.Empty,
                ShortId = qp["sid"] ?? string.Empty,
                Fingerprint = qp["fp"] ?? "chrome",
                Alpn = qp["alpn"] ?? string.Empty,
                Path = qp["path"] ?? string.Empty,
                Host = qp["host"] ?? string.Empty,
                HeaderType = qp["headerType"] ?? string.Empty,
                SpiderX = qp["spx"] ?? string.Empty,
                Encryption = qp["encryption"] ?? "none",
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
            var sanitized = input
                .Replace('-', '+')
                .Replace('_', '/');

            var remainder = sanitized.Length % 4;
            if (remainder == 2) sanitized += "==";
            else if (remainder == 3) sanitized += "=";

            var bytes = Convert.FromBase64String(sanitized);
            var result = Encoding.UTF8.GetString(bytes);

            if (result.Contains("vless://", StringComparison.OrdinalIgnoreCase))
                return result;

            return null;
        }
        catch
        {
            return null;
        }
    }
}
