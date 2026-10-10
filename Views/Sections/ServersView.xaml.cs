using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using ElyProxy.Models;
using ElyProxy.Services;
using ElyProxy.ViewModels;
using ElyProxy.Views.Sheets;

namespace ElyProxy.Views.Sections;

public partial class ServersView : UserControl
{
    private static readonly IComparer NameComparer = new NodeNameComparer();
    private static readonly IComparer LatencyComparer = new NodeLatencyComparer();

    private bool _sortReady;

    private string _protocolFilter = string.Empty;

    public ServersView()
    {
        InitializeComponent();
        EmptyServers.ActionCommand = new RelayCommand(() => _ = AddSubscriptionAsync());
        Loaded += (_, _) => _sortReady = true;
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    private async void AddServer_Click(object sender, RoutedEventArgs e) => await AddServerAsync();

    private async void RowShare_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel || viewModel.SelectedNode is not { } node)
        {
            return;
        }

        var sheet = new ShareServerSheet(node);
        await DialogService.Current.ShowCustomAsync("Поделиться сервером", sheet, "Готово");
    }

    private async Task AddServerAsync()
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        var sheet = new AddServerSheet();
        if (!await DialogService.Current.ShowCustomAsync("Добавить сервер", sheet, "Добавить"))
        {
            return;
        }

        if (!sheet.IsValid)
        {
            return;
        }

        viewModel.ManualServerUri = sheet.Uri.Trim();
        viewModel.AddManualServerCommand.Execute(null);
    }

    private async Task AddSubscriptionAsync()
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        var sheet = new AddSubscriptionSheet();
        if (!await DialogService.Current.ShowCustomAsync("Новая подписка", sheet, "Добавить") || !sheet.IsValid)
        {
            return;
        }

        viewModel.NewSubName = sheet.SubscriptionName.Trim();
        viewModel.NewSubUrl = sheet.SubscriptionUrl.Trim();
        viewModel.AddSubscriptionCommand.Execute(null);
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    private void ProtocolBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _protocolFilter = ProtocolBox.SelectedIndex switch
        {
            1 => "vless",
            2 => "vmess",
            3 => "trojan",
            4 => "shadowsocks",
            5 => "hysteria2",
            6 => "tuic",
            _ => string.Empty
        };
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        var view = CollectionViewSource.GetDefaultView(viewModel.AllNodes);
        var query = SearchBox.Text.Trim();
        if (string.IsNullOrEmpty(query) && string.IsNullOrEmpty(_protocolFilter))
        {
            view.Filter = null;
            return;
        }

        view.Filter = item => item is VlessNode node && Matches(node, query) && MatchesProtocol(node);
    }

    private bool MatchesProtocol(VlessNode node)
    {
        if (string.IsNullOrEmpty(_protocolFilter))
        {
            return true;
        }

        var protocol = (node.Protocol ?? "vless").ToLowerInvariant();
        return _protocolFilter switch
        {
            "shadowsocks" => protocol is "shadowsocks" or "ss",
            "hysteria2" => protocol is "hysteria2" or "hy2",
            _ => protocol == _protocolFilter
        };
    }

    private void SortBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_sortReady || ViewModel is not { } viewModel)
        {
            return;
        }

        if (CollectionViewSource.GetDefaultView(viewModel.AllNodes) is not ListCollectionView view)
        {
            return;
        }

        view.SortDescriptions.Clear();
        view.CustomSort = SortBox.SelectedIndex switch
        {
            1 => NameComparer,
            2 => LatencyComparer,
            _ => null
        };
    }

    private void ServerList_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListView list
            && ItemsControl.ContainerFromElement(list, (DependencyObject)e.OriginalSource) is ListViewItem item)
        {
            item.IsSelected = true;
        }
    }

    private static bool Matches(VlessNode node, string query) =>
        node.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)
        || node.Address.Contains(query, StringComparison.OrdinalIgnoreCase)
        || node.TransportDisplay.Contains(query, StringComparison.OrdinalIgnoreCase);

    private sealed class NodeNameComparer : IComparer
    {
        public int Compare(object? x, object? y) =>
            string.Compare(
                (x as VlessNode)?.DisplayName,
                (y as VlessNode)?.DisplayName,
                StringComparison.CurrentCultureIgnoreCase);
    }

    private sealed class NodeLatencyComparer : IComparer
    {
        public int Compare(object? x, object? y) =>
            ((x as VlessNode)?.Latency ?? int.MaxValue).CompareTo((y as VlessNode)?.Latency ?? int.MaxValue);
    }
}
