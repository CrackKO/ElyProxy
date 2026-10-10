using System.Security.Cryptography;
using System.Text;
using System.Windows.Media;
using ElyProxy.Models;
using ElyProxy.Services;

namespace ElyProxy.ViewModels;

public partial class MainViewModel
{
    private HashSet<string> _favoriteServers = new(StringComparer.Ordinal);
    private List<string> _recentServers = new();

    // Only opaque hashes are persisted, never credentials or subscription URLs.
    public static string LibraryKey(VlessNode node) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(Newtonsoft.Json.JsonConvert.SerializeObject(new
        { node.Protocol, node.UUID, node.Address, node.Port, node.Network, node.Security, node.SNI, node.PublicKey, node.ShortId, node.Username, node.Password, node.Method,
          Extra = node.Extra.OrderBy(entry => entry.Key, StringComparer.Ordinal).ToArray() }))));

    public bool IsFavorite(VlessNode node) => _favoriteServers.Contains(LibraryKey(node));
    public int RecentIndex(VlessNode node) => _recentServers.IndexOf(LibraryKey(node));
    public void ToggleFavorite(VlessNode node)
    {
        var key = LibraryKey(node);
        if (!_favoriteServers.Remove(key)) _favoriteServers.Add(key);
        OnPropertyChanged("ServerLibrary");
        _ = SaveSettingsAsync();
    }

    private void RememberRecentServer(VlessNode node)
    {
        var key = LibraryKey(node);
        _recentServers.Remove(key);
        _recentServers.Insert(0, key);
        if (_recentServers.Count > 30) _recentServers.RemoveRange(30, _recentServers.Count - 30);
        OnPropertyChanged("ServerLibrary");
    }

    public DateTime? SessionStartedAt { get; private set; }

    public VlessNode? ConnectionPanelNode => IsConnected ? _connectedNode : SelectedNode ?? _connectedNode;
    public static string ServerDisplayName(string name)
    {
        if (name.Length >= 4 && char.IsSurrogatePair(name, 0) && char.IsSurrogatePair(name, 2)
            && char.ConvertToUtf32(name, 0) is >= 0x1F1E6 and <= 0x1F1FF
            && char.ConvertToUtf32(name, 2) is >= 0x1F1E6 and <= 0x1F1FF)
            return name[4..].TrimStart();
        return name;
    }
    public string ConnectionPanelName => ConnectionPanelNode is { } node ? ServerDisplayName(node.DisplayName) : "Выберите сервер";
    public ImageSource? ConnectionPanelFlag => FlagCatalog.GetFlagImage(FlagCatalog.ParseCountryCode(ConnectionPanelNode?.DisplayName));
    public string ConnectionPanelSource => SubscriptionName(ConnectionPanelNode);
    public string SubscriptionName(VlessNode? node) => node == null ? "Подписка или сервер вручную" :
        Subscriptions.FirstOrDefault(s => s.Nodes.Any(n => ReferenceEquals(n, node) || LibraryKey(n) == LibraryKey(node)))?.Name ?? "Вручную";

    internal void NotifyConnectionPanel()
    {
        OnPropertyChanged(nameof(ConnectionPanelNode));
        OnPropertyChanged(nameof(ConnectionPanelName));
        OnPropertyChanged(nameof(ConnectionPanelFlag));
        OnPropertyChanged(nameof(ConnectionPanelSource));
    }
}
