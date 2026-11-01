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
    private readonly System.Windows.Threading.DispatcherTimer _refreshTimer;
    private MainViewModel? _attached;
    private readonly List<Subscription> _observedSubscriptions = new();
    private ListCollectionView? _rowsView;
    private string _scope = "all";
    private string _protocolFilter = "";
    private bool _rebuilding;
    private bool _restoreScroll;

    public ServersView()
    {
        InitializeComponent();
        _refreshTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(90) };
        _refreshTimer.Tick += (_, _) => { _refreshTimer.Stop(); RebuildLibrary(); };
        EmptyServers.ActionCommand = new RelayCommand(() => _ = AddSubscriptionAsync());
        NoSearchResults.ActionCommand = new RelayCommand(ResetFilters);
        InputBindings.Add(new KeyBinding(new RelayCommand(() => SearchBox.Focus()), Key.F, ModifierKeys.Control));
        Loaded += (_, _) => Attach();
        Unloaded += (_, _) => Detach();
        DataContextChanged += (_, _) => { if (IsLoaded) Attach(); };
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    private void Attach()
    {
        Detach();
        if (ViewModel is not { } vm) return;
        _attached = vm;
        vm.AllNodes.CollectionChanged += CollectionChanged;
        vm.Subscriptions.CollectionChanged += CollectionChanged;
        vm.PropertyChanged += VmChanged;
        RebuildLibrary();
    }

    private void Detach()
    {
        _refreshTimer.Stop();
        if (_attached is { } vm)
        {
            vm.AllNodes.CollectionChanged -= CollectionChanged;
            vm.Subscriptions.CollectionChanged -= CollectionChanged;
            vm.PropertyChanged -= VmChanged;
        }
        foreach (var subscription in _observedSubscriptions) subscription.PropertyChanged -= SubscriptionChanged;
        _observedSubscriptions.Clear();
        _attached = null;
    }

    private void CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => ScheduleRefresh();
    private void SubscriptionChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => ScheduleRefresh();
    private void VmChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_rebuilding && e.PropertyName == "ActiveServerNode") return;
        if (e.PropertyName is "ServerLibrary" or "ThemeMode") _restoreScroll = true;
        if (e.PropertyName is "ServerLibrary" or "ConnectedNodeName" or "ThemeMode" or "IsConnected" or "IsLoading" or "ActiveServerNode") { _restoreScroll = true; ScheduleRefresh(); }
        if (e.PropertyName == "SelectedNode" && !_rebuilding && _rowsView != null)
            ServerList.SelectedItem = _rowsView.Cast<ServerRow>().FirstOrDefault(r => ReferenceEquals(r.Node, ViewModel?.SelectedNode));
    }

    private void ScheduleRefresh()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(ScheduleRefresh); return; }
        if (_attached == null) return;
        _refreshTimer.Stop();
        _refreshTimer.Start();
    }

    private void RebuildLibrary()
    {
        if (_attached is not { } vm) return;
        var offset = _restoreScroll ? FindScrollViewer(ServerList)?.VerticalOffset ?? 0 : 0;
        _restoreScroll = false;
        _rebuilding = true;
        try
        {
            foreach (var subscription in _observedSubscriptions) subscription.PropertyChanged -= SubscriptionChanged;
            _observedSubscriptions.Clear();
            var sources = new Dictionary<VlessNode, Subscription>();
            foreach (var subscription in vm.Subscriptions)
            {
                _observedSubscriptions.Add(subscription);
                subscription.PropertyChanged += SubscriptionChanged;
                foreach (var node in subscription.Nodes) sources.TryAdd(node, subscription);
            }
            var selectedSource = (SubscriptionTabs.SelectedItem as SourceOption)?.Id ?? "";
            var options = new List<SourceOption> { new("", "Все подписки", vm.AllNodes.Count) };
            options.AddRange(vm.Subscriptions.Select(s => new SourceOption(s.Id, s.Name, vm.AllNodes.Count(n => sources.GetValueOrDefault(n)?.Id == s.Id))));
            var manual = vm.AllNodes.Count(n => !sources.ContainsKey(n));
            if (manual > 0) options.Add(new("manual", "Вручную", manual));
            SubscriptionTabs.ItemsSource = options;
            SourcePicker.ItemsSource = options;
            SourcePicker.Visibility = options.Count > 5 ? Visibility.Visible : Visibility.Collapsed;
            SubscriptionTabs.SelectedItem = options.FirstOrDefault(o => o.Id == selectedSource) ?? options[0];
            var country = (CountryBox.SelectedItem as CountryOption)?.Code ?? "";
            var rows = vm.AllNodes.Select((n, index) => new ServerRow(n, sources.GetValueOrDefault(n), vm, index)).ToList();
            if (vm.ActiveServerNode is { } active)
            {
                var activeSource = sources.GetValueOrDefault(active) ?? vm.Subscriptions.FirstOrDefault(sub => sub.Nodes.Any(n => ActiveServerIdentity.Matches(active, n)));
                PinnedServerContent.Content = new ServerRow(active, activeSource, vm, -1);
                PinnedServer.Visibility = Visibility.Visible;
            }
            else { PinnedServerContent.Content = null; PinnedServer.Visibility = Visibility.Collapsed; }
            var countries = new List<CountryOption> { new("", "Все страны") };
            countries.AddRange(rows.Select(r => r.CountryCode).Distinct().Where(c => c.Length > 0)
                .Select(c => new CountryOption(c, CountryName(c))).OrderBy(c => c.Name));
            CountryBox.ItemsSource = countries;
            CountryBox.SelectedItem = countries.FirstOrDefault(c => c.Code == country) ?? countries[0];
            if (vm.SelectedNode is { } selected && !rows.Any(row => ReferenceEquals(row.Node, selected)))
                vm.SelectedNode = rows.FirstOrDefault(row => MainViewModel.LibraryKey(row.Node) == MainViewModel.LibraryKey(selected))?.Node ?? rows.FirstOrDefault()?.Node;
            _rowsView = new ListCollectionView(rows);
            ServerList.ItemsSource = _rowsView;
            LibrarySummary.Text = $"{vm.AllNodes.Count} серверов · {vm.Subscriptions.Count} подписок";
            vm.NotifyConnectionPanel();
        }
        finally { _rebuilding = false; }
        ApplyFilter();
        if (offset > 0) Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() => FindScrollViewer(ServerList)?.ScrollToVerticalOffset(offset)));
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject parent)
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is ScrollViewer viewer) return viewer;
            if (FindScrollViewer(child) is { } nested) return nested;
        }
        return null;
    }

    private static string CountryName(string code)
    {
        try { return new System.Globalization.RegionInfo(code).DisplayName; }
        catch (ArgumentException) { return code == "?" ? "Без страны" : code; }
    }

    public void FocusSearch() => SearchBox.Focus();

    private async void AddServer_Click(object sender, RoutedEventArgs e) => await AddServerAsync();

    private async void AddSubscription_Click(object sender, RoutedEventArgs e) => await AddSubscriptionAsync();

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
        viewModel.NewSubUserAgent = sheet.UserAgent;
        viewModel.NewSubUpdateIntervalMinutes = sheet.UpdateIntervalMinutes;
        viewModel.NewSubServerFilter = sheet.ServerFilter;
        viewModel.NewSubSortMode = sheet.SortMode;
        viewModel.NewSubHwid = sheet.Hwid;
        viewModel.NewSubCustomHeadersText = sheet.CustomHeadersText;
        viewModel.AddSubscriptionCommand.Execute(null);
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => ScheduleRefresh();
    private void CountryBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => ApplyFilter();
    private void SubscriptionTabs_SelectionChanged(object sender, SelectionChangedEventArgs e) => ApplyFilter();
    private void SortBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => ApplyFilter();
    private void ProtocolBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _protocolFilter = (ProtocolBox.SelectedItem as ComboBoxItem)?.Content?.ToString()?.ToLowerInvariant() ?? "";
        if (ProtocolBox.SelectedIndex == 0) _protocolFilter = "";
        ApplyFilter();
    }

    private void ResetFilters()
    {
        SearchBox.Clear();
        ProtocolBox.SelectedIndex = 0;
        CountryBox.SelectedIndex = 0;
        SubscriptionTabs.SelectedIndex = 0;
        _scope = "all";
        ApplyFilter();
    }

    private void Scope_Click(object sender, RoutedEventArgs e)
    {
        _scope = (sender as Button)?.Tag?.ToString() ?? "all";
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        if (_rebuilding || _rowsView == null || ViewModel is not { } vm || SearchBox == null) return;
        var query = SearchBox.Text.Trim();
        var source = (SubscriptionTabs.SelectedItem as SourceOption)?.Id ?? "";
        var country = (CountryBox.SelectedItem as CountryOption)?.Code ?? "";
        using (_rowsView.DeferRefresh())
        {
            _rowsView.Filter = item => item is ServerRow row
                && !row.IsActive
                && (source.Length == 0 || row.SourceId == source)
                && (country.Length == 0 || row.CountryCode == country)
                && (_scope != "favorites" || vm.IsFavorite(row.Node))
                && (_scope != "recent" || vm.RecentIndex(row.Node) >= 0)
                && (_protocolFilter.Length == 0 || NormalizeProtocol(row.Node.Protocol) == _protocolFilter)
                && (query.Length == 0 || $"{row.Node.DisplayName} {row.Node.Address} {row.Node.ProtocolDisplay} {row.Node.TransportDisplay} {row.SubscriptionName} {CountryName(row.CountryCode)}".Contains(query, StringComparison.OrdinalIgnoreCase));
            _rowsView.CustomSort = new RowComparer(SortBox.SelectedIndex, _scope == "recent", vm);
        }
        ServerList.SelectedItem = _rowsView.Cast<ServerRow>().FirstOrDefault(r => ReferenceEquals(r.Node, vm.SelectedNode));
        var count = _rowsView.Count;
        ResultSummary.Text = $"Показано {count} из {vm.AllNodes.Count} серверов";
        EmptyServers.Visibility = vm.AllNodes.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        NoSearchResults.Visibility = vm.AllNodes.Count > 0 && count == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var button in new[] { AllTab, FavoritesTab, RecentTab })
        {
            button.SetResourceReference(Control.ForegroundProperty, button.Tag?.ToString() == _scope ? "AccentBrush" : "FgSecBrush");
            button.FontWeight = button.Tag?.ToString() == _scope ? FontWeights.SemiBold : FontWeights.Normal;
        }
    }

    private static string NormalizeProtocol(string? protocol) => protocol?.ToLowerInvariant() switch { "ss" => "shadowsocks", "hy2" => "hysteria2", null => "vless", var p => p };

    private void ServerList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_rebuilding && ServerList.SelectedItem is ServerRow row && ViewModel is { } vm) vm.SelectedNode = row.Node;
    }

    private void Favorite_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ServerRow row) ViewModel?.ToggleFavorite(row.Node);
        e.Handled = true;
    }

    private void FavoriteMenu_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel?.SelectedNode is { } node) ViewModel.ToggleFavorite(node);
    }

    private void AddMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { ContextMenu: { } menu } button)
        {
            menu.PlacementTarget = button;
            menu.IsOpen = true;
        }
    }

    private void RowMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: ServerRow row } button || ViewModel is not { } vm) return;
        vm.SelectedNode = row.Node;
        var parent = System.Windows.Media.VisualTreeHelper.GetParent(button) as FrameworkElement;
        if (parent?.ContextMenu is not { } menu) return;
        menu.PlacementTarget = parent;
        menu.IsOpen = true;
        e.Handled = true;
    }

    private void ServerList_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (ItemsControl.ContainerFromElement(ServerList, (DependencyObject)e.OriginalSource) is ListViewItem item)
            item.IsSelected = true;
    }

    private void ServerList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ItemsControl.ContainerFromElement(ServerList, (DependencyObject)e.OriginalSource) is not ListViewItem) return;
        for (DependencyObject? current = e.OriginalSource as DependencyObject; current != null && current is not ListViewItem;
             current = current is FrameworkContentElement content ? content.Parent : System.Windows.Media.VisualTreeHelper.GetParent(current))
            if (current is System.Windows.Controls.Primitives.ButtonBase) return;
        ConnectSelected();
    }

    private void ServerList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || e.OriginalSource is Button) return;
        ConnectSelected();
        e.Handled = true;
    }

    private void RowConnect_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not Button { DataContext: ServerRow row } || ViewModel is not { } vm || vm.IsLoading) return;
        var isActive = vm.IsActiveServer(row.Node);
        vm.SelectedNode = row.Node;
        var command = isActive ? vm.DisconnectCommand : vm.ConnectCommand;
        if (command.CanExecute(null)) command.Execute(null);
    }

    private void ConnectSelected()
    {
        if (ViewModel is { } vm && vm.ConnectCommand.CanExecute(null)) vm.ConnectCommand.Execute(null);
    }

    private sealed record SourceOption(string Id, string Name, int Count) { public override string ToString() => $"{Name} · {Count}"; }
    private sealed record CountryOption(string Code, string Name) { public override string ToString() => Name; }
    private sealed class ServerRow
    {
        public VlessNode Node { get; }
        public int Order { get; }
        public string SubscriptionName { get; }
        public string SourceId { get; }
        public string CountryCode { get; }
        public string Address => $"{Node.Address} : {Node.Port}";
        public string FavoriteGlyph { get; }
        public bool IsActive { get; }
        public bool IsWorking { get; }
        public string ActionGlyph => IsActive ? "■" : "▶";
        public string ActionLabel => $"{(IsWorking ? "Подключение / отключение" : IsActive ? "Отключиться" : "Подключиться")} · {Name}";
        public string LatencyBand => Node.ProbeRating?.Band ??
            (!string.IsNullOrEmpty(Node.ProbeVerdict) ? "Unknown" :
                Node.Latency is < 0 || Node.PingDetails.StartsWith("0/", StringComparison.Ordinal) ? "High" :
                ViewModelPingIsHttp ? ProbeRatingPolicy.HttpsLatencyBand(Node.Latency) :
                Node.Latency is null ? "Unknown" : Node.Latency <= 100 ? "Good" : Node.Latency <= 200 ? "Medium" : "High");
        private bool ViewModelPingIsHttp { get; }
        public string QualityText => Node.ProbeRating?.Title ?? Node.LatencyDisplay;
        public string QualityMetrics => Node.ProbeRating?.Metrics ?? "";
        public string LatencyHint => !string.IsNullOrWhiteSpace(Node.ProbeReport) ? Node.ProbeReport : ViewModelPingIsHttp ? "Полный HTTPS-запрос: SOCKS, TLS, ответ сервера и часть тела. Это не TCP ping. Ориентир: до 800 мс — быстрый, выше — медленнее; недоступность определяется ошибкой, а не миллисекундами." : $"{Node.LatencyDisplay}\nДо 100 мс — низкая задержка; 101–200 мс — средняя; выше 200 мс — высокая.";
        public bool IsFavorite => FavoriteGlyph == "★";
        public string Name => MainViewModel.ServerDisplayName(Node.DisplayName);
        public ServerRow(VlessNode node, Subscription? subscription, MainViewModel vm, int order)
        {
            ViewModelPingIsHttp = vm.PingMode != "Tcp";
            IsWorking = vm.IsLoading && ReferenceEquals(vm.SelectedNode, node);
            Order = order;
            Node = node;
            IsActive = vm.IsActiveServer(node);
            SubscriptionName = subscription?.Name ?? (IsActive ? vm.ActiveServerSubscriptionName : "Вручную");
            SourceId = subscription?.Id ?? "manual";
            var code = FlagCatalog.Normalize(FlagCatalog.ParseCountryCode(node.DisplayName));
            CountryCode = code != null && FlagCatalog.GetFlagImage(code) != null ? code : "?";
            FavoriteGlyph = vm.IsFavorite(node) ? "★" : "☆";
        }
    }

    private sealed class RowComparer(int sort, bool recent, MainViewModel vm) : IComparer
    {
        private static double? Speed(VlessNode node) => node.ProbeSpeedMbps;
        private static int CompareQuality(ServerRow left, ServerRow right)
        {
            int quality = (right.Node.ProbeRating?.Score ?? -1).CompareTo(left.Node.ProbeRating?.Score ?? -1);
            if (quality != 0) return quality;
            int speed = Nullable.Compare(right.Node.ProbeSpeedMbps, left.Node.ProbeSpeedMbps);
            return speed != 0 ? speed : (left.Node.Latency ?? int.MaxValue).CompareTo(right.Node.Latency ?? int.MaxValue);
        }
        public int Compare(object? x, object? y)
        {
            if (x is not ServerRow left || y is not ServerRow right) return 0;
            if (recent) return vm.RecentIndex(left.Node).CompareTo(vm.RecentIndex(right.Node));
            return sort switch
            {
                1 => StringComparer.CurrentCultureIgnoreCase.Compare(left.Node.DisplayName, right.Node.DisplayName),
                3 => CompareQuality(left, right),
                4 => Nullable.Compare(right.Node.ProbeRating == null ? null : Speed(right.Node), left.Node.ProbeRating == null ? null : Speed(left.Node)),
                2 => (left.Node.Latency is >= 0 ? left.Node.Latency.Value : int.MaxValue).CompareTo(right.Node.Latency is >= 0 ? right.Node.Latency.Value : int.MaxValue),
                _ => left.Order.CompareTo(right.Order)
            };
        }
    }
}
