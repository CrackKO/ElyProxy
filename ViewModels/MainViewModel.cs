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
    private readonly SubscriptionService _subscriptionService;
    private readonly ParserService _parserService;
    private readonly StorageService _storageService;
    private readonly ImportExportService _importExportService;
    private readonly XrayManager _xrayManager;
    private readonly Dispatcher _dispatcher;

    private VlessNode? _selectedNode;
    private Subscription? _selectedSubscription;
    private ProxyProfile? _selectedProfile;
    private VlessNode? _selectedProfileNode;
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
    private int _socksPort = 1080;

    public MainViewModel()
    {
        _dispatcher = Application.Current.Dispatcher;
        _subscriptionService = new SubscriptionService();
        _parserService = new ParserService();
        _storageService = new StorageService();
        _importExportService = new ImportExportService();
        _xrayManager = new XrayManager();

        _xrayManager.LogReceived += OnLogReceived;
        _xrayManager.StatusChanged += OnStatusChanged;

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

        _ = LoadAllDataAsync();
    }

    #region Collections

    public ObservableCollection<VlessNode> AllNodes { get; }
    public ObservableCollection<Subscription> Subscriptions { get; }
    public ObservableCollection<ProxyProfile> Profiles { get; }
    public ObservableCollection<VlessNode> ProfileNodes { get; }

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
        set => SetProperty(ref _socksPort, value);
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
                await _xrayManager.StopAsync();

            await _xrayManager.StartAsync(node, SocksPort);

            ConnectedNodeName = node.DisplayName;
            ConnectionInfo = $"socks5://127.0.0.1:{SocksPort}  →  {node.DisplayName}";
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
            await _xrayManager.StopAsync();
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

    #region Helpers

    private void RefreshAllNodes()
    {
        _dispatcher.Invoke(() =>
        {
            AllNodes.Clear();
            foreach (var sub in Subscriptions)
                foreach (var node in sub.Nodes)
                    AllNodes.Add(node);

            var settings = _storageService.LoadSettingsAsync().GetAwaiter().GetResult();
            if (settings.ManualNodes != null)
                foreach (var node in settings.ManualNodes)
                    AllNodes.Add(node);
        });
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
            }
        });
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
            SocksPort = settings.SocksPort > 0 ? settings.SocksPort : 1080;

            var subs = await _storageService.LoadSubscriptionsAsync();
            foreach (var sub in subs)
                Subscriptions.Add(sub);

            var profiles = await _storageService.LoadProfilesAsync();
            foreach (var p in profiles)
                Profiles.Add(p);

            RefreshAllNodes();

            if (settings.ManualNodes?.Count > 0)
            {
                foreach (var node in settings.ManualNodes)
                    if (!AllNodes.Any(n => n.Address == node.Address && n.Port == node.Port && n.UUID == node.UUID))
                        AllNodes.Add(node);
            }

            AppendLog($"[sys] Загружено: {Subscriptions.Count} подписок, {AllNodes.Count} серверов, {Profiles.Count} профилей");
        }
        catch (Exception ex)
        {
            AppendLog($"[err] Ошибка загрузки данных: {ex.Message}");
        }
    }

    private async Task SaveSettingsAsync()
    {
        try
        {
            var manualNodes = AllNodes
                .Where(n => !Subscriptions.Any(s => s.Nodes.Contains(n)))
                .ToList();

            var settings = new AppSettings
            {
                SocksPort = SocksPort,
                ManualNodes = manualNodes
            };

            await _storageService.SaveSettingsAsync(settings);
        }
        catch { }
    }

    #endregion

    public void Dispose()
    {
        _xrayManager.Dispose();
        _subscriptionService.Dispose();
        GC.SuppressFinalize(this);
    }
}
