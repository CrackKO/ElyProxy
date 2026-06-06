using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Principal;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using ElyProxy.Core;
using ElyProxy.Models;
using ElyProxy.Services;

namespace ElyProxy.ViewModels;

public class MainViewModel : ViewModelBase, IDisposable
{
    private const string ChromeExtensionUrl = "https://chromewebstore.google.com/detail/proxy-switchyomega-3-zero/pfnededegaaopdmhkdmcofjmoldfiped?pli=1";
    private const string FirefoxExtensionUrl = "https://addons.mozilla.org/ru/firefox/addon/zeroomega/";
    private const string OmegaRulesFileName = "OmegaRules_auto_switch.sorl";
    private const string DefaultProxyPingUrl = "https://youtube.com";
    private const string LegacyProxyPingUrl = "http://cp.cloudflare.com/generate_204";
    private const string PingModeTcp = "Tcp";
    private const string PingModeProxyGet = "ProxyGet";
    private const string PingModeElyTurbo = "ElyTurbo";
    private const string PingModeElyHard = "ElyHard";
    private const int DefaultProxyPingConcurrency = 10;
    private const int MinProxyPingConcurrency = 1;
    private const int MaxProxyPingConcurrency = 32;
    private const int DefaultElyTunMtu = 1500;
    private const int MaxAutoReconnectAttempts = 3;
    private static readonly Version MinElyTunXrayVersion = new(26, 4, 0);
    private static readonly string[] DefaultElyTunDnsServers = ["1.1.1.1", "8.8.8.8"];
    private static readonly TimeSpan TcpPingTimeout = TimeSpan.FromMilliseconds(2500);
    private static readonly TimeSpan ProxyPingHttpTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan ProxyPingStartupTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan AutoReconnectAttemptWindow = TimeSpan.FromMinutes(2);

    private readonly SubscriptionService _subscriptionService;
    private readonly ParserService _parserService;
    private readonly StorageService _storageService;
    private readonly ImportExportService _importExportService;
    private readonly AutoStartService _autoStartService;
    private readonly PacServerService _pacServerService;
    private readonly WindowsProxyService _windowsProxyService;
    private readonly ElyHardService _elyHardService;
    private readonly XrayManager _xrayManager;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _subscriptionUpdateTimer;
    private readonly List<VlessNode> _manualNodes = new();

    private VlessNode? _selectedNode;
    private Subscription? _selectedSubscription;
    private ProxyProfile? _selectedProfile;
    private VlessNode? _selectedProfileNode;
    private VlessNode? _connectedNode;
    private string _newSubName = string.Empty;
    private string _newSubUrl = string.Empty;
    private string _manualServerUri = string.Empty;
    private string _newProfileName = string.Empty;
    private string _statusText = "Отключено";
    private string _connectionInfo = string.Empty;
    private string _connectedNodeName = string.Empty;
    private string _logText = string.Empty;
    private string _pingSummary = string.Empty;
    private bool _isConnected;
    private bool _isLoading;
    private bool _autoStartWithWindows;
    private bool _autoConnect;
    private bool _autoReconnect;
    private bool _showLogs = true;
    private bool _systemProxyEnabled;
    private bool _elyTunEnabled;
    private bool _elyTunIgnoreOtherTunAdapters;
    private bool _isLoadingSettings;
    private bool _isReconnecting;
    private bool _isDisposed;
    private int _suppressedDisconnectNotifications;
    private int _autoReconnectAttempts;
    private int _subscriptionUpdateIntervalMinutes;
    private int _pacPort = 18080;
    private int _proxyPingConcurrency = DefaultProxyPingConcurrency;
    private string _systemProxyRulesText = string.Empty;
    private string _pingMode = PingModeTcp;
    private string _proxyPingUrl = DefaultProxyPingUrl;
    private string? _lastConnectedNodeId;
    private string? _previousAutoConfigUrl;
    private int _socksPort = 1080;

    private static readonly ElyTurboTarget[] ElyTurboTargets =
    [
        new("Discord", "https://discord.com/api/v9/experiments"),
        new("Telegram", "https://telegram.org"),
        new("YouTube", "https://www.youtube.com/generate_204"),
        new("Spotify", "https://open.spotify.com"),
        new("GitHub", "https://github.com"),
        new("Gmail", "https://mail.google.com/mail/generate_204")
    ];

    public MainViewModel()
    {
        _dispatcher = Application.Current.Dispatcher;
        _subscriptionService = new SubscriptionService();
        _parserService = new ParserService();
        _storageService = new StorageService();
        _importExportService = new ImportExportService();
        _autoStartService = new AutoStartService();
        _pacServerService = new PacServerService();
        _windowsProxyService = new WindowsProxyService();
        _elyHardService = new ElyHardService();
        _xrayManager = new XrayManager();

        _xrayManager.LogReceived += OnLogReceived;
        _xrayManager.StatusChanged += OnStatusChanged;

        _subscriptionUpdateTimer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher);
        _subscriptionUpdateTimer.Tick += OnSubscriptionUpdateTimerTick;

        AllNodes = new ObservableCollection<VlessNode>();
        Subscriptions = new ObservableCollection<Subscription>();
        Profiles = new ObservableCollection<ProxyProfile>();
        ProfileNodes = new ObservableCollection<VlessNode>();

        AddSubscriptionCommand = new AsyncRelayCommand(AddSubscriptionAsync, () => !IsLoading && !string.IsNullOrWhiteSpace(NewSubUrl));
        RemoveSubscriptionCommand = new RelayCommand(RemoveSubscription, () => SelectedSubscription != null);
        UpdateSubscriptionCommand = new AsyncRelayCommand(UpdateSelectedSubscriptionAsync, () => SelectedSubscription != null && !IsLoading);
        UpdateAllSubscriptionsCommand = new AsyncRelayCommand(UpdateAllSubscriptionsAsync, () => Subscriptions.Count > 0 && !IsLoading);

        ConnectCommand = new AsyncRelayCommand(ConnectAsync, () => SelectedNode != null && !IsLoading);
        DisconnectCommand = new AsyncRelayCommand(DisconnectAsync, () => IsConnected && !IsLoading);
        ConnectFromProfileCommand = new AsyncRelayCommand(ConnectFromProfileAsync, () => SelectedProfileNode != null && !IsLoading);
        PingAllCommand = new AsyncRelayCommand(PingAllAsync, () => AllNodes.Count > 0 && !IsLoading);

        AddManualServerCommand = new RelayCommand(AddManualServer, () => !string.IsNullOrWhiteSpace(ManualServerUri));
        AddToProfileCommand = new RelayCommand(AddToProfile, () => SelectedNode != null && SelectedProfile != null);

        CreateProfileCommand = new RelayCommand(CreateProfile, () => !string.IsNullOrWhiteSpace(NewProfileName));
        DeleteProfileCommand = new RelayCommand(DeleteProfile, () => SelectedProfile != null);
        RemoveFromProfileCommand = new RelayCommand(RemoveFromProfile, () => SelectedProfileNode != null && SelectedProfile != null);
        ClearProfileCommand = new RelayCommand(ClearProfile, () => SelectedProfile != null && SelectedProfile.Nodes.Count > 0);
        ExportProfileCommand = new AsyncRelayCommand(ExportProfileAsync, () => SelectedProfile != null);
        ImportProfileCommand = new AsyncRelayCommand(ImportProfileAsync);

        ClearLogCommand = new RelayCommand(() => LogText = string.Empty);
        CopyProxyCommand = new RelayCommand(CopyProxy, () => IsConnected);
        OpenTelegramCommand = new RelayCommand(() => OpenUrl("https://t.me/ProxyCheckXBot"));
        OpenDiscordCommand = new RelayCommand(() => OpenUrl("https://discord.gg/sxjV3S7J2k"));
        OpenChromeExtensionCommand = new RelayCommand(() => OpenUrl(ChromeExtensionUrl));
        OpenFirefoxExtensionCommand = new RelayCommand(() => OpenUrl(FirefoxExtensionUrl));
        ExportOmegaRulesCommand = new AsyncRelayCommand(ExportOmegaRulesAsync);
        ResetSystemProxyRulesCommand = new RelayCommand(ResetSystemProxyRules);

        _ = LoadAllDataAsync();
    }

    #region Collections

    public ObservableCollection<VlessNode> AllNodes { get; }
    public ObservableCollection<Subscription> Subscriptions { get; }
    public ObservableCollection<ProxyProfile> Profiles { get; }
    public ObservableCollection<VlessNode> ProfileNodes { get; }
    public IReadOnlyList<KeyValuePair<int, string>> SubscriptionUpdateIntervalOptions { get; } =
    [
        new(0, "Отключено"),
        new(30, "Каждые 30 минут"),
        new(60, "Каждый час"),
        new(360, "Каждые 6 часов"),
        new(1440, "Раз в день")
    ];

    public IReadOnlyList<KeyValuePair<string, string>> PingModeOptions { get; } =
    [
        new(PingModeTcp, "TCP"),
        new(PingModeProxyGet, "Proxy GET"),
        new(PingModeElyTurbo, "ElyTurbo"),
        new(PingModeElyHard, "ElyHard")
    ];

    #endregion

    #region Properties

    public VlessNode? SelectedNode
    {
        get => _selectedNode;
        set { if (SetProperty(ref _selectedNode, value)) CommandManager.InvalidateRequerySuggested(); }
    }

    public Subscription? SelectedSubscription
    {
        get => _selectedSubscription;
        set { if (SetProperty(ref _selectedSubscription, value)) CommandManager.InvalidateRequerySuggested(); }
    }

    public ProxyProfile? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (SetProperty(ref _selectedProfile, value))
            {
                RefreshProfileNodes();
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public VlessNode? SelectedProfileNode
    {
        get => _selectedProfileNode;
        set { if (SetProperty(ref _selectedProfileNode, value)) CommandManager.InvalidateRequerySuggested(); }
    }

    public string NewSubName
    {
        get => _newSubName;
        set { if (SetProperty(ref _newSubName, value)) CommandManager.InvalidateRequerySuggested(); }
    }

    public string NewSubUrl
    {
        get => _newSubUrl;
        set { if (SetProperty(ref _newSubUrl, value)) CommandManager.InvalidateRequerySuggested(); }
    }

    public string ManualServerUri
    {
        get => _manualServerUri;
        set { if (SetProperty(ref _manualServerUri, value)) CommandManager.InvalidateRequerySuggested(); }
    }

    public string NewProfileName
    {
        get => _newProfileName;
        set { if (SetProperty(ref _newProfileName, value)) CommandManager.InvalidateRequerySuggested(); }
    }

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public string ConnectionInfo
    {
        get => _connectionInfo;
        set => SetProperty(ref _connectionInfo, value);
    }

    public string ConnectedNodeName
    {
        get => _connectedNodeName;
        set => SetProperty(ref _connectedNodeName, value);
    }

    public string LogText
    {
        get => _logText;
        set => SetProperty(ref _logText, value);
    }

    public string PingSummary
    {
        get => _pingSummary;
        set
        {
            if (SetProperty(ref _pingSummary, value))
                OnPropertyChanged(nameof(HasPingSummary));
        }
    }

    public bool HasPingSummary => !string.IsNullOrWhiteSpace(PingSummary);

    public bool IsConnected
    {
        get => _isConnected;
        set { if (SetProperty(ref _isConnected, value)) CommandManager.InvalidateRequerySuggested(); }
    }

    public bool IsLoading
    {
        get => _isLoading;
        set { if (SetProperty(ref _isLoading, value)) CommandManager.InvalidateRequerySuggested(); }
    }

    public int SocksPort
    {
        get => _socksPort;
        set
        {
            if (value is < 1 or > 65535)
                return;

            if (!SetProperty(ref _socksPort, value))
                return;

            if (SystemProxyEnabled && IsConnected && !_isLoadingSettings)
                _ = RestartSystemProxyAsync(PacUrl);
            else if (!_isLoadingSettings)
                _ = SaveSettingsAsync();
        }
    }

    public bool AutoStartWithWindows
    {
        get => _autoStartWithWindows;
        set
        {
            if (!SetProperty(ref _autoStartWithWindows, value))
                return;

            if (_isLoadingSettings)
                return;

            ApplyAutoStartSetting(value);
            _ = SaveSettingsAsync();
        }
    }

    public bool AutoConnect
    {
        get => _autoConnect;
        set
        {
            if (SetProperty(ref _autoConnect, value) && !_isLoadingSettings)
                _ = SaveSettingsAsync();
        }
    }

    public bool AutoReconnect
    {
        get => _autoReconnect;
        set
        {
            if (SetProperty(ref _autoReconnect, value) && !_isLoadingSettings)
                _ = SaveSettingsAsync();
        }
    }

    public bool ShowLogs
    {
        get => _showLogs;
        set
        {
            if (SetProperty(ref _showLogs, value) && !_isLoadingSettings)
                _ = SaveSettingsAsync();
        }
    }

    public string PingMode
    {
        get => _pingMode;
        set
        {
            var normalized = NormalizePingMode(value);
            if (!SetProperty(ref _pingMode, normalized))
                return;

            OnPropertyChanged(nameof(IsProxyGetPingMode));
            OnPropertyChanged(nameof(IsProxyBasedPingMode));

            if (!_isLoadingSettings)
                _ = SaveSettingsAsync();
        }
    }

    public bool IsProxyGetPingMode => PingMode == PingModeProxyGet;
    public bool IsProxyBasedPingMode => PingMode is PingModeProxyGet or PingModeElyTurbo or PingModeElyHard;

    public string ProxyPingUrl
    {
        get => _proxyPingUrl;
        set
        {
            var normalized = NormalizeProxyPingUrl(value);
            if (SetProperty(ref _proxyPingUrl, normalized) && !_isLoadingSettings)
                _ = SaveSettingsAsync();
        }
    }

    public int ProxyPingConcurrency
    {
        get => _proxyPingConcurrency;
        set
        {
            var normalized = Math.Clamp(value, MinProxyPingConcurrency, MaxProxyPingConcurrency);
            if (SetProperty(ref _proxyPingConcurrency, normalized) && !_isLoadingSettings)
                _ = SaveSettingsAsync();
        }
    }

    public int SubscriptionUpdateIntervalMinutes
    {
        get => _subscriptionUpdateIntervalMinutes;
        set
        {
            if (!SetProperty(ref _subscriptionUpdateIntervalMinutes, value))
                return;

            ConfigureSubscriptionUpdateTimer();

            if (!_isLoadingSettings)
                _ = SaveSettingsAsync();
        }
    }

    public bool SystemProxyEnabled
    {
        get => _systemProxyEnabled;
        set
        {
            if (!SetProperty(ref _systemProxyEnabled, value))
                return;

            if (_isLoadingSettings)
                return;

            _ = ApplySystemProxySettingAsync(value);
        }
    }

    public bool ElyTunEnabled
    {
        get => _elyTunEnabled;
        set
        {
            if (!SetProperty(ref _elyTunEnabled, value))
                return;

            if (_isLoadingSettings)
                return;

            _ = ApplyElyTunSettingAsync(value);
        }
    }

    public bool ElyTunIgnoreOtherTunAdapters
    {
        get => _elyTunIgnoreOtherTunAdapters;
        set
        {
            if (SetProperty(ref _elyTunIgnoreOtherTunAdapters, value) && !_isLoadingSettings)
                _ = SaveSettingsAsync();
        }
    }

    public int PacPort
    {
        get => _pacPort;
        set
        {
            var oldPacUrl = PacUrl;
            if (!SetProperty(ref _pacPort, value))
                return;

            OnPropertyChanged(nameof(PacUrl));

            if (SystemProxyEnabled && IsConnected && !_isLoadingSettings)
                _ = RestartSystemProxyAsync(oldPacUrl);
            else if (!_isLoadingSettings)
                _ = SaveSettingsAsync();
        }
    }

    public string PacUrl => $"http://127.0.0.1:{PacPort}/proxy.pac";

    public string SystemProxyRulesText
    {
        get => _systemProxyRulesText;
        set
        {
            if (!SetProperty(ref _systemProxyRulesText, value))
                return;

            if (_isLoadingSettings)
                return;

            if (SystemProxyEnabled && IsConnected)
                _ = StartSystemProxyAsync();

            _ = SaveSettingsAsync();
        }
    }

    #endregion

    #region Commands

    public ICommand AddSubscriptionCommand { get; }
    public ICommand RemoveSubscriptionCommand { get; }
    public ICommand UpdateSubscriptionCommand { get; }
    public ICommand UpdateAllSubscriptionsCommand { get; }
    public ICommand ConnectCommand { get; }
    public ICommand DisconnectCommand { get; }
    public ICommand ConnectFromProfileCommand { get; }
    public ICommand PingAllCommand { get; }
    public ICommand AddManualServerCommand { get; }
    public ICommand AddToProfileCommand { get; }
    public ICommand CreateProfileCommand { get; }
    public ICommand DeleteProfileCommand { get; }
    public ICommand RemoveFromProfileCommand { get; }
    public ICommand ClearProfileCommand { get; }
    public ICommand ExportProfileCommand { get; }
    public ICommand ImportProfileCommand { get; }
    public ICommand ClearLogCommand { get; }
    public ICommand CopyProxyCommand { get; }
    public ICommand OpenTelegramCommand { get; }
    public ICommand OpenDiscordCommand { get; }
    public ICommand OpenChromeExtensionCommand { get; }
    public ICommand OpenFirefoxExtensionCommand { get; }
    public ICommand ExportOmegaRulesCommand { get; }
    public ICommand ResetSystemProxyRulesCommand { get; }

    #endregion

    #region Subscriptions

    private async Task AddSubscriptionAsync()
    {
        if (string.IsNullOrWhiteSpace(NewSubUrl)) return;

        var sub = new Subscription
        {
            Name = string.IsNullOrWhiteSpace(NewSubName) ? $"Подписка {Subscriptions.Count + 1}" : NewSubName.Trim(),
            Url = NewSubUrl.Trim()
        };

        IsLoading = true;
        StatusText = "Загрузка подписки...";

        try
        {
            var content = await _subscriptionService.FetchAsync(sub.Url);
            sub.Nodes = _parserService.ParseSubscriptionContent(content);
            sub.LastUpdated = DateTime.Now;

            _dispatcher.Invoke(() => Subscriptions.Add(sub));

            RefreshAllNodes();
            await _storageService.SaveSubscriptionsAsync(Subscriptions);

            StatusText = $"Подписка добавлена: {sub.Nodes.Count} серверов";
            AppendLog($"[sys] Подписка «{sub.Name}» добавлена, серверов: {sub.Nodes.Count}");

            NewSubName = string.Empty;
            NewSubUrl = string.Empty;
        }
        catch (Exception ex)
        {
            StatusText = "Ошибка загрузки подписки";
            AppendLog($"[err] {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void RemoveSubscription()
    {
        if (SelectedSubscription == null) return;

        var name = SelectedSubscription.Name;
        Subscriptions.Remove(SelectedSubscription);
        SelectedSubscription = null;
        RefreshAllNodes();
        _ = _storageService.SaveSubscriptionsAsync(Subscriptions);
        AppendLog($"[sys] Подписка «{name}» удалена");
    }

    private async Task UpdateSelectedSubscriptionAsync()
    {
        if (SelectedSubscription == null) return;
        await UpdateSubscriptionAsync(SelectedSubscription);
    }

    private async Task UpdateAllSubscriptionsAsync()
    {
        IsLoading = true;
        StatusText = "Обновление подписок...";

        int total = 0;
        foreach (var sub in Subscriptions.ToList())
        {
            try
            {
                var content = await _subscriptionService.FetchAsync(sub.Url);
                sub.Nodes = _parserService.ParseSubscriptionContent(content);
                sub.LastUpdated = DateTime.Now;
                total += sub.Nodes.Count;
            }
            catch (Exception ex)
            {
                AppendLog($"[err] Ошибка «{sub.Name}»: {ex.Message}");
            }
        }

        RefreshAllNodes();
        await _storageService.SaveSubscriptionsAsync(Subscriptions);

        StatusText = $"Обновлено: {total} серверов";
        AppendLog($"[sys] Все подписки обновлены, серверов: {total}");
        IsLoading = false;
    }

    private async Task UpdateSubscriptionAsync(Subscription sub)
    {
        IsLoading = true;
        StatusText = $"Обновление «{sub.Name}»...";

        try
        {
            var content = await _subscriptionService.FetchAsync(sub.Url);
            sub.Nodes = _parserService.ParseSubscriptionContent(content);
            sub.LastUpdated = DateTime.Now;

            RefreshAllNodes();
            await _storageService.SaveSubscriptionsAsync(Subscriptions);

            StatusText = $"«{sub.Name}»: {sub.Nodes.Count} серверов";
            AppendLog($"[sys] Подписка «{sub.Name}» обновлена, серверов: {sub.Nodes.Count}");
        }
        catch (Exception ex)
        {
            StatusText = "Ошибка обновления";
            AppendLog($"[err] {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    #endregion

    #region Connection

    private Task ConnectAsync() => ConnectToNodeAsync(SelectedNode);
    private Task ConnectFromProfileAsync() => ConnectToNodeAsync(SelectedProfileNode);

    private async Task ConnectToNodeAsync(VlessNode? node)
    {
        if (node == null) return;

        IsLoading = true;
        StatusText = "Подключение...";

        try
        {
            var xrayPath = _xrayManager.GetXrayPath();
            if (!File.Exists(xrayPath))
            {
                StatusText = "Xray не найден!";
                AppendLog($"[err] Xray не найден: {xrayPath}");
                MessageBox.Show(
                    $"Xray не найден по пути:\n{xrayPath}\n\n" +
                    "Скачайте Xray-core с:\ngithub.com/XTLS/Xray-core/releases\n\n" +
                    "Поместите xray.exe в папку xray/ рядом с программой.",
                    "ElyProxy — Xray не найден",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (IsConnected)
            {
                DeactivateSystemProxyForDisconnectedServer(silent: true);
                SuppressDisconnectNotifications(3);
                await _xrayManager.StopAsync();
            }

            if (ElyTunEnabled && !EnsureElyTunCanRun(showMessage: true))
                return;

            if (!EnsureSocksPortAvailableForStart())
                return;

            await _xrayManager.StartAsync(node, SocksPort, BuildElyTunOptionsOrNull());

            _connectedNode = node;
            _autoReconnectAttempts = 0;
            ConnectedNodeName = node.DisplayName;
            UpdateConnectionInfo(node);
            RememberConnectedNode(node);

            if (SystemProxyEnabled)
                await TryActivateSystemProxyAsync();

            if (ElyTunEnabled)
                _ = VerifyElyTunConnectivityAsync();
        }
        catch (Exception ex)
        {
            StatusText = "Ошибка подключения";
            AppendLog($"[err] {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task DisconnectAsync()
    {
        IsLoading = true;
        try
        {
            DeactivateSystemProxyForDisconnectedServer(silent: false);
            SuppressDisconnectNotifications();
            await _xrayManager.StopAsync();
            _connectedNode = null;
            ConnectionInfo = string.Empty;
            ConnectedNodeName = string.Empty;
        }
        catch (Exception ex)
        {
            AppendLog($"[err] Ошибка отключения: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    #endregion

    #region Ping

    private Task PingAllAsync()
    {
        return PingMode switch
        {
            PingModeProxyGet => ProxyPingAllAsync(),
            PingModeElyTurbo => ElyTurboAllAsync(),
            PingModeElyHard => ElyHardAllAsync(),
            _ => TcpPingAllAsync()
        };
    }

    private async Task TcpPingAllAsync()
    {
        IsLoading = true;
        StatusText = "TCP-пинг серверов...";
        PingSummary = string.Empty;
        AppendLog("[sys] Запуск TCP-пинга...");

        try
        {
            var nodes = AllNodes.ToList();
            var completed = 0;
            using var semaphore = new SemaphoreSlim(64);
            var tasks = nodes.Select(async node =>
            {
                await semaphore.WaitAsync();
                try
                {
                    node.PingDetails = string.Empty;
                    node.Latency = await MeasureTcpLatencyAsync(node.Address, node.Port);
                    var current = Interlocked.Increment(ref completed);
                    if (current % 5 == 0 || current == nodes.Count)
                    {
                        await _dispatcher.InvokeAsync(() =>
                            StatusText = $"TCP-пинг: {current}/{nodes.Count}");
                    }
                }
                finally
                {
                    semaphore.Release();
                }
            });

            await Task.WhenAll(tasks);

            SortNodesByLatency();

            var reachable = nodes.Count(n => n.Latency.HasValue);
            StatusText = $"TCP-пинг: {reachable}/{nodes.Count} доступно";
            AppendLog($"[sys] TCP-пинг завершён: {reachable}/{nodes.Count} доступно");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task ProxyPingAllAsync()
    {
        IsLoading = true;
        StatusText = "Proxy GET-пинг серверов...";
        PingSummary = string.Empty;

        if (!TryGetProxyPingUri(out var pingUri))
        {
            StatusText = "Некорректный URL пинга";
            AppendLog($"[err] Некорректный URL Proxy GET-пинга: {ProxyPingUrl}");
            IsLoading = false;
            return;
        }

        AppendLog($"[sys] Запуск Proxy GET-пинга: {pingUri}, потоков: {ProxyPingConcurrency}");

        var nodes = AllNodes.ToList();
        var reachable = 0;
        var completed = 0;

        try
        {
            using var semaphore = new SemaphoreSlim(ProxyPingConcurrency);
            var tasks = nodes.Select(async node =>
            {
                await semaphore.WaitAsync();
                try
                {
                    node.PingDetails = string.Empty;
                    node.Latency = await MeasureProxyGetLatencyAsync(node, pingUri);

                    if (node.Latency.HasValue)
                    {
                        Interlocked.Increment(ref reachable);
                        AppendLog($"[ping] Proxy GET {node.DisplayName}: {node.Latency.Value} ms");
                    }
                }
                finally
                {
                    semaphore.Release();
                    var current = Interlocked.Increment(ref completed);
                    if (current % 3 == 0 || current == nodes.Count)
                    {
                        var ok = Volatile.Read(ref reachable);
                        await _dispatcher.InvokeAsync(() =>
                            StatusText = $"Proxy GET: {current}/{nodes.Count}, ок: {ok}");
                    }
                }
            });

            await Task.WhenAll(tasks);

            SortNodesByLatency();

            StatusText = $"Proxy GET: {reachable}/{nodes.Count} доступно";
            AppendLog($"[sys] Proxy GET-пинг завершён: {reachable}/{nodes.Count} доступно");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task ElyTurboAllAsync()
    {
        IsLoading = true;
        StatusText = "ElyTurbo серверов...";
        PingSummary = string.Empty;
        AppendLog($"[sys] Запуск ElyTurbo: сервисов {ElyTurboTargets.Length}, потоков: {ProxyPingConcurrency}");

        var nodes = AllNodes.ToList();
        var results = new ConcurrentBag<ElyTurboNodeResult>();
        var completed = 0;

        try
        {
            using var semaphore = new SemaphoreSlim(ProxyPingConcurrency);
            var tasks = nodes.Select(async node =>
            {
                await semaphore.WaitAsync();
                try
                {
                    var result = await MeasureElyTurboAsync(node);
                    results.Add(result);

                    node.Latency = result.AverageLatencyMs;
                    node.PingDetails = result.SuccessCount > 0
                        ? $"{result.SuccessCount}/{result.TotalCount} · {result.AverageLatencyMs} ms"
                        : $"0/{result.TotalCount}";
                }
                finally
                {
                    semaphore.Release();
                    var current = Interlocked.Increment(ref completed);
                    if (current % 3 == 0 || current == nodes.Count)
                    {
                        var best = results
                            .OrderByDescending(result => result.SuccessCount)
                            .ThenBy(result => result.AverageLatencyMs ?? int.MaxValue)
                            .FirstOrDefault();
                        var bestText = best == null
                            ? string.Empty
                            : $" · лучший: {best.Node.DisplayName} {best.SuccessCount}/{best.TotalCount}";

                        await _dispatcher.InvokeAsync(() =>
                            StatusText = $"ElyTurbo: {current}/{nodes.Count}{bestText}");
                    }
                }
            });

            await Task.WhenAll(tasks);

            var ordered = results
                .OrderByDescending(result => result.SuccessCount)
                .ThenBy(result => result.AverageLatencyMs ?? int.MaxValue)
                .ThenBy(result => result.FastestLatencyMs ?? int.MaxValue)
                .ToList();

            _dispatcher.Invoke(() =>
            {
                AllNodes.Clear();
                foreach (var result in ordered)
                    AllNodes.Add(result.Node);

                SelectedNode = ordered.FirstOrDefault()?.Node;
            });

            var bestResult = ordered.FirstOrDefault();
            if (bestResult == null || bestResult.SuccessCount == 0)
            {
                StatusText = "ElyTurbo: рабочие серверы не найдены";
                PingSummary = "ElyTurbo: рабочие серверы не найдены";
                AppendLog("[sys] ElyTurbo завершён: рабочие серверы не найдены");
                return;
            }

            PingSummary = BuildElyTurboSummary(bestResult);
            StatusText = $"ElyTurbo: лучший {bestResult.SuccessCount}/{bestResult.TotalCount}, {bestResult.AverageLatencyMs} ms";
            AppendLog($"[turbo] {PingSummary}");

            foreach (var result in ordered.Take(5))
            {
                AppendLog($"[turbo] {result.Node.DisplayName}: {result.SuccessCount}/{result.TotalCount}, {FormatNullableLatency(result.AverageLatencyMs)}");
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task ElyHardAllAsync()
    {
        IsLoading = true;
        StatusText = "ElyHard серверов...";
        PingSummary = string.Empty;
        AppendLog($"[sys] Запуск ElyHard: full real session, потоков: {ProxyPingConcurrency}");

        var nodes = AllNodes.ToList();
        var results = new ConcurrentBag<ElyHardService.ElyHardNodeResult>();
        var completed = 0;
        var options = new ElyHardService.ElyHardOptions(
            AttemptsPerTarget: 2,
            StartupTimeout: TimeSpan.FromSeconds(3),
            RequestTimeout: TimeSpan.FromSeconds(6));

        try
        {
            using var semaphore = new SemaphoreSlim(ProxyPingConcurrency);
            var tasks = nodes.Select(async node =>
            {
                await semaphore.WaitAsync();
                try
                {
                    var result = await _elyHardService.CheckNodeAsync(node, options);
                    results.Add(result);

                    node.Latency = result.AverageLatencyMs;
                    node.PingDetails = result.ReachableServices > 0
                        ? $"{result.StableServices}/{result.TotalServices} · {FormatNullableLatency(result.AverageLatencyMs)}"
                        : $"0/{result.TotalServices}";
                }
                finally
                {
                    semaphore.Release();
                    var current = Interlocked.Increment(ref completed);
                    if (current % 2 == 0 || current == nodes.Count)
                    {
                        var best = OrderElyHardResults(results).FirstOrDefault();
                        var bestText = best == null
                            ? string.Empty
                            : $" · лучший: {best.Node.DisplayName} {best.StableServices}/{best.TotalServices}";

                        await _dispatcher.InvokeAsync(() =>
                            StatusText = $"ElyHard: {current}/{nodes.Count}{bestText}");
                    }
                }
            });

            await Task.WhenAll(tasks);

            var ordered = OrderElyHardResults(results).ToList();
            _dispatcher.Invoke(() =>
            {
                AllNodes.Clear();
                foreach (var result in ordered)
                    AllNodes.Add(result.Node);

                SelectedNode = ordered.FirstOrDefault()?.Node;
            });

            var bestResult = ordered.FirstOrDefault();
            if (bestResult == null || bestResult.ReachableServices == 0)
            {
                StatusText = "ElyHard: рабочие серверы не найдены";
                PingSummary = "ElyHard: рабочие серверы не найдены";
                AppendLog("[sys] ElyHard завершён: рабочие серверы не найдены");
                return;
            }

            PingSummary = BuildElyHardSummary(bestResult);
            StatusText = $"ElyHard: лучший {bestResult.StableServices}/{bestResult.TotalServices}, {FormatNullableLatency(bestResult.AverageLatencyMs)}";
            AppendLog($"[hard] {PingSummary}");

            foreach (var result in ordered.Take(5))
            {
                AppendLog($"[hard] {result.Node.DisplayName}: стабильно {result.StableServices}/{result.TotalServices}, попытки {result.TotalSuccessfulAttempts}/{result.TotalAttempts}, среднее {FormatNullableLatency(result.AverageLatencyMs)}");
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    private static async Task<int?> MeasureTcpLatencyAsync(string host, int port)
    {
        try
        {
            using var client = new TcpClient();
            var sw = Stopwatch.StartNew();
            await client.ConnectAsync(host, port).WaitAsync(TcpPingTimeout);
            sw.Stop();
            return (int)sw.ElapsedMilliseconds;
        }
        catch
        {
            return null;
        }
    }

    private async Task<int?> MeasureProxyGetLatencyAsync(VlessNode node, Uri pingUri)
    {
        var probePort = GetFreeTcpPort();
        using var probeXray = new XrayManager();

        try
        {
            await probeXray.StartAsync(node, probePort);

            if (!await WaitForLocalPortAsync(probePort, ProxyPingStartupTimeout))
                return null;

            using var handler = new HttpClientHandler
            {
                Proxy = new WebProxy($"socks5://127.0.0.1:{probePort}"),
                UseProxy = true
            };
            using var http = new HttpClient(handler)
            {
                Timeout = ProxyPingHttpTimeout
            };

            http.DefaultRequestHeaders.UserAgent.ParseAdd("ElyProxy/1.1");

            var sw = Stopwatch.StartNew();
            using var response = await http.GetAsync(pingUri, HttpCompletionOption.ResponseHeadersRead);
            sw.Stop();

            return response.IsSuccessStatusCode || (int)response.StatusCode is >= 300 and < 500
                ? (int)sw.ElapsedMilliseconds
                : null;
        }
        catch
        {
            return null;
        }
        finally
        {
            await probeXray.StopAsync();
        }
    }

    private async Task<ElyTurboNodeResult> MeasureElyTurboAsync(VlessNode node)
    {
        var probePort = GetFreeTcpPort();
        using var probeXray = new XrayManager();

        try
        {
            await probeXray.StartAsync(node, probePort);

            if (!await WaitForLocalPortAsync(probePort, ProxyPingStartupTimeout))
                return ElyTurboNodeResult.Empty(node, ElyTurboTargets.Length);

            using var handler = new HttpClientHandler
            {
                Proxy = new WebProxy($"socks5://127.0.0.1:{probePort}"),
                UseProxy = true,
                AllowAutoRedirect = false
            };
            using var http = new HttpClient(handler)
            {
                Timeout = ProxyPingHttpTimeout
            };

            http.DefaultRequestHeaders.UserAgent.ParseAdd("ElyProxy/1.1");

            var checks = await Task.WhenAll(ElyTurboTargets.Select(target => MeasureElyTurboTargetAsync(http, target)));
            return new ElyTurboNodeResult(node, checks);
        }
        catch
        {
            return ElyTurboNodeResult.Empty(node, ElyTurboTargets.Length);
        }
        finally
        {
            await probeXray.StopAsync();
        }
    }

    private static async Task<ElyTurboServiceResult> MeasureElyTurboTargetAsync(HttpClient http, ElyTurboTarget target)
    {
        try
        {
            var sw = Stopwatch.StartNew();
            using var response = await http.GetAsync(target.Url, HttpCompletionOption.ResponseHeadersRead);
            sw.Stop();

            var statusCode = (int)response.StatusCode;
            var isReachable = statusCode is >= 200 and < 500;
            return new ElyTurboServiceResult(target.Name, isReachable ? (int)sw.ElapsedMilliseconds : null);
        }
        catch
        {
            return new ElyTurboServiceResult(target.Name, null);
        }
    }

    private void SortNodesByLatency()
    {
        _dispatcher.Invoke(() =>
        {
            var sorted = AllNodes.OrderBy(n => n.Latency ?? int.MaxValue).ToList();
            AllNodes.Clear();
            foreach (var node in sorted)
                AllNodes.Add(node);
        });
    }

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static async Task<bool> WaitForLocalPortAsync(int port, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var client = new TcpClient();
                var connectTask = client.ConnectAsync(IPAddress.Loopback, port);
                var completed = await Task.WhenAny(connectTask, Task.Delay(250));
                if (completed == connectTask && connectTask.IsCompletedSuccessfully)
                    return true;
            }
            catch { }

            await Task.Delay(100);
        }

        return false;
    }

    private bool TryGetProxyPingUri(out Uri pingUri)
    {
        if (Uri.TryCreate(ProxyPingUrl, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            pingUri = uri;
            return true;
        }

        pingUri = new Uri(DefaultProxyPingUrl);
        return false;
    }

    private static string NormalizePingMode(string? value)
    {
        if (string.Equals(value, PingModeProxyGet, StringComparison.OrdinalIgnoreCase))
            return PingModeProxyGet;

        if (string.Equals(value, PingModeElyTurbo, StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, string.Concat("Super", "Ping"), StringComparison.OrdinalIgnoreCase))
            return PingModeElyTurbo;

        if (string.Equals(value, PingModeElyHard, StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, string.Concat("Ultra", "Ping"), StringComparison.OrdinalIgnoreCase))
            return PingModeElyHard;

        return PingModeTcp;
    }

    private static string NormalizeProxyPingUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return DefaultProxyPingUrl;

        var trimmed = value.Trim();
        return string.Equals(trimmed, LegacyProxyPingUrl, StringComparison.OrdinalIgnoreCase)
            ? DefaultProxyPingUrl
            : trimmed;
    }

    private static string BuildElyTurboSummary(ElyTurboNodeResult result)
    {
        var services = string.Join(", ", result.Services.Select(service =>
            $"{service.Name}: {FormatNullableLatency(service.LatencyMs)}"));

        return $"Лучший: {result.Node.DisplayName} · сервисы {result.SuccessCount}/{result.TotalCount} · среднее {FormatNullableLatency(result.AverageLatencyMs)} · {services}";
    }

    private static IOrderedEnumerable<ElyHardService.ElyHardNodeResult> OrderElyHardResults(IEnumerable<ElyHardService.ElyHardNodeResult> results)
    {
        return results
            .OrderByDescending(result => result.StableServices)
            .ThenByDescending(result => result.ReachableServices)
            .ThenByDescending(result => result.TotalSuccessfulAttempts)
            .ThenBy(result => result.AverageLatencyMs ?? int.MaxValue)
            .ThenBy(result => result.WorstLatencyMs ?? int.MaxValue);
    }

    private static string BuildElyHardSummary(ElyHardService.ElyHardNodeResult result)
    {
        var services = string.Join(", ", result.Services.Select(service => service.CompactDisplay));

        return $"ElyHard лучший: {result.Node.DisplayName} · стабильно {result.StableServices}/{result.TotalServices} · попытки {result.TotalSuccessfulAttempts}/{result.TotalAttempts} · среднее {FormatNullableLatency(result.AverageLatencyMs)} · худший {FormatNullableLatency(result.WorstLatencyMs)} · {services}";
    }

    private static string FormatNullableLatency(int? latency)
    {
        return latency.HasValue ? $"{latency.Value} ms" : "—";
    }

    private sealed record ElyTurboTarget(string Name, string Url);

    private sealed record ElyTurboServiceResult(string Name, int? LatencyMs)
    {
        public bool IsSuccess => LatencyMs.HasValue;
    }

    private sealed class ElyTurboNodeResult
    {
        public ElyTurboNodeResult(VlessNode node, IReadOnlyList<ElyTurboServiceResult> services)
        {
            Node = node;
            Services = services;
        }

        public VlessNode Node { get; }
        public IReadOnlyList<ElyTurboServiceResult> Services { get; }
        public int TotalCount => Services.Count;
        public int SuccessCount => Services.Count(service => service.IsSuccess);
        public int? AverageLatencyMs => SuccessCount > 0
            ? (int)Math.Round(Services.Where(service => service.LatencyMs.HasValue).Average(service => service.LatencyMs!.Value))
            : null;
        public int? FastestLatencyMs => Services
            .Where(service => service.LatencyMs.HasValue)
            .Select(service => service.LatencyMs!.Value)
            .DefaultIfEmpty()
            .Min() is var fastest && fastest > 0
                ? fastest
                : null;

        public static ElyTurboNodeResult Empty(VlessNode node, int serviceCount)
        {
            var services = ElyTurboTargets
                .Take(serviceCount)
                .Select(target => new ElyTurboServiceResult(target.Name, null))
                .ToList();
            return new ElyTurboNodeResult(node, services);
        }
    }

    #endregion

    #region Manual Server

    private void AddManualServer()
    {
        if (string.IsNullOrWhiteSpace(ManualServerUri)) return;

        var node = _parserService.ParseVlessUri(ManualServerUri.Trim());
        if (node == null)
        {
            StatusText = "Неверный формат vless://";
            return;
        }

        AllNodes.Add(node);
        if (!_manualNodes.Any(n => NodesMatch(n, node)))
            _manualNodes.Add(node);

        ManualServerUri = string.Empty;

        _ = SaveSettingsAsync();
        AppendLog($"[sys] Сервер добавлен: {node.DisplayName}");
        StatusText = $"Сервер добавлен: {node.DisplayName}";
    }

    #endregion

    #region Profiles

    private void CreateProfile()
    {
        if (string.IsNullOrWhiteSpace(NewProfileName)) return;

        var profile = new ProxyProfile { Name = NewProfileName.Trim() };
        Profiles.Add(profile);
        NewProfileName = string.Empty;
        SelectedProfile = profile;

        _ = _storageService.SaveProfilesAsync(Profiles);
        AppendLog($"[sys] Профиль создан: {profile.Name}");
    }

    private void DeleteProfile()
    {
        if (SelectedProfile == null) return;

        var name = SelectedProfile.Name;
        Profiles.Remove(SelectedProfile);
        SelectedProfile = null;
        ProfileNodes.Clear();

        _ = _storageService.SaveProfilesAsync(Profiles);
        AppendLog($"[sys] Профиль удалён: {name}");
    }

    private void AddToProfile()
    {
        if (SelectedNode == null || SelectedProfile == null) return;

        var exists = SelectedProfile.Nodes.Any(n =>
            n.Address == SelectedNode.Address && n.Port == SelectedNode.Port && n.UUID == SelectedNode.UUID);

        if (exists)
        {
            StatusText = "Сервер уже в профиле";
            return;
        }

        SelectedProfile.Nodes.Add(SelectedNode);
        RefreshProfileNodes();

        _ = _storageService.SaveProfilesAsync(Profiles);
        StatusText = $"Добавлен в «{SelectedProfile.Name}»";
    }

    private void RemoveFromProfile()
    {
        if (SelectedProfile == null || SelectedProfileNode == null) return;

        SelectedProfile.Nodes.Remove(SelectedProfileNode);
        SelectedProfileNode = null;
        RefreshProfileNodes();

        _ = _storageService.SaveProfilesAsync(Profiles);
    }

    private void ClearProfile()
    {
        if (SelectedProfile == null) return;

        SelectedProfile.Nodes.Clear();
        RefreshProfileNodes();

        _ = _storageService.SaveProfilesAsync(Profiles);
        AppendLog($"[sys] Профиль «{SelectedProfile.Name}» очищен");
    }

    private void RefreshProfileNodes()
    {
        ProfileNodes.Clear();
        if (SelectedProfile == null) return;

        foreach (var node in SelectedProfile.Nodes)
            ProfileNodes.Add(node);
    }

    private async Task ExportProfileAsync()
    {
        if (SelectedProfile == null) return;

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = $"{SelectedProfile.Name}.elyproxy",
            DefaultExt = ".elyproxy",
            Filter = "ElyProxy Profile|*.elyproxy"
        };

        if (dialog.ShowDialog() == true)
        {
            await _importExportService.ExportProfileAsync(SelectedProfile, dialog.FileName);
            AppendLog($"[sys] Профиль экспортирован: {dialog.FileName}");
            StatusText = "Профиль экспортирован";
        }
    }

    private async Task ImportProfileAsync()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            DefaultExt = ".elyproxy",
            Filter = "ElyProxy Profile|*.elyproxy|JSON|*.json"
        };

        if (dialog.ShowDialog() != true) return;

        var profile = await _importExportService.ImportProfileAsync(dialog.FileName);
        if (profile == null)
        {
            StatusText = "Ошибка импорта";
            return;
        }

        Profiles.Add(profile);
        SelectedProfile = profile;
        await _storageService.SaveProfilesAsync(Profiles);

        AppendLog($"[sys] Профиль импортирован: {profile.Name} ({profile.Nodes.Count} серверов)");
        StatusText = $"Импортирован: {profile.Name}";
    }

    #endregion

    #region Extensions

    private async Task ExportOmegaRulesAsync()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = OmegaRulesFileName,
            DefaultExt = ".sorl",
            Filter = "ZeroOmega Rules|*.sorl|All files|*.*"
        };

        if (dialog.ShowDialog() != true)
            return;

        try
        {
            await CopyOmegaRulesAsync(dialog.FileName);
            StatusText = "Файл правил ZeroOmega сохранён";
            AppendLog($"[sys] Файл правил ZeroOmega сохранён: {dialog.FileName}");
        }
        catch (Exception ex)
        {
            StatusText = "Ошибка сохранения файла правил";
            AppendLog($"[err] Не удалось сохранить файл правил ZeroOmega: {ex.Message}");
        }
    }

    private static async Task CopyOmegaRulesAsync(string destinationPath)
    {
        var assembly = typeof(MainViewModel).Assembly;
        var resourceName = assembly
            .GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith(OmegaRulesFileName, StringComparison.OrdinalIgnoreCase));

        if (resourceName != null)
        {
            await using var input = assembly.GetManifestResourceStream(resourceName)
                ?? throw new FileNotFoundException($"Встроенный ресурс {OmegaRulesFileName} не найден.");
            await using var output = File.Create(destinationPath);
            await input.CopyToAsync(output);
            return;
        }

        var sourcePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, OmegaRulesFileName);
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException($"Файл {OmegaRulesFileName} не найден.", sourcePath);

        File.Copy(sourcePath, destinationPath, true);
    }

    #endregion

    #region Helpers

    private void RefreshAllNodes()
    {
        void Refresh()
        {
            AllNodes.Clear();
            foreach (var sub in Subscriptions)
                foreach (var node in sub.Nodes)
                    AllNodes.Add(node);

            foreach (var node in _manualNodes)
                if (!AllNodes.Any(existing => NodesMatch(existing, node)))
                    AllNodes.Add(node);
        }

        if (_dispatcher.CheckAccess())
            Refresh();
        else
            _dispatcher.Invoke(Refresh);
    }

    private void ApplyAutoStartSetting(bool enabled)
    {
        try
        {
            _autoStartService.SetEnabled(enabled);
            StatusText = enabled ? "Автозапуск включён" : "Автозапуск выключен";
            AppendLog(enabled
                ? "[sys] Автозапуск вместе с Windows включён"
                : "[sys] Автозапуск вместе с Windows выключен");
        }
        catch (Exception ex)
        {
            StatusText = "Ошибка настройки автозапуска";
            AppendLog($"[err] Не удалось изменить автозапуск: {ex.Message}");

            try
            {
                var actual = _autoStartService.IsEnabled();
                if (_autoStartWithWindows != actual)
                {
                    _autoStartWithWindows = actual;
                    OnPropertyChanged(nameof(AutoStartWithWindows));
                }
            }
            catch { }
        }
    }

    private void SelectLastConnectedNode()
    {
        if (string.IsNullOrWhiteSpace(_lastConnectedNodeId))
            return;

        SelectedNode = AllNodes.FirstOrDefault(node => GetNodeKey(node) == _lastConnectedNodeId);
    }

    private void RememberConnectedNode(VlessNode node)
    {
        _lastConnectedNodeId = GetNodeKey(node);
        _ = SaveSettingsAsync();
    }

    private void UpdateConnectionInfo(VlessNode node)
    {
        var prefix = ElyTunEnabled
            ? $"ElyTun + socks5://127.0.0.1:{SocksPort}"
            : $"socks5://127.0.0.1:{SocksPort}";

        ConnectionInfo = $"{prefix}  →  {node.DisplayName}";
    }

    private XrayTunOptions? BuildElyTunOptionsOrNull()
    {
        return ElyTunEnabled
            ? new XrayTunOptions(CreateElyTunInterfaceName(), DefaultElyTunMtu, GetElyTunDnsServers(), IncludeIpv6: false)
            : null;
    }

    private static string CreateElyTunInterfaceName()
    {
        return $"ElyTun-{Guid.NewGuid():N}"[..15];
    }

    private static IReadOnlyList<string> GetElyTunDnsServers()
    {
        try
        {
            var servers = NetworkInterface.GetAllNetworkInterfaces()
                .Where(adapter =>
                    adapter.OperationalStatus == OperationalStatus.Up
                    && adapter.NetworkInterfaceType != NetworkInterfaceType.Loopback
                    && adapter.NetworkInterfaceType != NetworkInterfaceType.Tunnel
                    && adapter.NetworkInterfaceType != NetworkInterfaceType.Ppp
                    && !IsVirtualOrVpnAdapter(adapter))
                .SelectMany(adapter => adapter.GetIPProperties().DnsAddresses)
                .Where(address => address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address))
                .Select(address => address.ToString())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(3)
                .ToList();

            return servers.Count > 0 ? servers : DefaultElyTunDnsServers;
        }
        catch
        {
            return DefaultElyTunDnsServers;
        }
    }

    private static bool IsVirtualOrVpnAdapter(NetworkInterface adapter)
    {
        var text = $"{adapter.Name} {adapter.Description}".ToLowerInvariant();
        string[] markers = ["elytun", "happ", "tun", "tap", "vpn", "wintun", "wireguard", "sing-tun", "virtualbox", "radmin"];
        return markers.Any(text.Contains);
    }

    private bool EnsureElyTunCanRun(bool showMessage)
    {
        if (!IsRunningAsAdministrator())
        {
            DisableElyTunAfterPreflightFailure();
            StatusText = "ElyTun требует запуск от администратора";
            AppendLog("[err] ElyTun требует запуск приложения от администратора");

            if (showMessage)
            {
                MessageBox.Show(
                    "ElyTun работает как полноценный TUN/VPN-режим и меняет системные маршруты Windows.\n\n" +
                    "Запустите ElyProxy от имени администратора и включите ElyTun ещё раз.",
                    "ElyProxy — нужны права администратора",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            return false;
        }

        var xrayPath = _xrayManager.GetXrayPath();
        if (!File.Exists(xrayPath))
        {
            DisableElyTunAfterPreflightFailure();
            StatusText = "ElyTun: Xray не найден";
            AppendLog($"[err] ElyTun требует Xray: {xrayPath}");
            return false;
        }

        if (TryFindExternalXrayProcess(xrayPath, out var externalXray))
        {
            if (!ConfirmElyTunConflict("другой Xray-процесс", externalXray, showMessage))
                return false;
        }

        if (TryFindConflictingTunAdapter(out var conflictingAdapter))
        {
            if (!ConfirmElyTunConflict("активный VPN/TUN-адаптер", conflictingAdapter, showMessage))
                return false;
        }

        if (!TryGetXrayVersion(xrayPath, out var xrayVersion)
            || xrayVersion.CompareTo(MinElyTunXrayVersion) < 0)
        {
            DisableElyTunAfterPreflightFailure();
            var versionText = xrayVersion?.ToString() ?? "неизвестная версия";
            StatusText = "ElyTun: обновите Xray";
            AppendLog($"[err] ElyTun требует Xray {MinElyTunXrayVersion}+; сейчас: {versionText}");

            if (showMessage)
            {
                MessageBox.Show(
                    $"ElyTun требует Xray {MinElyTunXrayVersion} или новее.\n\n" +
                    $"Сейчас найден: {versionText}.\n" +
                    "Обновите файлы xray/xray.exe и xray/wintun.dll из свежего Xray-windows-64.zip.",
                    "ElyProxy — обновите Xray",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            return false;
        }

        var xrayDir = Path.GetDirectoryName(xrayPath);
        var wintunPath = xrayDir == null ? string.Empty : Path.Combine(xrayDir, "wintun.dll");
        if (!File.Exists(wintunPath))
        {
            DisableElyTunAfterPreflightFailure();
            StatusText = "ElyTun: не найден wintun.dll";
            AppendLog($"[err] ElyTun требует wintun.dll рядом с xray.exe: {wintunPath}");

            if (showMessage)
            {
                MessageBox.Show(
                    "Для ElyTun нужен файл wintun.dll рядом с xray.exe.\n\n" +
                    "Скачайте Xray-windows-64.zip и поместите wintun.dll в папку xray/ рядом с ElyProxy.exe.",
                    "ElyProxy — не найден wintun.dll",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            return false;
        }

        return true;
    }

    private bool ConfirmElyTunConflict(string conflictKind, string description, bool showMessage)
    {
        if (ElyTunIgnoreOtherTunAdapters)
        {
            AppendLog($"[warn] ElyTun игнорирует возможный конфликт: {conflictKind}: {description}");
            return true;
        }

        if (!showMessage)
        {
            DisableElyTunAfterPreflightFailure();
            StatusText = "ElyTun: другой VPN/TUN";
            AppendLog($"[err] ElyTun найден возможный конфликт: {conflictKind}: {description}");
            return false;
        }

        var result = MessageBox.Show(
            $"Найден {conflictKind}:\n{description}\n\n" +
            "ElyTun может работать нестабильно, если несколько VPN/TUN-клиентов одновременно меняют маршруты Windows.\n\n" +
            "Запустить ElyTun всё равно?",
            "ElyProxy — возможен конфликт VPN/TUN",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        if (result == MessageBoxResult.Yes)
        {
            AppendLog($"[warn] ElyTun запущен несмотря на возможный конфликт: {conflictKind}: {description}");
            return true;
        }

        DisableElyTunAfterPreflightFailure();
        StatusText = "ElyTun отменён";
        AppendLog($"[sys] ElyTun отменён пользователем из-за возможного конфликта: {description}");
        return false;
    }

    private static bool TryFindExternalXrayProcess(string expectedXrayPath, out string description)
    {
        description = string.Empty;

        foreach (var process in Process.GetProcessesByName("xray"))
        {
            try
            {
                var path = process.MainModule?.FileName;
                if (string.IsNullOrWhiteSpace(path))
                    continue;

                if (AreSamePath(path, expectedXrayPath))
                    continue;

                description = $"{path} (PID {process.Id})";
                return true;
            }
            catch
            {
                description = $"xray.exe (PID {process.Id})";
                return true;
            }
        }

        return false;
    }

    private static bool TryFindConflictingTunAdapter(out string description)
    {
        description = string.Empty;

        try
        {
            var adapter = NetworkInterface.GetAllNetworkInterfaces()
                .FirstOrDefault(item =>
                    item.OperationalStatus == OperationalStatus.Up
                    && !item.Name.StartsWith("ElyTun", StringComparison.OrdinalIgnoreCase)
                    && IsKnownTunAdapter(item));

            if (adapter == null)
                return false;

            description = $"{adapter.Name} ({adapter.Description})";
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsKnownTunAdapter(NetworkInterface adapter)
    {
        var text = $"{adapter.Name} {adapter.Description}".ToLowerInvariant();
        string[] ignoredSystemTunnels = ["teredo", "isatap", "iphttps", "6to4"];
        if (ignoredSystemTunnels.Any(text.Contains))
            return false;

        string[] markers = ["happ", "sing-tun", "wintun", "wireguard", "tap-windows", "openvpn", "outline"];
        return markers.Any(text.Contains);
    }

    private static bool AreSamePath(string left, string right)
    {
        try
        {
            return string.Equals(
                Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }
    }

    private void DisableElyTunAfterPreflightFailure()
    {
        _elyTunEnabled = false;
        OnPropertyChanged(nameof(ElyTunEnabled));
        _ = SaveSettingsAsync();
    }

    private static bool IsRunningAsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static bool TryGetXrayVersion(string xrayPath, out Version version)
    {
        version = new Version(0, 0, 0);

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = xrayPath,
                Arguments = "version",
                WorkingDirectory = Path.GetDirectoryName(xrayPath) ?? string.Empty,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = Process.Start(psi);
            if (process == null)
                return false;

            if (!process.WaitForExit(2500))
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                return false;
            }

            var output = process.StandardOutput.ReadToEnd();
            if (string.IsNullOrWhiteSpace(output))
                output = process.StandardError.ReadToEnd();

            var firstLine = output
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault();

            var versionText = firstLine?
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Skip(1)
                .FirstOrDefault();

            if (!Version.TryParse(versionText, out var parsed) || parsed == null)
                return false;

            version = parsed;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void SuppressDisconnectNotifications(int count = 2)
    {
        _suppressedDisconnectNotifications = Math.Max(_suppressedDisconnectNotifications, count);
    }

    private bool EnsureSocksPortAvailableForStart()
    {
        if (IsLocalTcpPortAvailable(SocksPort))
            return true;

        var xrayPath = _xrayManager.GetXrayPath();
        if (PortOwnerService.TryStopStaleXrayOnPort(SocksPort, xrayPath, out var cleanupMessage))
        {
            AppendLog(cleanupMessage);
            if (IsLocalTcpPortAvailable(SocksPort))
                return true;
        }

        StatusText = $"Порт {SocksPort} занят";
        var owner = string.IsNullOrWhiteSpace(cleanupMessage)
            ? PortOwnerService.DescribeTcpPortOwner(SocksPort)
            : cleanupMessage;
        AppendLog($"[err] {owner}");
        SuppressDisconnectNotifications();
        return false;
    }

    private static bool IsLocalTcpPortAvailable(int port)
    {
        try
        {
            var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            listener.Stop();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task ReconnectAsync(VlessNode node)
    {
        if (_isReconnecting || _isDisposed)
            return;

        _isReconnecting = true;
        AppendLog($"[sys] Обрыв соединения. Переподключение через 3 сек.: {node.DisplayName}");

        try
        {
            if (!CanTryAutoReconnect())
                return;

            await Task.Delay(TimeSpan.FromSeconds(3));

            if (_isDisposed || !AutoReconnect || IsConnected)
                return;

            IsLoading = true;
            StatusText = "Переподключение...";

            if (ElyTunEnabled && !EnsureElyTunCanRun(showMessage: true))
            {
                _connectedNode = null;
                ConnectionInfo = string.Empty;
                ConnectedNodeName = string.Empty;
                return;
            }

            if (!EnsureSocksPortAvailableForStart())
            {
                _connectedNode = null;
                ConnectionInfo = string.Empty;
                ConnectedNodeName = string.Empty;
                return;
            }

            await _xrayManager.StartAsync(node, SocksPort, BuildElyTunOptionsOrNull());

            _connectedNode = node;
            ConnectedNodeName = node.DisplayName;
            UpdateConnectionInfo(node);
            RememberConnectedNode(node);

            if (SystemProxyEnabled)
                await TryActivateSystemProxyAsync();
        }
        catch (Exception ex)
        {
            StatusText = "Ошибка переподключения";
            AppendLog($"[err] Не удалось переподключиться: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
            _isReconnecting = false;
        }
    }

    private DateTime _lastAutoReconnectAttemptUtc;

    private bool CanTryAutoReconnect()
    {
        var now = DateTime.UtcNow;
        if (now - _lastAutoReconnectAttemptUtc > AutoReconnectAttemptWindow)
            _autoReconnectAttempts = 0;

        _lastAutoReconnectAttemptUtc = now;
        _autoReconnectAttempts++;

        if (_autoReconnectAttempts <= MaxAutoReconnectAttempts)
            return true;

        StatusText = "Автопереподключение остановлено";
        AppendLog($"[err] Автопереподключение остановлено после {MaxAutoReconnectAttempts} неудачных попыток.");
        _connectedNode = null;
        ConnectionInfo = string.Empty;
        ConnectedNodeName = string.Empty;
        return false;
    }

    private static string GetNodeKey(VlessNode node)
    {
        return string.Join("|",
            node.UUID,
            node.Address,
            node.Port,
            node.Network,
            node.Security,
            node.SNI,
            node.PublicKey,
            node.ShortId);
    }

    private static bool NodesMatch(VlessNode left, VlessNode right)
    {
        return left.Address == right.Address
            && left.Port == right.Port
            && left.UUID == right.UUID
            && left.Network == right.Network
            && left.Security == right.Security;
    }

    private string[] GetSystemProxyRules()
    {
        var rules = SystemProxyRulesText
            .Split(['\r', '\n', '|', ';', ','], StringSplitOptions.RemoveEmptyEntries)
            .Select(rule => rule.Trim())
            .Where(rule => !string.IsNullOrWhiteSpace(rule))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return rules.Length > 0 ? rules : PacServerService.DefaultBypassRules;
    }

    private static string FormatRules(IEnumerable<string> rules)
    {
        return string.Join(Environment.NewLine, rules);
    }

    private void CopyProxy()
    {
        try
        {
            Clipboard.SetText($"socks5://127.0.0.1:{SocksPort}");
            StatusText = "Адрес прокси скопирован";
        }
        catch { }
    }

    private void ResetSystemProxyRules()
    {
        SystemProxyRulesText = FormatRules(PacServerService.DefaultBypassRules);
        StatusText = "Правила разделения сброшены";
    }

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch { }
    }

    private void OnLogReceived(string message)
    {
        _dispatcher.InvokeAsync(() => AppendLog(message));
    }

    private void OnStatusChanged(bool running)
    {
        _dispatcher.InvokeAsync(() =>
        {
            IsConnected = running;
            StatusText = running ? "Подключено" : "Отключено";
            if (!running)
            {
                ConnectionInfo = string.Empty;
                ConnectedNodeName = string.Empty;

                if (_suppressedDisconnectNotifications > 0)
                {
                    _suppressedDisconnectNotifications--;
                    return;
                }

                DeactivateSystemProxyForDisconnectedServer(silent: true);

                if (AutoReconnect && !_isReconnecting && !_isDisposed && _connectedNode != null)
                    _ = ReconnectAsync(_connectedNode);
            }
        });
    }

    private async void OnSubscriptionUpdateTimerTick(object? sender, EventArgs e)
    {
        if (IsLoading || Subscriptions.Count == 0)
            return;

        AppendLog("[sys] Автообновление подписок...");
        await UpdateAllSubscriptionsAsync();
    }

    private void ConfigureSubscriptionUpdateTimer()
    {
        _subscriptionUpdateTimer.Stop();

        if (SubscriptionUpdateIntervalMinutes <= 0)
            return;

        _subscriptionUpdateTimer.Interval = TimeSpan.FromMinutes(SubscriptionUpdateIntervalMinutes);
        _subscriptionUpdateTimer.Start();
    }

    private void DeactivateSystemProxyForDisconnectedServer(bool silent)
    {
        if (!SystemProxyEnabled)
            return;

        try
        {
            StopSystemProxy(PacUrl, restorePrevious: true, silent);
        }
        catch (Exception ex)
        {
            AppendLog($"[err] Не удалось выключить системный прокси: {ex.Message}");
        }
    }

    private async Task TryActivateSystemProxyAsync()
    {
        try
        {
            await StartSystemProxyAsync();
        }
        catch (Exception ex)
        {
            StatusText = "Ошибка системного прокси";
            AppendLog($"[err] Не удалось включить системный прокси: {ex.Message}");
        }
    }

    private async Task ApplyElyTunSettingAsync(bool enabled)
    {
        try
        {
            if (enabled && !EnsureElyTunCanRun(showMessage: true))
                return;

            if (enabled && SystemProxyEnabled)
            {
                StopSystemProxy(PacUrl, restorePrevious: true, silent: true);
                _systemProxyEnabled = false;
                OnPropertyChanged(nameof(SystemProxyEnabled));
                AppendLog("[sys] Системный прокси выключен: ElyTun использует полный TUN-режим");
            }

            if (IsConnected)
                await RestartActiveXrayAsync();
            else
                StatusText = enabled
                    ? "ElyTun включится после подключения"
                    : "ElyTun выключен";

            await SaveSettingsAsync();
        }
        catch (Exception ex)
        {
            if (enabled)
            {
                _elyTunEnabled = false;
                OnPropertyChanged(nameof(ElyTunEnabled));
            }

            StatusText = "Ошибка ElyTun";
            AppendLog($"[err] Не удалось изменить режим ElyTun: {ex.Message}");
        }
    }

    private async Task RestartActiveXrayAsync()
    {
        if (_connectedNode == null || !IsConnected)
            return;

        if (ElyTunEnabled && !EnsureElyTunCanRun(showMessage: true))
            return;

        try
        {
            IsLoading = true;
            StatusText = "Перезапуск Xray...";
            SuppressDisconnectNotifications(3);
            await _xrayManager.RestartAsync(_connectedNode, SocksPort, BuildElyTunOptionsOrNull());
            UpdateConnectionInfo(_connectedNode);

            if (SystemProxyEnabled)
                await TryActivateSystemProxyAsync();

            if (ElyTunEnabled)
                _ = VerifyElyTunConnectivityAsync();
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task VerifyElyTunConnectivityAsync()
    {
        await Task.Delay(TimeSpan.FromSeconds(3));

        try
        {
            using var handler = new HttpClientHandler
            {
                UseProxy = false,
                AllowAutoRedirect = false
            };
            using var http = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(8)
            };

            http.DefaultRequestHeaders.UserAgent.ParseAdd("ElyProxy/ElyTun");

            var sw = Stopwatch.StartNew();
            using var response = await http.GetAsync("https://www.gstatic.com/generate_204", HttpCompletionOption.ResponseHeadersRead);
            sw.Stop();

            if ((int)response.StatusCode is >= 200 and < 400)
            {
                await _dispatcher.InvokeAsync(() =>
                {
                    StatusText = $"ElyTun работает: {sw.ElapsedMilliseconds} ms";
                    AppendLog($"[sys] ElyTun self-test OK: HTTP {(int)response.StatusCode}, {sw.ElapsedMilliseconds} ms");
                });
                return;
            }

            await _dispatcher.InvokeAsync(() =>
            {
                StatusText = "ElyTun: HTTP-тест не прошёл";
                AppendLog($"[err] ElyTun self-test failed: HTTP {(int)response.StatusCode}");
            });
        }
        catch (Exception ex)
        {
            await _dispatcher.InvokeAsync(() =>
            {
                StatusText = "ElyTun: сеть не отвечает";
                AppendLog($"[err] ElyTun self-test failed: {ex.GetType().Name}: {ex.Message}");
            });
        }
    }

    private async Task ApplySystemProxySettingAsync(bool enabled)
    {
        try
        {
            if (enabled && ElyTunEnabled)
            {
                _elyTunEnabled = false;
                OnPropertyChanged(nameof(ElyTunEnabled));
                AppendLog("[sys] ElyTun выключен: системный прокси использует PAC-режим");

                if (IsConnected)
                    await RestartActiveXrayAsync();
            }

            if (enabled && IsConnected)
                await StartSystemProxyAsync();
            else if (enabled)
                StatusText = "Системный прокси включится после подключения";
            else
                StopSystemProxy(PacUrl, restorePrevious: true, silent: false);

            await SaveSettingsAsync();
        }
        catch (Exception ex)
        {
            if (enabled)
            {
                _pacServerService.Stop();
                _systemProxyEnabled = false;
                OnPropertyChanged(nameof(SystemProxyEnabled));
            }

            StatusText = "Ошибка системного прокси";
            AppendLog($"[err] Не удалось изменить системный прокси: {ex.Message}");
        }
    }

    private async Task RestartSystemProxyAsync(string oldPacUrl)
    {
        try
        {
            if (!string.Equals(oldPacUrl, PacUrl, StringComparison.OrdinalIgnoreCase))
                StopSystemProxy(oldPacUrl, restorePrevious: false, silent: true);

            await StartSystemProxyAsync();
            await SaveSettingsAsync();
        }
        catch (Exception ex)
        {
            StatusText = "Ошибка системного прокси";
            AppendLog($"[err] Не удалось перезапустить системный прокси: {ex.Message}");
        }
    }

    private async Task StartSystemProxyAsync()
    {
        ValidatePacPort();

        await _pacServerService.StartAsync(PacPort, SocksPort, GetSystemProxyRules());

        var currentAutoConfigUrl = _windowsProxyService.GetAutoConfigUrl();
        if (!string.Equals(currentAutoConfigUrl, PacUrl, StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(_previousAutoConfigUrl))
        {
            _previousAutoConfigUrl = currentAutoConfigUrl;
        }

        _windowsProxyService.EnablePac(PacUrl);
        StatusText = "Системный прокси включён";
        AppendLog("[sys] Системный прокси включён");
    }

    private void StopSystemProxy(string pacUrl, bool restorePrevious, bool silent)
    {
        _windowsProxyService.DisablePac(pacUrl, restorePrevious ? _previousAutoConfigUrl : null);
        if (restorePrevious)
            _previousAutoConfigUrl = null;

        _pacServerService.Stop();
        if (!silent)
        {
            StatusText = "Системный прокси выключен";
            AppendLog("[sys] Системный прокси выключен");
        }
    }

    private void ValidatePacPort()
    {
        if (PacPort is < 1024 or > 65535)
            throw new InvalidOperationException("PAC порт должен быть в диапазоне 1024-65535.");
    }

    private void AppendLog(string message)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        LogText += $"[{timestamp}] {message}\n";

        const int maxLogLength = 50_000;
        if (LogText.Length > maxLogLength)
            LogText = LogText[^(maxLogLength / 2)..];
    }

    #endregion

    #region Persistence

    private async Task LoadAllDataAsync()
    {
        try
        {
            var settings = await _storageService.LoadSettingsAsync();
            _isLoadingSettings = true;
            SocksPort = settings.SocksPort > 0 ? settings.SocksPort : 1080;
            AutoConnect = settings.AutoConnect;
            AutoReconnect = settings.AutoReconnect;
            ShowLogs = settings.ShowLogs;
            PingMode = settings.PingMode;
            ProxyPingUrl = settings.ProxyPingUrl;
            ProxyPingConcurrency = settings.ProxyPingConcurrency;
            PacPort = settings.PacPort is >= 1024 and <= 65535 ? settings.PacPort : 18080;
            _previousAutoConfigUrl = settings.PreviousAutoConfigUrl;
            ElyTunEnabled = settings.ElyTunEnabled;
            ElyTunIgnoreOtherTunAdapters = settings.ElyTunIgnoreOtherTunAdapters;
            SystemProxyEnabled = !ElyTunEnabled && (settings.SystemProxyEnabled || settings.PacModeEnabled);
            SystemProxyRulesText = FormatRules(settings.SystemProxyBypassRules.Count > 0
                ? settings.SystemProxyBypassRules
                : PacServerService.DefaultBypassRules);
            SubscriptionUpdateIntervalMinutes = settings.SubscriptionUpdateIntervalMinutes;
            _lastConnectedNodeId = settings.LastConnectedNodeId;
            _manualNodes.Clear();
            if (settings.ManualNodes?.Count > 0)
                _manualNodes.AddRange(settings.ManualNodes);

            try
            {
                if (settings.AutoStartWithWindows && !_autoStartService.IsEnabled())
                    _autoStartService.SetEnabled(true);

                AutoStartWithWindows = settings.AutoStartWithWindows || _autoStartService.IsEnabled();
            }
            catch (Exception ex)
            {
                AutoStartWithWindows = settings.AutoStartWithWindows;
                AppendLog($"[err] Не удалось проверить автозапуск: {ex.Message}");
            }
            finally
            {
                _isLoadingSettings = false;
                ConfigureSubscriptionUpdateTimer();
            }

            var subs = await _storageService.LoadSubscriptionsAsync();
            foreach (var sub in subs)
                Subscriptions.Add(sub);

            var profiles = await _storageService.LoadProfilesAsync();
            foreach (var p in profiles)
                Profiles.Add(p);

            RefreshAllNodes();

            AppendLog($"[sys] Загружено: {Subscriptions.Count} подписок, {AllNodes.Count} серверов, {Profiles.Count} профилей");

            SelectLastConnectedNode();

            if (AutoConnect)
            {
                if (SelectedNode != null)
                {
                    AppendLog($"[sys] Автоподключение к последнему серверу: {SelectedNode.DisplayName}");
                    await ConnectToNodeAsync(SelectedNode);
                }
                else if (!string.IsNullOrWhiteSpace(_lastConnectedNodeId))
                {
                    AppendLog("[sys] Последний сервер для автоподключения не найден");
                }
            }
        }
        catch (Exception ex)
        {
            AppendLog($"[err] Ошибка загрузки данных: {ex.Message}");
            _isLoadingSettings = false;
        }
    }

    private async Task SaveSettingsAsync()
    {
        try
        {
            var settings = new AppSettings
            {
                SocksPort = SocksPort,
                AutoStartWithWindows = AutoStartWithWindows,
                AutoConnect = AutoConnect,
                AutoReconnect = AutoReconnect,
                ShowLogs = ShowLogs,
                PingMode = PingMode,
                ProxyPingUrl = ProxyPingUrl,
                ProxyPingConcurrency = ProxyPingConcurrency,
                SystemProxyEnabled = SystemProxyEnabled,
                SystemProxyBypassRules = GetSystemProxyRules().ToList(),
                PacModeEnabled = SystemProxyEnabled,
                PacPort = PacPort,
                ElyTunEnabled = ElyTunEnabled,
                ElyTunIgnoreOtherTunAdapters = ElyTunIgnoreOtherTunAdapters,
                PreviousAutoConfigUrl = _previousAutoConfigUrl,
                SubscriptionUpdateIntervalMinutes = SubscriptionUpdateIntervalMinutes,
                LastConnectedNodeId = _lastConnectedNodeId,
                ManualNodes = _manualNodes.ToList()
            };

            await _storageService.SaveSettingsAsync(settings);
        }
        catch { }
    }

    #endregion

    public void Dispose()
    {
        _isDisposed = true;
        _subscriptionUpdateTimer.Stop();
        _subscriptionUpdateTimer.Tick -= OnSubscriptionUpdateTimerTick;
        try
        {
            if (SystemProxyEnabled)
                StopSystemProxy(PacUrl, restorePrevious: true, silent: true);
        }
        catch { }

        _pacServerService.Dispose();
        _xrayManager.Dispose();
        _subscriptionService.Dispose();
        GC.SuppressFinalize(this);
    }
}
