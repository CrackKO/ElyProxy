using System.Security.Cryptography;
using System.Text;
using ElyProxy.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ElyProxy.Services;

public static class ServerLibraryIdentity
{
    public static string Key(VlessNode node)
    {
        if (string.IsNullOrWhiteSpace(node.LibraryId)) node.LibraryId = Guid.NewGuid().ToString("N");
        return Hash("entry:" + node.LibraryId);
    }

    public static string LegacyKey(VlessNode node) => Hash(JsonConvert.SerializeObject(new
    {
        node.Protocol, node.UUID, node.Address, node.Port, node.Network, node.Security,
        node.SNI, node.PublicKey, node.ShortId, node.Username, node.Password, node.Method,
        Extra = node.Extra.OrderBy(entry => entry.Key, StringComparer.Ordinal).ToArray()
    }));

    public static string ConnectionKey(VlessNode node)
    {
        var json = JObject.FromObject(node);
        foreach (string field in new[] { "LibraryId", "Name", "DisplayName", "Latency", "LatencyDisplay", "PingDetails", "ProtocolDisplay", "TransportDisplay" })
            json.Remove(field);
        json["Extra"] = JObject.FromObject(node.Extra.OrderBy(p => p.Key, StringComparer.Ordinal).ToDictionary(p => p.Key, p => p.Value));
        return Hash(json.ToString(Formatting.None));
    }

    public static void Reconcile(IReadOnlyList<VlessNode> previous, IReadOnlyList<VlessNode> refreshed)
    {
        var oldGroups = previous.GroupBy(ConnectionKey).ToDictionary(g => g.Key, g => g.ToList());
        foreach (var group in refreshed.GroupBy(ConnectionKey))
        {
            if (!oldGroups.TryGetValue(group.Key, out var candidates)) continue;
            var remaining = candidates.ToList();
            var unmatched = new List<VlessNode>();
            foreach (var node in group)
            {
                var match = remaining.FirstOrDefault(p => p.Name == node.Name);
                if (match == null) { unmatched.Add(node); continue; }
                node.LibraryId = match.LibraryId;
                remaining.Remove(match);
            }
            // A unique connection can be renamed without losing its favorite.
            // Never guess between multiple identical routes with different labels.
            if (unmatched.Count == 1 && remaining.Count == 1)
                unmatched[0].LibraryId = remaining[0].LibraryId;
        }
    }

    public static IReadOnlyList<string> Migrate(IEnumerable<string> keys, IReadOnlyList<VlessNode> nodes)
    {
        var current = nodes.Select(Key).ToHashSet(StringComparer.Ordinal);
        var legacy = nodes.GroupBy(LegacyKey).ToDictionary(g => g.Key, g => g.ToArray());
        return keys.Select(key => current.Contains(key) ? key :
            legacy.TryGetValue(key, out var matches) && matches.Length == 1 ? Key(matches[0]) : null)
            .Where(key => key != null).Cast<string>().Distinct().ToArray();
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
