using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Net.Sockets;
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

    private readonly SubscriptionService _subscriptionService;
    private readonly ParserService _parserService;
    private readonly StorageService _storageService;
    private readonly ImportExportService _importExportService;
    private readonly AutoStartService _autoStartService;
    private readonly PacServerService _pacServerService;
    private readonly WindowsProxyService _windowsProxyService;
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
    private bool _isConnected;
    private bool _isLoading;
    private bool _autoStartWithWindows;
    private bool _autoConnect;
    private bool _autoReconnect;
    private bool _showLogs = true;
    private bool _systemProxyEnabled;
    private bool _isLoadingSettings;
    private bool _isReconnecting;
    private bool _isDisposed;
    private int _suppressedDisconnectNotifications;
    private int _subscriptionUpdateIntervalMinutes;
    private int _pacPort = 18080;
    private string _systemProxyRulesText = string.Empty;
    private string? _lastConnectedNodeId;
    private string? _previousAutoConfigUrl;
    private int _socksPort = 1080;

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
            if (!SetProperty(ref _socksPort, value))
                return;

            if (SystemProxyEnabled && IsConnected && !_isLoadingSettings)
                _ = RestartSystemProxyAsync(PacUrl);
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
                SuppressDisconnectNotifications();
                await _xrayManager.StopAsync();
            }

            await _xrayManager.StartAsync(node, SocksPort);

            _connectedNode = node;
            ConnectedNodeName = node.DisplayName;
            ConnectionInfo = $"socks5://127.0.0.1:{SocksPort}  →  {node.DisplayName}";
            RememberConnectedNode(node);

            if (SystemProxyEnabled)
                await TryActivateSystemProxyAsync();
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

    private async Task PingAllAsync()
    {
        IsLoading = true;
        StatusText = "Проверка серверов...";
        AppendLog("[sys] Запуск проверки пинга...");

        var nodes = AllNodes.ToList();
        var tasks = nodes.Select(async node =>
        {
            node.Latency = await MeasureLatencyAsync(node.Address, node.Port);
        });

        await Task.WhenAll(tasks);

        _dispatcher.Invoke(() =>
        {
            var sorted = AllNodes.OrderBy(n => n.Latency ?? int.MaxValue).ToList();
            AllNodes.Clear();
            foreach (var node in sorted)
                AllNodes.Add(node);
        });

        var reachable = nodes.Count(n => n.Latency.HasValue);
        StatusText = $"Пинг: {reachable}/{nodes.Count} доступно";
        AppendLog($"[sys] Пинг завершён: {reachable}/{nodes.Count} доступно");
        IsLoading = false;
    }

    private static async Task<int?> MeasureLatencyAsync(string host, int port)
    {
        try
        {
            using var client = new TcpClient();
            var sw = Stopwatch.StartNew();
            var connectTask = client.ConnectAsync(host, port);
            var completed = await Task.WhenAny(connectTask, Task.Delay(5000));

            if (completed == connectTask && connectTask.IsCompletedSuccessfully)
            {
                sw.Stop();
                return (int)sw.ElapsedMilliseconds;
            }
            return null;
        }
        catch
        {
            return null;
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

    private void SuppressDisconnectNotifications()
    {
        _suppressedDisconnectNotifications = Math.Max(_suppressedDisconnectNotifications, 2);
    }

    private async Task ReconnectAsync(VlessNode node)
    {
        if (_isReconnecting || _isDisposed)
            return;

        _isReconnecting = true;
        AppendLog($"[sys] Обрыв соединения. Переподключение через 3 сек.: {node.DisplayName}");

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(3));

            if (_isDisposed || !AutoReconnect || IsConnected)
                return;

            IsLoading = true;
            StatusText = "Переподключение...";

            await _xrayManager.StartAsync(node, SocksPort);

            _connectedNode = node;
            ConnectedNodeName = node.DisplayName;
            ConnectionInfo = $"socks5://127.0.0.1:{SocksPort}  →  {node.DisplayName}";
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

    private async Task ApplySystemProxySettingAsync(bool enabled)
    {
        try
        {
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
            PacPort = settings.PacPort is >= 1024 and <= 65535 ? settings.PacPort : 18080;
            _previousAutoConfigUrl = settings.PreviousAutoConfigUrl;
            SystemProxyEnabled = settings.SystemProxyEnabled || settings.PacModeEnabled;
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
                SystemProxyEnabled = SystemProxyEnabled,
                SystemProxyBypassRules = GetSystemProxyRules().ToList(),
                PacModeEnabled = SystemProxyEnabled,
                PacPort = PacPort,
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
