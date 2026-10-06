using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ElyProxy.Core;
using ElyProxy.Models;
using ElyProxy.Services;
using ElyProxy.Views.Sheets;
using Microsoft.Win32;

namespace ElyProxy.ViewModels;

public partial class MainViewModel : ViewModelBase, IDisposable
{
	private sealed record ElyTurboTarget(string Name, string Url);

	private sealed record ElyTurboServiceResult(string Name, int? LatencyMs)
	{
		public bool IsSuccess => LatencyMs.HasValue;
	}

	private sealed class ElyTurboNodeResult
	{
		public VlessNode Node { get; }

		public IReadOnlyList<ElyTurboServiceResult> Services { get; }

		public int TotalCount => Services.Count;

		public int SuccessCount => Services.Count((ElyTurboServiceResult service) => service.IsSuccess);

		public int? AverageLatencyMs
		{
			get
			{
				if (SuccessCount <= 0)
				{
					return null;
				}
				return (int)Math.Round(Services.Where((ElyTurboServiceResult service) => service.LatencyMs.HasValue).Average((ElyTurboServiceResult service) => service.LatencyMs.Value));
			}
		}

		public int? FastestLatencyMs
		{
			get
			{
				int fastest = (from service in Services
					where service.LatencyMs.HasValue
					select service.LatencyMs.Value).DefaultIfEmpty().Min();
				if (fastest <= 0)
				{
					return null;
				}
				return fastest;
			}
		}

		public ElyTurboNodeResult(VlessNode node, IReadOnlyList<ElyTurboServiceResult> services)
		{
			Node = node;
			Services = services;
		}

		public static ElyTurboNodeResult Empty(VlessNode node, int serviceCount)
		{
			List<ElyTurboServiceResult> services = (from target in ElyTurboTargets.Take(serviceCount)
				select new ElyTurboServiceResult(target.Name, null)).ToList();
			return new ElyTurboNodeResult(node, services);
		}
	}

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

	private const int MaxLogLength = 50000;

	private const int MaxPendingLogMessages = 1000;

	private const int MaxLogMessagesPerBatch = 200;

	private static readonly string[] DefaultElyTunDnsServers = new string[2] { "1.1.1.1", "8.8.8.8" };

	private static readonly TimeSpan TcpPingTimeout = TimeSpan.FromMilliseconds(2500.0);

	private static readonly TimeSpan ProxyPingHttpTimeout = TimeSpan.FromSeconds(3.0);

	private static readonly TimeSpan ProxyPingStartupTimeout = TimeSpan.FromSeconds(2.0);

	private readonly SubscriptionService _subscriptionService;

	private readonly SubscriptionRefreshCoordinator _subscriptionRefreshCoordinator;

	private readonly ParserService _parserService;

	private readonly StorageService _storageService;

	private bool _canSaveSettings = true;

	private bool _canSaveSubscriptions = true;

	private bool _canSaveProfiles = true;

	private bool _dataReady;

	private bool _subscriptionsRecoveryWarningShown;

	private bool _settingsSaveErrorReported;

	private bool _profilesSaveErrorReported;

	private readonly ImportExportService _importExportService;

	private readonly RunningApplicationService _runningApplicationService;

	private readonly AutoStartService _autoStartService;

	private readonly PacServerService _pacServerService;

	private readonly IWindowsProxyService _windowsProxyService;

	private readonly WindowsProxyRecoveryService _windowsProxyRecoveryService;

	private readonly ConnectionDiagnosticsService _connectionDiagnosticsService;

	private readonly RuntimePreflightService _runtimePreflightService = new();

	private readonly AutoReconnectPolicy _autoReconnectPolicy = new(3, TimeSpan.FromMinutes(2));

	private readonly ElyHardService _elyHardService;

	private readonly XrayManager _xrayManager;

	private readonly SingBoxTunManager _singBoxTunManager;

	private readonly SingBoxProxyManager _singBoxProxyManager;

	private readonly UpdateService _updateService;

	private readonly Dispatcher _dispatcher;

	private readonly SemaphoreSlim _coreLifecycleLock = new SemaphoreSlim(1, 1);

	private readonly SemaphoreSlim _subscriptionMutationLock = new SemaphoreSlim(1, 1);

	private readonly SemaphoreSlim _profileMutationLock = new SemaphoreSlim(1, 1);

	private readonly DispatcherTimer _subscriptionUpdateTimer;

	private readonly DispatcherTimer _logFlushTimer;

	private readonly ConcurrentQueue<string> _pendingLogMessages = new ConcurrentQueue<string>();

	private readonly List<VlessNode> _manualNodes = new List<VlessNode>();

	private VlessNode? _selectedNode;

	private Subscription? _selectedSubscription;

	private ProxyProfile? _selectedProfile;

	private VlessNode? _selectedProfileNode;

	private RunningApplication? _selectedApplicationBypass;

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

	private string _diagnosticsReport = string.Empty;

	private string _updateStatus = "Проверка обновлений ещё не выполнялась";

	private string _updateBadgeText = "Обновления";

	private Brush _updateBadgeBrush = Brushes.Gray;

	private bool _isConnected;

	private bool _isLoading;

	private bool _autoStartWithWindows;

	private bool _autoConnect;

	private bool _autoReconnect;

	private bool _showLogs = true;

	private bool _autoUpdate;

	private bool _isCheckingForUpdates;

	private bool _isUpdating;

	private bool _systemProxyEnabled;

	private bool _proxyProEnabled;

	private bool _elyTunEnabled;

	private bool _elyTunIpv6Enabled = true;

	private RoutingPreset _routingPreset;

	private bool _isLoadingSettings;

	private bool _isReconnecting;

	private bool _isDisposed;

	private bool _isRelaunchingForElevation;

	private readonly bool _launchElyTunOnStartup;

	private readonly int? _handoffParentProcessId;

	private int _coreLifecycleTransition;

	private int _connectionGeneration;

	private int _subscriptionUpdateIntervalMinutes;

	private int _pacPort = 18080;

	private int _proxyPingConcurrency = 10;

	private int _pendingLogMessageCount;

	private int _droppedLogMessageCount;

	private string _systemProxyRulesText = string.Empty;

	private string _pingMode = "Tcp";

	private string _proxyPingUrl = "https://youtube.com";

	private string? _lastConnectedNodeId;

	private string? _previousAutoConfigUrl;

	private int _socksPort = 1080;

	private static readonly ElyTurboTarget[] ElyTurboTargets = new ElyTurboTarget[6]
	{
		new ElyTurboTarget("Discord", "https://discord.com/api/v9/experiments"),
		new ElyTurboTarget("Telegram", "https://telegram.org"),
		new ElyTurboTarget("YouTube", "https://www.youtube.com/generate_204"),
		new ElyTurboTarget("Spotify", "https://open.spotify.com"),
		new ElyTurboTarget("GitHub", "https://github.com"),
		new ElyTurboTarget("Gmail", "https://mail.google.com/mail/generate_204")
	};

	public ObservableCollection<VlessNode> AllNodes { get; }

	public ObservableCollection<Subscription> Subscriptions { get; }

	public ObservableCollection<ProxyProfile> Profiles { get; }

	public ObservableCollection<VlessNode> ProfileNodes { get; }

	public ObservableCollection<RunningApplication> ApplicationBypassApplications { get; }

	public IReadOnlyList<KeyValuePair<int, string>> SubscriptionUpdateIntervalOptions { get; } = new _003C_003Ez__ReadOnlyArray<KeyValuePair<int, string>>(new KeyValuePair<int, string>[5]
	{
		new KeyValuePair<int, string>(0, "Отключено"),
		new KeyValuePair<int, string>(30, "Каждые 30 минут"),
		new KeyValuePair<int, string>(60, "Каждый час"),
		new KeyValuePair<int, string>(360, "Каждые 6 часов"),
		new KeyValuePair<int, string>(1440, "Раз в день")
	});

	public IReadOnlyList<KeyValuePair<string, string>> PingModeOptions { get; } = new _003C_003Ez__ReadOnlyArray<KeyValuePair<string, string>>(new KeyValuePair<string, string>[4]
	{
		new KeyValuePair<string, string>("Tcp", "TCP"),
		new KeyValuePair<string, string>("ProxyGet", "Proxy GET"),
		new KeyValuePair<string, string>("ElyTurbo", "ElyTurbo"),
		new KeyValuePair<string, string>("ElyHard", "ElyHard")
	});

	public VlessNode? SelectedNode
	{
		get
		{
			return _selectedNode;
		}
		set
		{
			if (SetProperty(ref _selectedNode, value, "SelectedNode"))
			{
				CommandManager.InvalidateRequerySuggested();
			}
		}
	}

	public Subscription? SelectedSubscription
	{
		get
		{
			return _selectedSubscription;
		}
		set
		{
			if (SetProperty(ref _selectedSubscription, value, "SelectedSubscription"))
			{
				CommandManager.InvalidateRequerySuggested();
			}
		}
	}

	public ProxyProfile? SelectedProfile
	{
		get
		{
			return _selectedProfile;
		}
		set
		{
			if (SetProperty(ref _selectedProfile, value, "SelectedProfile"))
			{
				RefreshProfileNodes();
				CommandManager.InvalidateRequerySuggested();
			}
		}
	}

	public VlessNode? SelectedProfileNode
	{
		get
		{
			return _selectedProfileNode;
		}
		set
		{
			if (SetProperty(ref _selectedProfileNode, value, "SelectedProfileNode"))
			{
				CommandManager.InvalidateRequerySuggested();
			}
		}
	}

	public RunningApplication? SelectedApplicationBypass
	{
		get => _selectedApplicationBypass;
		set
		{
			if (SetProperty(ref _selectedApplicationBypass, value, nameof(SelectedApplicationBypass)))
			{
				CommandManager.InvalidateRequerySuggested();
			}
		}
	}

	public string NewSubName
	{
		get
		{
			return _newSubName;
		}
		set
		{
			if (SetProperty(ref _newSubName, value, "NewSubName"))
			{
				CommandManager.InvalidateRequerySuggested();
			}
		}
	}

	public string NewSubUrl
	{
		get
		{
			return _newSubUrl;
		}
		set
		{
			if (SetProperty(ref _newSubUrl, value, "NewSubUrl"))
			{
				CommandManager.InvalidateRequerySuggested();
			}
		}
	}

	public string ManualServerUri
	{
		get
		{
			return _manualServerUri;
		}
		set
		{
			if (SetProperty(ref _manualServerUri, value, "ManualServerUri"))
			{
				CommandManager.InvalidateRequerySuggested();
			}
		}
	}

	public string NewProfileName
	{
		get
		{
			return _newProfileName;
		}
		set
		{
			if (SetProperty(ref _newProfileName, value, "NewProfileName"))
			{
				CommandManager.InvalidateRequerySuggested();
			}
		}
	}

	public string StatusText
	{
		get
		{
			return _statusText;
		}
		set
		{
			SetProperty(ref _statusText, value, "StatusText");
		}
	}

	public string ConnectionInfo
	{
		get
		{
			return _connectionInfo;
		}
		set
		{
			SetProperty(ref _connectionInfo, value, "ConnectionInfo");
		}
	}

	public string ConnectedNodeName
	{
		get
		{
			return _connectedNodeName;
		}
		set
		{
			SetProperty(ref _connectedNodeName, value, "ConnectedNodeName");
		}
	}

	public string LogText
	{
		get
		{
			return _logText;
		}
		set
		{
			SetProperty(ref _logText, value, "LogText");
		}
	}

	public string PingSummary
	{
		get
		{
			return _pingSummary;
		}
		set
		{
			if (SetProperty(ref _pingSummary, value, "PingSummary"))
			{
				OnPropertyChanged("HasPingSummary");
			}
		}
	}

	public bool HasPingSummary => !string.IsNullOrWhiteSpace(PingSummary);

	public bool HasDiagnosticsReport => !string.IsNullOrWhiteSpace(DiagnosticsReport);

	public string DiagnosticsReport
	{
		get => _diagnosticsReport;
		private set
		{
			if (SetProperty(ref _diagnosticsReport, value, "DiagnosticsReport"))
			{
				OnPropertyChanged("HasDiagnosticsReport");
			}
		}
	}

	public bool IsConnected
	{
		get
		{
			return _isConnected;
		}
		set
		{
			if (SetProperty(ref _isConnected, value, "IsConnected"))
			{
				CommandManager.InvalidateRequerySuggested();
			}
		}
	}

	public bool IsLoading
	{
		get
		{
			return _isLoading;
		}
		set
		{
			if (SetProperty(ref _isLoading, value, "IsLoading"))
			{
				CommandManager.InvalidateRequerySuggested();
			}
		}
	}

	public int SocksPort
	{
		get
		{
			return _socksPort;
		}
		set
		{
			bool flag = ((value < 1 || value > 65535) ? true : false);
			if (!flag && SetProperty(ref _socksPort, value, "SocksPort"))
			{
				if (ProxyProEnabled && IsConnected && !_isLoadingSettings)
				{
					RestartActiveXrayAsync();
				}
				else if (SystemProxyEnabled && IsConnected && !_isLoadingSettings)
				{
					RestartSystemProxyAsync();
				}
				else if (!_isLoadingSettings)
				{
					SaveSettingsAsync();
				}
			}
		}
	}

	public bool AutoStartWithWindows
	{
		get
		{
			return _autoStartWithWindows;
		}
		set
		{
			if (SetProperty(ref _autoStartWithWindows, value, "AutoStartWithWindows") && !_isLoadingSettings)
			{
				ApplyAutoStartSetting(value);
				SaveSettingsAsync();
			}
		}
	}

	public bool AutoConnect
	{
		get
		{
			return _autoConnect;
		}
		set
		{
			if (SetProperty(ref _autoConnect, value, "AutoConnect") && !_isLoadingSettings)
			{
				SaveSettingsAsync();
			}
		}
	}

	public bool AutoReconnect
	{
		get
		{
			return _autoReconnect;
		}
		set
		{
			if (SetProperty(ref _autoReconnect, value, "AutoReconnect") && !_isLoadingSettings)
			{
				SaveSettingsAsync();
			}
		}
	}

	public bool ShowLogs
	{
		get
		{
			return _showLogs;
		}
		set
		{
			if (SetProperty(ref _showLogs, value, "ShowLogs") && !_isLoadingSettings)
			{
				SaveSettingsAsync();
			}
		}
	}

	public string ApplicationVersion => "Версия " + UpdateService.CurrentVersion.ToString(3);

	public string UpdateStatus
	{
		get
		{
			return _updateStatus;
		}
		private set
		{
			SetProperty(ref _updateStatus, value, "UpdateStatus");
		}
	}

	public string UpdateBadgeText
	{
		get
		{
			return _updateBadgeText;
		}
		private set
		{
			SetProperty(ref _updateBadgeText, value, "UpdateBadgeText");
		}
	}

	public Brush UpdateBadgeBrush
	{
		get
		{
			return _updateBadgeBrush;
		}
		private set
		{
			SetProperty(ref _updateBadgeBrush, value, "UpdateBadgeBrush");
		}
	}

	public bool IsCheckingForUpdates
	{
		get
		{
			return _isCheckingForUpdates;
		}
		private set
		{
			if (SetProperty(ref _isCheckingForUpdates, value, "IsCheckingForUpdates"))
			{
				CommandManager.InvalidateRequerySuggested();
			}
		}
	}

	public bool AutoUpdate
	{
		get
		{
			return _autoUpdate;
		}
		set
		{
			if (SetProperty(ref _autoUpdate, value, "AutoUpdate"))
			{
				OnPropertyChanged("AutoUpdateDescription");
				if (!_isLoadingSettings)
				{
					SaveSettingsAsync();
				}
			}
		}
	}

	public string AutoUpdateDescription
	{
		get
		{
			if (!AutoUpdate)
			{
				return "Выключено: программа всегда спрашивает перед загрузкой и установкой обновления.";
			}
			return "Включено: новые версии и срочные исправления устанавливаются автоматически после проверки SHA-256.";
		}
	}

	public string PingMode
	{
		get
		{
			return _pingMode;
		}
		set
		{
			string normalized = NormalizePingMode(value);
			if (SetProperty(ref _pingMode, normalized, "PingMode"))
			{
				OnPropertyChanged("IsProxyGetPingMode");
				OnPropertyChanged("IsProxyBasedPingMode");
				if (!_isLoadingSettings)
				{
					SaveSettingsAsync();
				}
			}
		}
	}

	public bool IsProxyGetPingMode => PingMode == "ProxyGet";

	public bool IsProxyBasedPingMode
	{
		get
		{
			switch (PingMode)
			{
			case "ProxyGet":
			case "ElyTurbo":
			case "ElyHard":
				return true;
			default:
				return false;
			}
		}
	}

	public string ProxyPingUrl
	{
		get
		{
			return _proxyPingUrl;
		}
		set
		{
			string normalized = NormalizeProxyPingUrl(value);
			if (SetProperty(ref _proxyPingUrl, normalized, "ProxyPingUrl") && !_isLoadingSettings)
			{
				SaveSettingsAsync();
			}
		}
	}

	public int ProxyPingConcurrency
	{
		get
		{
			return _proxyPingConcurrency;
		}
		set
		{
			int normalized = Math.Clamp(value, 1, 32);
			if (SetProperty(ref _proxyPingConcurrency, normalized, "ProxyPingConcurrency") && !_isLoadingSettings)
			{
				SaveSettingsAsync();
			}
		}
	}

	public int SubscriptionUpdateIntervalMinutes
	{
		get
		{
			return _subscriptionUpdateIntervalMinutes;
		}
		set
		{
			if (SetProperty(ref _subscriptionUpdateIntervalMinutes, value, "SubscriptionUpdateIntervalMinutes"))
			{
				ConfigureSubscriptionUpdateTimer();
				if (!_isLoadingSettings)
				{
					SaveSettingsAsync();
				}
			}
		}
	}

	public bool SystemProxyEnabled
	{
		get
		{
			return _systemProxyEnabled;
		}
		set
		{
			if (SetProperty(ref _systemProxyEnabled, value, "SystemProxyEnabled") && !_isLoadingSettings)
			{
				ApplySystemProxySettingAsync(value);
			}
		}
	}

	public bool ProxyProEnabled
	{
		get
		{
			return _proxyProEnabled;
		}
		set
		{
			if (SetProperty(ref _proxyProEnabled, value, "ProxyProEnabled") && !_isLoadingSettings)
			{
				ApplyProxyProSettingAsync(value);
			}
		}
	}

	public bool ElyTunEnabled
	{
		get
		{
			return _elyTunEnabled;
		}
		set
		{
			if (value && !_isLoadingSettings && !ElyTunElevationService.IsRunningAsAdministrator())
			{
				OnPropertyChanged("ElyTunEnabled");
				if (RequestElyTunCommand.CanExecute(null))
				{
					RequestElyTunCommand.Execute(null);
				}
				return;
			}

			if (SetProperty(ref _elyTunEnabled, value, "ElyTunEnabled") && !_isLoadingSettings)
			{
				ApplyElyTunSettingAsync(value);
			}
		}
	}

	public bool ElyTunIpv6Enabled
	{
		get
		{
			return _elyTunIpv6Enabled;
		}
		set
		{
			if (SetProperty(ref _elyTunIpv6Enabled, value, "ElyTunIpv6Enabled") && !_isLoadingSettings)
			{
				ApplyTunRuntimeSetting();
			}
		}
	}

	public RoutingPreset RoutingPreset
	{
		get
		{
			return _routingPreset;
		}
		set
		{
			if (SetProperty(ref _routingPreset, value, "RoutingPreset"))
			{
				OnPropertyChanged("UseCustomRouting");
				OnPropertyChanged("UseRoscomVpnDefaultRouting");
				if (!_isLoadingSettings)
				{
					ApplyRoutingPresetAsync();
				}
			}
		}
	}

	public bool UseCustomRouting
	{
		get
		{
			return RoutingPreset == RoutingPreset.Custom;
		}
		set
		{
			if (value)
			{
				RoutingPreset = RoutingPreset.Custom;
			}
		}
	}

	public bool UseRoscomVpnDefaultRouting
	{
		get
		{
			return RoutingPreset == RoutingPreset.RoscomVpnDefault;
		}
		set
		{
			if (value)
			{
				RoutingPreset = RoutingPreset.RoscomVpnDefault;
			}
		}
	}

	public int PacPort
	{
		get
		{
			return _pacPort;
		}
		set
		{
			if (SetProperty(ref _pacPort, value, "PacPort"))
			{
				OnPropertyChanged("PacUrl");
				if (SystemProxyEnabled && IsConnected && !_isLoadingSettings)
				{
					RestartSystemProxyAsync();
				}
				else if (!_isLoadingSettings)
				{
					SaveSettingsAsync();
				}
			}
		}
	}

	public string PacUrl => $"http://127.0.0.1:{PacPort}/proxy.pac";

	public string SystemProxyRulesText
	{
		get
		{
			return _systemProxyRulesText;
		}
		set
		{
			if (SetProperty(ref _systemProxyRulesText, value, "SystemProxyRulesText") && !_isLoadingSettings)
			{
				if ((ElyTunEnabled || ProxyProEnabled) && RoutingPreset == RoutingPreset.Custom && IsConnected)
				{
					RestartActiveXrayAsync();
				}
				else if (SystemProxyEnabled && IsConnected)
				{
					StartSystemProxyAsync();
				}
				SaveSettingsAsync();
			}
		}
	}

	public ICommand AddSubscriptionCommand { get; }

	public ICommand RemoveSubscriptionCommand { get; }

	public ICommand UpdateSubscriptionCommand { get; }

	public ICommand UpdateAllSubscriptionsCommand { get; }

	public ICommand ConnectCommand { get; }

	public ICommand DisconnectCommand { get; }

	public ICommand ConnectFromProfileCommand { get; }

	public ICommand PingAllCommand { get; }

	public ICommand RunConnectionDiagnosticsCommand { get; }

	public ICommand CopyDiagnosticsCommand { get; }

	public ICommand CheckRuntimeComponentsCommand { get; }

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

	public ICommand ResetSystemProxyRulesCommand { get; }

	public ICommand CheckForUpdatesCommand { get; }

	public ICommand RequestElyTunCommand { get; }

	public ICommand AddApplicationBypassCommand { get; }

	public ICommand RemoveApplicationBypassCommand { get; }

	public MainViewModel(bool launchElyTunOnStartup = false, int? handoffParentProcessId = null)
	{
		//IL_02f1: Unknown result type (might be due to invalid IL or missing references)
		//IL_02fb: Expected O, but got Unknown
		//IL_031a: Unknown result type (might be due to invalid IL or missing references)
		//IL_031f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0338: Expected O, but got Unknown
		_launchElyTunOnStartup = launchElyTunOnStartup;
		_handoffParentProcessId = handoffParentProcessId;
		_dispatcher = ((DispatcherObject)Application.Current).Dispatcher;
		_subscriptionService = new SubscriptionService();
		_parserService = new ParserService();
		_subscriptionRefreshCoordinator = new SubscriptionRefreshCoordinator(_subscriptionService, _parserService);
		_storageService = new StorageService();
		_importExportService = new ImportExportService();
		_runningApplicationService = new RunningApplicationService();
		_autoStartService = new AutoStartService();
		_pacServerService = new PacServerService();
		_windowsProxyService = new WindowsProxyService();
		_windowsProxyRecoveryService = new WindowsProxyRecoveryService(_windowsProxyService, _storageService);
		_connectionDiagnosticsService = new ConnectionDiagnosticsService(_windowsProxyService);
		_elyHardService = new ElyHardService();
		_xrayManager = new XrayManager();
		_singBoxTunManager = new SingBoxTunManager();
		_singBoxProxyManager = new SingBoxProxyManager();
		_updateService = new UpdateService();
		_xrayManager.LogReceived += OnLogReceived;
		_xrayManager.StatusChanged += OnXrayStatusChanged;
		_singBoxTunManager.LogReceived += OnLogReceived;
		_singBoxTunManager.StatusChanged += OnSingBoxTunStatusChanged;
		_singBoxProxyManager.LogReceived += OnLogReceived;
		_singBoxProxyManager.StatusChanged += OnSingBoxProxyStatusChanged;
		_subscriptionUpdateTimer = new DispatcherTimer((DispatcherPriority)4, _dispatcher);
		_subscriptionUpdateTimer.Tick += OnSubscriptionUpdateTimerTick;
		_logFlushTimer = new DispatcherTimer((DispatcherPriority)4, _dispatcher)
		{
			Interval = TimeSpan.FromMilliseconds(250.0)
		};
		_logFlushTimer.Tick += OnLogFlushTimerTick;
		_logFlushTimer.Start();
		AllNodes = new ObservableCollection<VlessNode>();
		Subscriptions = new ObservableCollection<Subscription>();
		Profiles = new ObservableCollection<ProxyProfile>();
		ProfileNodes = new ObservableCollection<VlessNode>();
		ApplicationBypassApplications = new ObservableCollection<RunningApplication>();
		AddSubscriptionCommand = new AsyncRelayCommand(() => RunSubscriptionMutationAsync(AddSubscriptionAsync), () => !IsLoading && !string.IsNullOrWhiteSpace(NewSubUrl));
		RemoveSubscriptionCommand = new AsyncRelayCommand(() => RunSubscriptionMutationAsync(RemoveSubscriptionAsync), () => SelectedSubscription != null && !IsLoading);
		UpdateSubscriptionCommand = new AsyncRelayCommand(() => RunSubscriptionMutationAsync(UpdateSelectedSubscriptionAsync), () => SelectedSubscription != null && !IsLoading);
		UpdateAllSubscriptionsCommand = new AsyncRelayCommand(() => RunSubscriptionMutationAsync(UpdateAllSubscriptionsAsync), () => Subscriptions.Count > 0 && !IsLoading);
		ConnectCommand = new AsyncRelayCommand(ConnectAsync, () => SelectedNode != null && !IsLoading);
		DisconnectCommand = new AsyncRelayCommand(DisconnectAsync, () => IsConnected && !IsLoading);
		ConnectFromProfileCommand = new AsyncRelayCommand(ConnectFromProfileAsync, () => SelectedProfileNode != null && !IsLoading);
		PingAllCommand = new AsyncRelayCommand(PingAllAsync, () => AllNodes.Count > 0 && !IsLoading);
		RunConnectionDiagnosticsCommand = new AsyncRelayCommand(RunConnectionDiagnosticsAsync, () => IsConnected && !IsLoading);
		CopyDiagnosticsCommand = new RelayCommand(CopyDiagnostics, () => HasDiagnosticsReport);
		CheckRuntimeComponentsCommand = new RelayCommand(CheckRuntimeComponents);
		AddManualServerCommand = new RelayCommand(AddManualServer, () => !string.IsNullOrWhiteSpace(ManualServerUri));
		AddToProfileCommand = new AsyncRelayCommand(() => RunProfileMutationAsync(AddToProfileAsync), () => SelectedNode != null && SelectedProfile != null);
		CreateProfileCommand = new AsyncRelayCommand(() => RunProfileMutationAsync(CreateProfileAsync), () => !string.IsNullOrWhiteSpace(NewProfileName));
		DeleteProfileCommand = new AsyncRelayCommand(() => RunProfileMutationAsync(DeleteProfileAsync), () => SelectedProfile != null);
		RemoveFromProfileCommand = new AsyncRelayCommand(() => RunProfileMutationAsync(RemoveFromProfileAsync), () => SelectedProfileNode != null && SelectedProfile != null);
		ClearProfileCommand = new AsyncRelayCommand(() => RunProfileMutationAsync(ClearProfileAsync), () => SelectedProfile != null && SelectedProfile.Nodes.Count > 0);
		ExportProfileCommand = new AsyncRelayCommand(ExportProfileAsync, () => SelectedProfile != null);
		ImportProfileCommand = new AsyncRelayCommand(() => RunProfileMutationAsync(ImportProfileAsync));
		ClearLogCommand = new RelayCommand(ClearLog);
		CopyProxyCommand = new RelayCommand(CopyProxy, () => IsConnected);
		OpenTelegramCommand = new RelayCommand((Action)delegate
		{
			OpenUrl("https://t.me/ProxyCheckXBot");
		}, (Func<bool>?)null);
		OpenDiscordCommand = new RelayCommand((Action)delegate
		{
			OpenUrl("https://discord.gg/sxjV3S7J2k");
		}, (Func<bool>?)null);
		ResetSystemProxyRulesCommand = new RelayCommand(ResetSystemProxyRules);
		CheckForUpdatesCommand = new AsyncRelayCommand(CheckForUpdatesFromCommandAsync, () => !IsCheckingForUpdates && !IsLoading);
		RequestElyTunCommand = new AsyncRelayCommand(RequestElyTunAsync, () => !IsLoading && !_isRelaunchingForElevation);
		AddApplicationBypassCommand = new AsyncRelayCommand(AddApplicationBypassAsync, () => !IsLoading);
		RemoveApplicationBypassCommand = new RelayCommand(RemoveSelectedApplicationBypass, () => SelectedApplicationBypass != null && !IsLoading);
		LoadAllDataAsync();
	}

	private async Task AddSubscriptionAsync()
	{
		if (!CanModifySubscriptions())
		{
			return;
		}

		if (string.IsNullOrWhiteSpace(NewSubUrl))
		{
			return;
		}
		Subscription sub = new Subscription
		{
			Name = (string.IsNullOrWhiteSpace(NewSubName) ? $"Подписка {Subscriptions.Count + 1}" : NewSubName.Trim()),
			Url = NewSubUrl.Trim()
		};
		IsLoading = true;
		StatusText = "Загрузка подписки...";
		try
		{
			SubscriptionRefreshResult refresh = await _subscriptionRefreshCoordinator.RefreshOneAsync(sub);
			if (!refresh.IsSuccess)
			{
				StatusText = "Не удалось добавить подписку";
				AppendLog("[warn] Подписка не добавлена: " + refresh.ErrorMessage);
				DialogService.Current.Notify("Не удалось добавить подписку", refresh.ErrorMessage ?? "Ответ не содержит VLESS-серверов.", NoticeKind.Error);
				return;
			}

			sub.Nodes = refresh.Nodes.ToList();
			sub.LastUpdated = DateTime.Now;
			List<Subscription> nextSubscriptions = Subscriptions.Append(sub).ToList();
			await _storageService.SaveSubscriptionsAsync(nextSubscriptions);
			_dispatcher.Invoke((Action)delegate { Subscriptions.Add(sub); });
			RefreshAllNodes();
			StatusText = $"Подписка добавлена: {sub.Nodes.Count} серверов";
			AppendLog($"[sys] Подписка «{sub.Name}» добавлена, серверов: {sub.Nodes.Count}");
			NewSubName = string.Empty;
			NewSubUrl = string.Empty;
		}
		catch (Exception ex)
		{
			StatusText = "Ошибка загрузки подписки";
			AppendLog("[err] Не удалось сохранить подписку: " + ex.Message);
			DialogService.Current.Notify("Не удалось сохранить подписку", ex.Message, NoticeKind.Error);
		}
		finally
		{
			IsLoading = false;
		}
	}

	private async Task RemoveSubscriptionAsync()
	{
		if (!CanModifySubscriptions())
		{
			return;
		}

		if (SelectedSubscription != null)
		{
			Subscription selected = SelectedSubscription;
			string name = selected.Name;
			try
			{
				await _storageService.SaveSubscriptionsAsync(Subscriptions.Where(subscription => subscription != selected));
				Subscriptions.Remove(selected);
				SelectedSubscription = null;
				RefreshAllNodes();
				AppendLog("[sys] Подписка «" + name + "» удалена");
			}
			catch (Exception ex)
			{
				AppendLog("[err] Не удалось удалить подписку из хранилища: " + ex.Message);
				DialogService.Current.Notify("Подписка не удалена", ex.Message, NoticeKind.Error);
			}
		}
	}

	private async Task UpdateSelectedSubscriptionAsync()
	{
		if (SelectedSubscription != null)
		{
			await UpdateSubscriptionAsync(SelectedSubscription);
		}
	}

	private async Task UpdateAllSubscriptionsAsync()
	{
		if (!CanModifySubscriptions())
		{
			return;
		}

		IsLoading = true;
		StatusText = "Обновление подписок...";
		SubscriptionRefreshBatchResult refreshBatch = await _subscriptionRefreshCoordinator.RefreshAllAsync(Subscriptions);
		List<Subscription> pending = refreshBatch.Subscriptions.ToList();
		foreach (Subscription sub in pending.Where(subscription => !string.IsNullOrWhiteSpace(subscription.LastUpdateError)))
		{
			AppendLog("[warn] Подписка «" + sub.Name + "» не обновлена; прежние серверы сохранены. " + sub.LastUpdateError);
		}
		try
		{
			await _storageService.SaveSubscriptionsAsync(pending);
			for (int index = 0; index < pending.Count; index++)
			{
				ApplySubscriptionSnapshot(Subscriptions[index], pending[index]);
			}

			RefreshAllNodes();
			int total = pending.Sum(subscription => subscription.Nodes.Count);
			StatusText = refreshBatch.FailedCount == 0 ? $"Обновлено: {total} серверов" : $"Подписок обновлено: {refreshBatch.UpdatedCount}, с ошибкой: {refreshBatch.FailedCount}";
			AppendLog($"[sys] Обновление подписок завершено: успешно {refreshBatch.UpdatedCount}, с ошибкой {refreshBatch.FailedCount}, серверов сохранено {total}");
		}
		catch (Exception ex)
		{
			StatusText = "Не удалось сохранить подписки";
			AppendLog("[err] Обновлённые подписки не сохранены: " + ex.Message);
			DialogService.Current.Notify("Не удалось сохранить подписки", ex.Message, NoticeKind.Error);
		}
		finally
		{
			IsLoading = false;
		}
	}

	private async Task UpdateSubscriptionAsync(Subscription sub)
	{
		if (!CanModifySubscriptions())
		{
			return;
		}

		IsLoading = true;
		StatusText = "Обновление «" + sub.Name + "»...";
		try
		{
			SubscriptionRefreshResult refresh = await _subscriptionRefreshCoordinator.RefreshOneAsync(sub);
			Subscription updated = CloneSubscription(sub);
			if (refresh.Status == SubscriptionRefreshStatus.Empty)
			{
				bool clear = await DialogService.Current.ConfirmAsync(
					"Подписка не содержит серверов",
					$"В ответе «{sub.Name}» не найдено ни одного корректного VLESS-сервера. Очистить сохранённый список из {sub.Nodes.Count} серверов?",
					"Очистить список",
					"Оставить прежние");
				if (!clear)
				{
					updated.LastUpdateError = refresh.ErrorMessage;
					await SaveSubscriptionSnapshotAsync(sub, updated);
					StatusText = "Прежние серверы подписки сохранены";
					return;
				}

				updated.Nodes.Clear();
			}
			else if (!refresh.IsSuccess)
			{
				updated.LastUpdateError = refresh.ErrorMessage;
				await SaveSubscriptionSnapshotAsync(sub, updated);
				StatusText = "Ошибка обновления — прежние серверы сохранены";
				AppendLog("[warn] Подписка «" + sub.Name + "» не обновлена; прежние серверы сохранены. " + refresh.ErrorMessage);
				DialogService.Current.Notify("Не удалось обновить подписку", refresh.ErrorMessage ?? "Прежний список серверов сохранён.", NoticeKind.Error);
				return;
			}

			if (refresh.IsSuccess)
			{
				updated.Nodes = refresh.Nodes.ToList();
			}
			updated.LastUpdated = DateTime.Now;
			updated.LastUpdateError = null;
			await SaveSubscriptionSnapshotAsync(sub, updated);
			RefreshAllNodes();
			StatusText = $"«{sub.Name}»: {updated.Nodes.Count} серверов";
			AppendLog($"[sys] Подписка «{sub.Name}» обновлена, серверов: {updated.Nodes.Count}");
			DialogService.Current.Notify("Подписка обновлена", $"«{sub.Name}»: {updated.Nodes.Count} серверов", NoticeKind.Success);
		}
		catch (Exception ex)
		{
			StatusText = "Ошибка обновления";
			AppendLog("[err] " + ex.Message);
			DialogService.Current.Notify("Не удалось обновить подписку", ex.Message, NoticeKind.Error);
		}
		finally
		{
			IsLoading = false;
		}
	}

	private Task ConnectAsync()
	{
		return ConnectToNodeAsync(SelectedNode);
	}

	private Task ConnectFromProfileAsync()
	{
		return ConnectToNodeAsync(SelectedProfileNode);
	}

	private async Task ConnectToNodeAsync(VlessNode? node)
	{
		if (node == null)
		{
			return;
		}

		Interlocked.Increment(ref _connectionGeneration);
		await _coreLifecycleLock.WaitAsync();
		try
		{
			if (IsConnected && _connectedNode != null
				&& string.Equals(GetNodeKey(_connectedNode), GetNodeKey(node), StringComparison.Ordinal)
				&& IsActiveCoreRunning())
			{
				return;
			}

			await ConnectToNodeCoreAsync(node);
		}
		finally
		{
			_coreLifecycleLock.Release();
		}
	}

	private async Task SaveSubscriptionSnapshotAsync(Subscription target, Subscription snapshot)
	{
		List<Subscription> pending = Subscriptions
			.Select(subscription => ReferenceEquals(subscription, target) ? snapshot : CloneSubscription(subscription))
			.ToList();
		await _storageService.SaveSubscriptionsAsync(pending);
		ApplySubscriptionSnapshot(target, snapshot);
	}

	private async Task RunSubscriptionMutationAsync(Func<Task> mutation)
	{
		await _subscriptionMutationLock.WaitAsync();
		try
		{
			await mutation();
		}
		finally
		{
			_subscriptionMutationLock.Release();
		}
	}

	private async Task RunProfileMutationAsync(Func<Task> mutation)
	{
		await _profileMutationLock.WaitAsync();
		try
		{
			await mutation();
		}
		finally
		{
			_profileMutationLock.Release();
		}
	}

	private bool CanModifySubscriptions()
	{
		if (!_dataReady)
		{
			StatusText = "Дождитесь загрузки данных";
			return false;
		}

		if (_canSaveSubscriptions)
		{
			return true;
		}

		StatusText = "Подписки доступны только для чтения";
		if (!_subscriptionsRecoveryWarningShown)
		{
			_subscriptionsRecoveryWarningShown = true;
			const string message = "Файл подписок повреждён и резервную копию найти не удалось. Исходный файл сохранён; добавление, обновление и удаление временно отключены, чтобы не потерять данные.";
			AppendLog("[err] " + message);
			DialogService.Current.Notify("Подписки доступны только для чтения", message, NoticeKind.Error);
		}

		return false;
	}

	private bool CanModifyProfiles()
	{
		if (!_dataReady)
		{
			StatusText = "Дождитесь загрузки данных";
			return false;
		}

		if (_canSaveProfiles)
		{
			return true;
		}

		StatusText = "Профили доступны только для чтения";
		if (!_profilesSaveErrorReported)
		{
			_profilesSaveErrorReported = true;
			const string message = "Файл профилей повреждён и резервную копию найти не удалось. Исходный файл сохранён; изменения профилей временно отключены, чтобы не потерять данные.";
			AppendLog("[err] " + message);
			DialogService.Current.Notify("Профили доступны только для чтения", message, NoticeKind.Error);
		}

		return false;
	}

	private async Task<bool> SaveProfilesAsync(IEnumerable<ProxyProfile> profiles)
	{
		if (!CanModifyProfiles())
		{
			return false;
		}

		try
		{
			await _storageService.SaveProfilesAsync(profiles.ToList());
			_profilesSaveErrorReported = false;
			return true;
		}
		catch (Exception ex)
		{
			StatusText = "Не удалось сохранить профили";
			if (!_profilesSaveErrorReported)
			{
				_profilesSaveErrorReported = true;
				AppendLog("[err] Профили не сохранены: " + ex.Message);
				DialogService.Current.Notify("Не удалось сохранить профили", ex.Message, NoticeKind.Error);
			}
			return false;
		}
	}

	private static List<ProxyProfile> CloneProfiles(IEnumerable<ProxyProfile> profiles) => profiles
		.Select(profile => new ProxyProfile
		{
			Id = profile.Id,
			Name = profile.Name,
			Nodes = profile.Nodes.ToList()
		})
		.ToList();

	private static bool SameProfileNode(VlessNode left, VlessNode right) =>
		left.Address == right.Address
		&& left.Port == right.Port
		&& left.UUID == right.UUID
		&& left.Network == right.Network;

	private static Subscription CloneSubscription(Subscription source) => new()
	{
		Id = source.Id,
		Name = source.Name,
		Url = source.Url,
		LastUpdated = source.LastUpdated,
		Nodes = source.Nodes.ToList(),
		LastUpdateError = source.LastUpdateError
	};

	private static void ApplySubscriptionSnapshot(Subscription target, Subscription snapshot)
	{
		target.Nodes = snapshot.Nodes.ToList();
		target.LastUpdated = snapshot.LastUpdated;
		target.LastUpdateError = snapshot.LastUpdateError;
	}

	private async Task ConnectToNodeCoreAsync(VlessNode? node)
	{
		if (node == null)
		{
			return;
		}
		Interlocked.Increment(ref _coreLifecycleTransition);
		IsLoading = true;
		StatusText = "Подключение...";
		bool stoppedExistingConnection = false;
		try
		{
			if (!ElyTunEnabled && !ProxyProEnabled)
			{
				string xrayPath = _xrayManager.GetXrayPath();
				if (!File.Exists(xrayPath))
				{
					StatusText = "Xray не найден!";
					AppendLog("[err] Xray не найден: " + xrayPath);
					_ = DialogService.Current.AlertAsync("Xray не найден", "Xray не найден по пути:\n" + xrayPath + "\n\nСкачайте Xray-core с:\ngithub.com/XTLS/Xray-core/releases\n\nПоместите xray.exe в папку xray/ рядом с программой.");
					return;
				}
			}
			if (IsConnected)
			{
				DeactivateSystemProxyForDisconnectedServer(silent: true);
				DeactivateProxyPro(silent: true);
				await StopAllCoresAsync();
				stoppedExistingConnection = true;
				IsConnected = false;
			}
			if ((!ElyTunEnabled || EnsureElyTunCanRun(showMessage: true)) && (!ProxyProEnabled || EnsureProxyProCanRun(showMessage: true)) && EnsureSocksPortAvailableForStart())
			{
				await StartSelectedCoreAsync(node);
				IsConnected = true;
				_connectedNode = node;
				_autoReconnectPolicy.Reset();
				ConnectedNodeName = node.DisplayName;
				UpdateConnectionInfo(node);
				RememberConnectedNode(node);
				if (SystemProxyEnabled)
				{
					await TryActivateSystemProxyAsync();
				}
				if (ProxyProEnabled)
				{
					ActivateProxyPro();
				}
				if (ElyTunEnabled)
				{
					VerifyElyTunConnectivityAsync();
				}
			}
			else if (stoppedExistingConnection)
			{
				_connectedNode = null;
				ConnectionInfo = string.Empty;
				ConnectedNodeName = string.Empty;
			}
		}
		catch (Exception ex)
		{
			if (stoppedExistingConnection)
			{
				IsConnected = false;
				_connectedNode = null;
				ConnectionInfo = string.Empty;
				ConnectedNodeName = string.Empty;
			}
			StatusText = "Ошибка подключения";
			AppendLog("[err] " + ex.Message);
		}
		finally
		{
			IsLoading = false;
			Interlocked.Decrement(ref _coreLifecycleTransition);
		}
	}

	private async Task DisconnectAsync()
	{
		Interlocked.Increment(ref _connectionGeneration);
		await _coreLifecycleLock.WaitAsync();
		try
		{
			await DisconnectCoreAsync();
		}
		finally
		{
			_coreLifecycleLock.Release();
		}
	}

	private async Task DisconnectCoreAsync()
	{
		Interlocked.Increment(ref _coreLifecycleTransition);
		IsLoading = true;
		try
		{
			DeactivateSystemProxyForDisconnectedServer(silent: false);
			DeactivateProxyPro(silent: true);
			await StopAllCoresAsync();
			IsConnected = false;
			_connectedNode = null;
			ConnectionInfo = string.Empty;
			ConnectedNodeName = string.Empty;
			StatusText = "Отключено";
		}
		catch (Exception ex)
		{
			AppendLog("[err] Ошибка отключения: " + ex.Message);
		}
		finally
		{
			IsLoading = false;
			Interlocked.Decrement(ref _coreLifecycleTransition);
		}
	}

	private void CheckRuntimeComponents()
	{
		try
		{
			RuntimePreflightResult result = _runtimePreflightService.Check(
				_xrayManager.GetXrayPath(),
				_singBoxTunManager.GetSingBoxPath(),
				ElyTunElevationService.IsRunningAsAdministrator(),
				RoscomVpnDefaultRouting.GetMissingAssetPaths());
			string report = result.ToReport();
			AppendLog("[sys] Проверка компонентов: " + string.Join("; ", result.Checks.Select(check => check.Name + " — " + check.Details)));
			_ = DialogService.Current.AlertAsync("Проверка компонентов ElyProxy", report);
		}
		catch (Exception ex)
		{
			AppendLog("[err] Не удалось проверить компоненты: " + ex.Message);
			DialogService.Current.Notify("Не удалось проверить компоненты", ex.Message, NoticeKind.Error);
		}
	}

	private async Task RunConnectionDiagnosticsAsync()
	{
		IsLoading = true;
		try
		{
			string mode = ElyTunEnabled
				? "ElyTun"
				: ProxyProEnabled
					? "Proxy Pro"
					: SystemProxyEnabled
						? "Proxy Lite"
						: "SOCKS5";
			ConnectionDiagnosticsResult result = await _connectionDiagnosticsService.RunAsync(
				IsActiveCoreRunning(),
				SocksPort,
				mode,
				PacUrl,
				GetSystemProxyRules());
			DiagnosticsReport = result.ToSafeReport();
			foreach (ConnectionDiagnosticCheck check in result.Checks)
			{
				AppendLog($"[sys] Диагностика · {check.Name}: {check.Details}");
			}

			bool engineRunning = result.Checks.Any(check => check.Name == "Ядро" && check.Passed);
			StatusText = result.RoutePassed
				? "Внешний маршрут проверен"
				: engineRunning ? "Ядро запущено, проверьте маршрут" : "Ядро не запущено";
			DialogService.Current.Notify(
				result.RoutePassed ? "Проверка соединения пройдена" : "Найдена проблема с соединением",
				result.RoutePassed ? "Все проверки показаны в отчёте. Его можно скопировать." : "В отчёте есть результат каждой проверки и следующий шаг для исправления.",
				result.RoutePassed ? NoticeKind.Success : NoticeKind.Warning);
			_ = DialogService.Current.AlertAsync("Диагностика соединения", DiagnosticsReport);
		}
		catch (Exception ex)
		{
			StatusText = "Не удалось выполнить диагностику";
			AppendLog("[err] Диагностика соединения: " + ex.Message);
			DialogService.Current.Notify("Не удалось выполнить диагностику", ex.Message, NoticeKind.Error);
		}
		finally
		{
			IsLoading = false;
		}
	}

	private void CopyDiagnostics()
	{
		if (!HasDiagnosticsReport)
		{
			return;
		}

		try
		{
			Clipboard.SetText(DiagnosticsReport);
			DialogService.Current.Notify("Отчёт скопирован", "Секретные ссылки и идентификаторы в отчёт не включаются.", NoticeKind.Success);
		}
		catch (Exception ex)
		{
			DialogService.Current.Notify("Не удалось скопировать отчёт", ex.Message, NoticeKind.Error);
		}
	}

	private Task PingAllAsync()
	{
		return PingMode switch
		{
			"ProxyGet" => ProxyPingAllAsync(),
			"ElyTurbo" => ElyTurboAllAsync(),
			"ElyHard" => ElyHardAllAsync(),
			_ => TcpPingAllAsync(),
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
			List<VlessNode> nodes = AllNodes.ToList();
			int completed = 0;
			SemaphoreSlim semaphore = new SemaphoreSlim(64);
			try
			{
				await Task.WhenAll(((IEnumerable<VlessNode>)nodes).Select((Func<VlessNode, Task>)async delegate(VlessNode node)
				{
					await semaphore.WaitAsync();
					try
					{
						node.PingDetails = string.Empty;
						node.Latency = await MeasureTcpLatencyAsync(node.Address, node.Port);
						int current = Interlocked.Increment(ref completed);
						if (current % 5 == 0 || current == nodes.Count)
						{
							await _dispatcher.InvokeAsync<string>((Func<string>)(() => StatusText = $"TCP-пинг: {current}/{nodes.Count}"));
						}
					}
					finally
					{
						semaphore.Release();
					}
				}));
				SortNodesByLatency();
				int reachable = nodes.Count((VlessNode n) => n.Latency.HasValue);
				StatusText = $"TCP-пинг: {reachable}/{nodes.Count} доступно";
				AppendLog($"[sys] TCP-пинг завершён: {reachable}/{nodes.Count} доступно");
			}
			finally
			{
				if (semaphore != null)
				{
					((IDisposable)semaphore).Dispose();
				}
			}
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
		if (!TryGetProxyPingUri(out Uri pingUri))
		{
			StatusText = "Некорректный URL пинга";
			AppendLog("[err] Некорректный URL Proxy GET-пинга: " + ProxyPingUrl);
			IsLoading = false;
			return;
		}
		AppendLog($"[sys] Запуск Proxy GET-пинга: {pingUri}, потоков: {ProxyPingConcurrency}");
		List<VlessNode> nodes = AllNodes.ToList();
		int reachable = 0;
		int completed = 0;
		try
		{
			SemaphoreSlim semaphore = new SemaphoreSlim(ProxyPingConcurrency);
			try
			{
				await Task.WhenAll(((IEnumerable<VlessNode>)nodes).Select((Func<VlessNode, Task>)async delegate(VlessNode node)
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
						int current = Interlocked.Increment(ref completed);
						if (current % 3 == 0 || current == nodes.Count)
						{
							int ok = Volatile.Read(in reachable);
							await _dispatcher.InvokeAsync<string>((Func<string>)(() => StatusText = $"Proxy GET: {current}/{nodes.Count}, ок: {ok}"));
						}
					}
				}));
				SortNodesByLatency();
				StatusText = $"Proxy GET: {reachable}/{nodes.Count} доступно";
				AppendLog($"[sys] Proxy GET-пинг завершён: {reachable}/{nodes.Count} доступно");
			}
			finally
			{
				if (semaphore != null)
				{
					((IDisposable)semaphore).Dispose();
				}
			}
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
		List<VlessNode> nodes = AllNodes.ToList();
		ConcurrentBag<ElyTurboNodeResult> results = new ConcurrentBag<ElyTurboNodeResult>();
		int completed = 0;
		try
		{
			SemaphoreSlim semaphore = new SemaphoreSlim(ProxyPingConcurrency);
			try
			{
				await Task.WhenAll(((IEnumerable<VlessNode>)nodes).Select((Func<VlessNode, Task>)async delegate(VlessNode node)
				{
					await semaphore.WaitAsync();
					try
					{
						ElyTurboNodeResult result2 = await MeasureElyTurboAsync(node);
						results.Add(result2);
						node.Latency = result2.AverageLatencyMs;
						node.PingDetails = ((result2.SuccessCount > 0) ? $"{result2.SuccessCount}/{result2.TotalCount} · {result2.AverageLatencyMs} ms" : $"0/{result2.TotalCount}");
					}
					finally
					{
						semaphore.Release();
						int current = Interlocked.Increment(ref completed);
						if (current % 3 == 0 || current == nodes.Count)
						{
							ElyTurboNodeResult best = (from elyTurboNodeResult in results
								orderby elyTurboNodeResult.SuccessCount descending, elyTurboNodeResult.AverageLatencyMs ?? int.MaxValue
								select elyTurboNodeResult).FirstOrDefault();
							string bestText = ((best == null) ? string.Empty : $" · лучший: {best.Node.DisplayName} {best.SuccessCount}/{best.TotalCount}");
							await _dispatcher.InvokeAsync<string>((Func<string>)(() => StatusText = $"ElyTurbo: {current}/{nodes.Count}{bestText}"));
						}
					}
				}));
				List<ElyTurboNodeResult> ordered = (from elyTurboNodeResult in results
					orderby elyTurboNodeResult.SuccessCount descending, elyTurboNodeResult.AverageLatencyMs ?? int.MaxValue, elyTurboNodeResult.FastestLatencyMs ?? int.MaxValue
					select elyTurboNodeResult).ToList();
				_dispatcher.Invoke((Action)delegate
				{
					AllNodes.Clear();
					foreach (ElyTurboNodeResult current in ordered)
					{
						AllNodes.Add(current.Node);
					}
					SelectedNode = ordered.FirstOrDefault()?.Node;
				});
				ElyTurboNodeResult bestResult = ordered.FirstOrDefault();
				if (bestResult == null || bestResult.SuccessCount == 0)
				{
					StatusText = "ElyTurbo: рабочие серверы не найдены";
					PingSummary = "ElyTurbo: рабочие серверы не найдены";
					AppendLog("[sys] ElyTurbo завершён: рабочие серверы не найдены");
					return;
				}
				PingSummary = BuildElyTurboSummary(bestResult);
				StatusText = $"ElyTurbo: лучший {bestResult.SuccessCount}/{bestResult.TotalCount}, {bestResult.AverageLatencyMs} ms";
				AppendLog("[turbo] " + PingSummary);
				foreach (ElyTurboNodeResult result in ordered.Take(5))
				{
					AppendLog($"[turbo] {result.Node.DisplayName}: {result.SuccessCount}/{result.TotalCount}, {FormatNullableLatency(result.AverageLatencyMs)}");
				}
			}
			finally
			{
				if (semaphore != null)
				{
					((IDisposable)semaphore).Dispose();
				}
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
		List<VlessNode> nodes = AllNodes.ToList();
		ConcurrentBag<ElyHardService.ElyHardNodeResult> results = new ConcurrentBag<ElyHardService.ElyHardNodeResult>();
		int completed = 0;
		ElyHardService.ElyHardOptions options = new ElyHardService.ElyHardOptions(2, TimeSpan.FromSeconds(3.0), TimeSpan.FromSeconds(6.0));
		try
		{
			SemaphoreSlim semaphore = new SemaphoreSlim(ProxyPingConcurrency);
			try
			{
				await Task.WhenAll(((IEnumerable<VlessNode>)nodes).Select((Func<VlessNode, Task>)async delegate(VlessNode node)
				{
					await semaphore.WaitAsync();
					try
					{
						ElyHardService.ElyHardNodeResult result2 = await _elyHardService.CheckNodeAsync(node, options);
						results.Add(result2);
						node.Latency = result2.AverageLatencyMs;
						node.PingDetails = ((result2.ReachableServices > 0) ? $"{result2.StableServices}/{result2.TotalServices} · {FormatNullableLatency(result2.AverageLatencyMs)}" : $"0/{result2.TotalServices}");
					}
					finally
					{
						semaphore.Release();
						int current = Interlocked.Increment(ref completed);
						if (current % 2 == 0 || current == nodes.Count)
						{
							ElyHardService.ElyHardNodeResult best = OrderElyHardResults(results).FirstOrDefault();
							string bestText = ((best == null) ? string.Empty : $" · лучший: {best.Node.DisplayName} {best.StableServices}/{best.TotalServices}");
							await _dispatcher.InvokeAsync<string>((Func<string>)(() => StatusText = $"ElyHard: {current}/{nodes.Count}{bestText}"));
						}
					}
				}));
				List<ElyHardService.ElyHardNodeResult> ordered = OrderElyHardResults(results).ToList();
				_dispatcher.Invoke((Action)delegate
				{
					AllNodes.Clear();
					foreach (ElyHardService.ElyHardNodeResult current in ordered)
					{
						AllNodes.Add(current.Node);
					}
					SelectedNode = ordered.FirstOrDefault()?.Node;
				});
				ElyHardService.ElyHardNodeResult bestResult = ordered.FirstOrDefault();
				if (bestResult == null || bestResult.ReachableServices == 0)
				{
					StatusText = "ElyHard: рабочие серверы не найдены";
					PingSummary = "ElyHard: рабочие серверы не найдены";
					AppendLog("[sys] ElyHard завершён: рабочие серверы не найдены");
					return;
				}
				PingSummary = BuildElyHardSummary(bestResult);
				StatusText = $"ElyHard: лучший {bestResult.StableServices}/{bestResult.TotalServices}, {FormatNullableLatency(bestResult.AverageLatencyMs)}";
				AppendLog("[hard] " + PingSummary);
				foreach (ElyHardService.ElyHardNodeResult result in ordered.Take(5))
				{
					AppendLog($"[hard] {result.Node.DisplayName}: стабильно {result.StableServices}/{result.TotalServices}, попытки {result.TotalSuccessfulAttempts}/{result.TotalAttempts}, среднее {FormatNullableLatency(result.AverageLatencyMs)}");
				}
			}
			finally
			{
				if (semaphore != null)
				{
					((IDisposable)semaphore).Dispose();
				}
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
			using TcpClient client = new TcpClient();
			Stopwatch sw = Stopwatch.StartNew();
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
		int probePort = GetFreeTcpPort();
		using XrayManager probeXray = new XrayManager();
		int? result;
		try
		{
			_ = 2;
			try
			{
				await probeXray.StartAsync(node, probePort);
				if (!(await WaitForLocalPortAsync(probePort, ProxyPingStartupTimeout)))
				{
					result = null;
				}
				else
				{
					using HttpClientHandler handler = new HttpClientHandler
					{
						Proxy = new WebProxy($"socks5://127.0.0.1:{probePort}"),
						UseProxy = true
					};
					using HttpClient http = new HttpClient(handler)
					{
						Timeout = ProxyPingHttpTimeout
					};
					http.DefaultRequestHeaders.UserAgent.ParseAdd("ElyProxy/1.1");
					Stopwatch sw = Stopwatch.StartNew();
					using HttpResponseMessage response = await http.GetAsync(pingUri, HttpCompletionOption.ResponseHeadersRead);
					sw.Stop();
					int? obj;
					if (!response.IsSuccessStatusCode)
					{
						int statusCode = (int)response.StatusCode;
						if (statusCode < 300 || statusCode >= 500)
						{
							obj = null;
							goto IL_0283;
						}
					}
					obj = (int)sw.ElapsedMilliseconds;
					goto IL_0283;
					IL_0283:
					result = obj;
				}
			}
			catch
			{
				result = null;
			}
		}
		finally
		{
			await probeXray.StopAsync();
		}
		return result;
	}

	private async Task<ElyTurboNodeResult> MeasureElyTurboAsync(VlessNode node)
	{
		int probePort = GetFreeTcpPort();
		using XrayManager probeXray = new XrayManager();
		ElyTurboNodeResult result;
		try
		{
			_ = 2;
			try
			{
				await probeXray.StartAsync(node, probePort);
				if (!(await WaitForLocalPortAsync(probePort, ProxyPingStartupTimeout)))
				{
					result = ElyTurboNodeResult.Empty(node, ElyTurboTargets.Length);
				}
				else
				{
					using HttpClientHandler handler = new HttpClientHandler
					{
						Proxy = new WebProxy($"socks5://127.0.0.1:{probePort}"),
						UseProxy = true,
						AllowAutoRedirect = false
					};
					HttpClient http = new HttpClient(handler)
					{
						Timeout = ProxyPingHttpTimeout
					};
					try
					{
						http.DefaultRequestHeaders.UserAgent.ParseAdd("ElyProxy/1.1");
						result = new ElyTurboNodeResult(node, await Task.WhenAll(ElyTurboTargets.Select((ElyTurboTarget target) => MeasureElyTurboTargetAsync(http, target))));
					}
					finally
					{
						if (http != null)
						{
							((IDisposable)http).Dispose();
						}
					}
				}
			}
			catch
			{
				result = ElyTurboNodeResult.Empty(node, ElyTurboTargets.Length);
			}
		}
		finally
		{
			await probeXray.StopAsync();
		}
		return result;
	}

	private static async Task<ElyTurboServiceResult> MeasureElyTurboTargetAsync(HttpClient http, ElyTurboTarget target)
	{
		try
		{
			Stopwatch sw = Stopwatch.StartNew();
			using HttpResponseMessage response = await http.GetAsync(target.Url, HttpCompletionOption.ResponseHeadersRead);
			sw.Stop();
			int statusCode = (int)response.StatusCode;
			bool isReachable = statusCode >= 200 && statusCode < 500;
			return new ElyTurboServiceResult(target.Name, isReachable ? new int?((int)sw.ElapsedMilliseconds) : ((int?)null));
		}
		catch
		{
			return new ElyTurboServiceResult(target.Name, null);
		}
	}

	private void SortNodesByLatency()
	{
		_dispatcher.Invoke((Action)delegate
		{
			List<VlessNode> list = AllNodes.OrderBy((VlessNode n) => n.Latency ?? int.MaxValue).ToList();
			AllNodes.Clear();
			foreach (VlessNode current in list)
			{
				AllNodes.Add(current);
			}
		});
	}

	private static int GetFreeTcpPort()
	{
		TcpListener tcpListener = new TcpListener(IPAddress.Loopback, 0);
		tcpListener.Start();
		int port = ((IPEndPoint)tcpListener.LocalEndpoint).Port;
		tcpListener.Stop();
		return port;
	}

	private static async Task<bool> WaitForLocalPortAsync(int port, TimeSpan timeout)
	{
		DateTime deadline = DateTime.UtcNow + timeout;
		while (DateTime.UtcNow < deadline)
		{
			try
			{
				using TcpClient client = new TcpClient();
				Task connectTask = client.ConnectAsync(IPAddress.Loopback, port);
				if (await Task.WhenAny(connectTask, Task.Delay(250)) == connectTask && connectTask.IsCompletedSuccessfully)
				{
					return true;
				}
			}
			catch
			{
			}
			await Task.Delay(100);
		}
		return false;
	}

	private bool TryGetProxyPingUri(out Uri pingUri)
	{
		if (Uri.TryCreate(ProxyPingUrl, UriKind.Absolute, out Uri uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
		{
			pingUri = uri;
			return true;
		}
		pingUri = new Uri("https://youtube.com");
		return false;
	}

	private static string NormalizePingMode(string? value)
	{
		if (string.Equals(value, "ProxyGet", StringComparison.OrdinalIgnoreCase))
		{
			return "ProxyGet";
		}
		if (string.Equals(value, "ElyTurbo", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "Super" + "Ping", StringComparison.OrdinalIgnoreCase))
		{
			return "ElyTurbo";
		}
		if (string.Equals(value, "ElyHard", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "Ultra" + "Ping", StringComparison.OrdinalIgnoreCase))
		{
			return "ElyHard";
		}
		return "Tcp";
	}

	private static string NormalizeProxyPingUrl(string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return "https://youtube.com";
		}
		string trimmed = value.Trim();
		if (!string.Equals(trimmed, "http://cp.cloudflare.com/generate_204", StringComparison.OrdinalIgnoreCase))
		{
			return trimmed;
		}
		return "https://youtube.com";
	}

	private static string BuildElyTurboSummary(ElyTurboNodeResult result)
	{
		string services = string.Join(", ", result.Services.Select((ElyTurboServiceResult service) => service.Name + ": " + FormatNullableLatency(service.LatencyMs)));
		return $"Лучший: {result.Node.DisplayName} · сервисы {result.SuccessCount}/{result.TotalCount} · среднее {FormatNullableLatency(result.AverageLatencyMs)} · {services}";
	}

	private static IOrderedEnumerable<ElyHardService.ElyHardNodeResult> OrderElyHardResults(IEnumerable<ElyHardService.ElyHardNodeResult> results)
	{
		return from result in results
			orderby result.StableServices descending, result.ReachableServices descending, result.TotalSuccessfulAttempts descending, result.AverageLatencyMs ?? int.MaxValue, result.WorstLatencyMs ?? int.MaxValue
			select result;
	}

	private static string BuildElyHardSummary(ElyHardService.ElyHardNodeResult result)
	{
		string services = string.Join(", ", result.Services.Select((ElyHardService.ElyHardServiceResult service) => service.CompactDisplay));
		return $"ElyHard лучший: {result.Node.DisplayName} · стабильно {result.StableServices}/{result.TotalServices} · попытки {result.TotalSuccessfulAttempts}/{result.TotalAttempts} · среднее {FormatNullableLatency(result.AverageLatencyMs)} · худший {FormatNullableLatency(result.WorstLatencyMs)} · {services}";
	}

	private static string FormatNullableLatency(int? latency)
	{
		if (!latency.HasValue)
		{
			return "—";
		}
		return $"{latency.Value} ms";
	}

	private async Task AddApplicationBypassAsync()
	{
		ApplicationPickerSheet sheet = new ApplicationPickerSheet(
			_runningApplicationService,
			GetApplicationBypassPaths());
		if (!await DialogService.Current.ShowCustomAsync("Обход приложения", sheet, "Добавить"))
		{
			return;
		}

		RunningApplication? application = sheet.SelectedApplication;
		if (application == null || ApplicationBypassApplications.Any(existing =>
			string.Equals(existing.ExecutablePath, application.ExecutablePath, StringComparison.OrdinalIgnoreCase)))
		{
			return;
		}

		ApplicationBypassApplications.Add(application);
		await SaveSettingsAsync();
		ReportApplicationBypassChange($"Добавлено приложение для обхода: {application.Name}");
	}

	private void RemoveSelectedApplicationBypass()
	{
		if (SelectedApplicationBypass == null)
		{
			return;
		}

		string name = SelectedApplicationBypass.Name;
		ApplicationBypassApplications.Remove(SelectedApplicationBypass);
		SelectedApplicationBypass = null;
		SaveSettingsAsync();
		ReportApplicationBypassChange($"Удалено приложение из обхода: {name}");
	}

	private void ReportApplicationBypassChange(string logMessage)
	{
		AppendLog("[sys] " + logMessage);
		if (IsConnected && (ElyTunEnabled || ProxyProEnabled))
		{
			StatusText = "Список приложений изменён — переподключитесь, чтобы применить";
			DialogService.Current.Notify("Нужно переподключиться", "Отключите и снова подключите VPN, чтобы применить новые правила обхода приложений.", NoticeKind.Info);
		}
		else if (SystemProxyEnabled)
		{
			StatusText = "Обход по приложениям доступен в ElyTun и Proxy Pro";
		}
		else
		{
			StatusText = "Правило сохранено для ElyTun и Proxy Pro";
		}
	}

	private void AddManualServer()
	{
		if (string.IsNullOrWhiteSpace(ManualServerUri))
		{
			return;
		}
		VlessNode node = _parserService.ParseVlessUri(ManualServerUri.Trim());
		if (node == null)
		{
			StatusText = "Неверный формат vless://";
			return;
		}
		AllNodes.Add(node);
		if (!_manualNodes.Any((VlessNode n) => NodesMatch(n, node)))
		{
			_manualNodes.Add(node);
		}
		ManualServerUri = string.Empty;
		SaveSettingsAsync();
		AppendLog("[sys] Сервер добавлен: " + node.DisplayName);
		StatusText = "Сервер добавлен: " + node.DisplayName;
	}

	private async Task CreateProfileAsync()
	{
		if (string.IsNullOrWhiteSpace(NewProfileName) || !CanModifyProfiles())
		{
			return;
		}

		var profile = new ProxyProfile { Name = NewProfileName.Trim() };
		List<ProxyProfile> pending = CloneProfiles(Profiles);
		pending.Add(profile);
		if (!await SaveProfilesAsync(pending))
		{
			return;
		}

		Profiles.Add(profile);
		NewProfileName = string.Empty;
		SelectedProfile = profile;
		AppendLog("[sys] Профиль создан: " + profile.Name);
	}

	private async Task DeleteProfileAsync()
	{
		if (SelectedProfile == null || !CanModifyProfiles())
		{
			return;
		}

		ProxyProfile selected = SelectedProfile;
		List<ProxyProfile> pending = CloneProfiles(Profiles.Where(profile => profile.Id != selected.Id));
		if (!await SaveProfilesAsync(pending))
		{
			return;
		}

		Profiles.Remove(selected);
		SelectedProfile = null;
		ProfileNodes.Clear();
		AppendLog("[sys] Профиль удалён: " + selected.Name);
	}

	private async Task AddToProfileAsync()
	{
		if (SelectedNode == null || SelectedProfile == null || !CanModifyProfiles())
		{
			return;
		}

		if (SelectedProfile.Nodes.Any(node => node.Address == SelectedNode.Address && node.Port == SelectedNode.Port && node.UUID == SelectedNode.UUID))
		{
			StatusText = "Сервер уже в профиле";
			return;
		}

		List<ProxyProfile> pending = CloneProfiles(Profiles);
		ProxyProfile? staged = pending.FirstOrDefault(profile => profile.Id == SelectedProfile.Id);
		if (staged == null)
		{
			return;
		}
		staged.Nodes.Add(SelectedNode);
		if (!await SaveProfilesAsync(pending))
		{
			return;
		}

		SelectedProfile.Nodes.Add(SelectedNode);
		RefreshProfileNodes();
		StatusText = "Добавлен в «" + SelectedProfile.Name + "»";
	}

	private async Task RemoveFromProfileAsync()
	{
		if (SelectedProfile == null || SelectedProfileNode == null || !CanModifyProfiles())
		{
			return;
		}

		VlessNode selectedNode = SelectedProfileNode;
		List<ProxyProfile> pending = CloneProfiles(Profiles);
		ProxyProfile? staged = pending.FirstOrDefault(profile => profile.Id == SelectedProfile.Id);
		if (staged == null)
		{
			return;
		}
		staged.Nodes.RemoveAll(node => SameProfileNode(node, selectedNode));
		if (!await SaveProfilesAsync(pending))
		{
			return;
		}

		SelectedProfile.Nodes.RemoveAll(node => SameProfileNode(node, selectedNode));
		SelectedProfileNode = null;
		RefreshProfileNodes();
	}

	private async Task ClearProfileAsync()
	{
		if (SelectedProfile == null || !CanModifyProfiles())
		{
			return;
		}

		List<ProxyProfile> pending = CloneProfiles(Profiles);
		ProxyProfile? staged = pending.FirstOrDefault(profile => profile.Id == SelectedProfile.Id);
		if (staged == null)
		{
			return;
		}
		staged.Nodes.Clear();
		if (!await SaveProfilesAsync(pending))
		{
			return;
		}

		SelectedProfile.Nodes.Clear();
		RefreshProfileNodes();
		AppendLog("[sys] Профиль «" + SelectedProfile.Name + "» очищен");
	}

	private void RefreshProfileNodes()
	{
		ProfileNodes.Clear();
		if (SelectedProfile == null)
		{
			return;
		}
		foreach (VlessNode node in SelectedProfile.Nodes)
		{
			ProfileNodes.Add(node);
		}
	}

	private async Task ExportProfileAsync()
	{
		if (SelectedProfile != null)
		{
			SaveFileDialog dialog = new SaveFileDialog
			{
				FileName = SelectedProfile.Name + ".elyproxy",
				DefaultExt = ".elyproxy",
				Filter = "ElyProxy Profile|*.elyproxy"
			};
			if (dialog.ShowDialog() == true)
			{
				await _importExportService.ExportProfileAsync(SelectedProfile, dialog.FileName);
				AppendLog("[sys] Профиль экспортирован: " + dialog.FileName);
				StatusText = "Профиль экспортирован";
			}
		}
	}

	private async Task ImportProfileAsync()
	{
		if (!CanModifyProfiles())
		{
			return;
		}

		OpenFileDialog dialog = new OpenFileDialog
		{
			DefaultExt = ".elyproxy",
			Filter = "ElyProxy Profile|*.elyproxy|JSON|*.json"
		};
		if (dialog.ShowDialog() == true)
		{
			try
			{
				ProxyProfile profile = await _importExportService.ImportProfileAsync(dialog.FileName);
				List<ProxyProfile> pending = CloneProfiles(Profiles);
				pending.Add(profile);
				if (!await SaveProfilesAsync(pending))
				{
					return;
				}

				Profiles.Add(profile);
				SelectedProfile = profile;
				AppendLog($"[sys] Профиль импортирован: {profile.Name} ({profile.Nodes.Count} серверов)");
				StatusText = "Импортирован: " + profile.Name;
			}
			catch (Exception ex)
			{
				StatusText = "Ошибка импорта профиля";
				AppendLog("[err] Не удалось прочитать профиль: " + ex.Message);
				DialogService.Current.Notify("Не удалось импортировать профиль", ex.Message, NoticeKind.Error);
			}
		}
	}

	private void RefreshAllNodes()
	{
		if (_dispatcher.CheckAccess())
		{
			Refresh();
		}
		else
		{
			_dispatcher.Invoke((Action)Refresh);
		}
		void Refresh()
		{
			AllNodes.Clear();
			foreach (Subscription subscription in Subscriptions)
			{
				foreach (VlessNode node in subscription.Nodes)
				{
					AllNodes.Add(node);
				}
			}
			foreach (VlessNode node2 in _manualNodes)
			{
				if (!AllNodes.Any((VlessNode existing) => NodesMatch(existing, node2)))
				{
					AllNodes.Add(node2);
				}
			}
		}
	}

	private void ApplyAutoStartSetting(bool enabled)
	{
		try
		{
			_autoStartService.SetEnabled(enabled);
			StatusText = (enabled ? "Автозапуск включён" : "Автозапуск выключен");
			AppendLog(enabled ? "[sys] Автозапуск вместе с Windows включён" : "[sys] Автозапуск вместе с Windows выключен");
		}
		catch (Exception ex)
		{
			StatusText = "Ошибка настройки автозапуска";
			AppendLog("[err] Не удалось изменить автозапуск: " + ex.Message);
			try
			{
				bool actual = _autoStartService.IsEnabled();
				if (_autoStartWithWindows != actual)
				{
					_autoStartWithWindows = actual;
					OnPropertyChanged("AutoStartWithWindows");
				}
			}
			catch
			{
			}
		}
	}

	private void SelectLastConnectedNode()
	{
		if (!string.IsNullOrWhiteSpace(_lastConnectedNodeId))
		{
			SelectedNode = AllNodes.FirstOrDefault((VlessNode node) => GetNodeKey(node) == _lastConnectedNodeId);
		}
	}

	private void RememberConnectedNode(VlessNode node)
	{
		_lastConnectedNodeId = GetNodeKey(node);
		SaveSettingsAsync();
	}

	private void UpdateConnectionInfo(VlessNode node)
	{
		string prefix = (ElyTunEnabled ? $"ElyTun + socks5://127.0.0.1:{SocksPort}" : (ProxyProEnabled ? $"Proxy Pro (HTTP/SOCKS) 127.0.0.1:{SocksPort}" : $"socks5://127.0.0.1:{SocksPort}"));
		ConnectionInfo = prefix + "  →  " + node.DisplayName;
	}

	private XrayTunOptions? BuildElyTunOptionsOrNull()
	{
		if (!ElyTunEnabled)
		{
			return null;
		}
		return new XrayTunOptions(CreateElyTunInterfaceName(), 1500, GetElyTunDnsServers(), ElyTunIpv6Enabled, GetSystemProxyRules(), GetActiveAdapterNetworkCidrs(), RoutingPreset, GetApplicationBypassPaths());
	}

	private XrayTunOptions? BuildProxyProOptionsOrNull()
	{
		if (!ProxyProEnabled)
		{
			return null;
		}
		return new XrayTunOptions("ProxyPro", 1500, GetElyTunDnsServers(), ElyTunIpv6Enabled, GetSystemProxyRules(), Array.Empty<string>(), RoutingPreset, GetApplicationBypassPaths());
	}

	private IReadOnlyList<string> GetApplicationBypassPaths()
	{
		return ApplicationBypassApplications
			.Select(application => application.ExecutablePath)
			.Where(path => !string.IsNullOrWhiteSpace(path))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToArray();
	}

	private void ApplyTunRuntimeSetting()
	{
		if ((ElyTunEnabled || ProxyProEnabled) && IsConnected)
		{
			RestartActiveXrayAsync();
		}
		else
		{
			SaveSettingsAsync();
		}
	}

	private async Task ApplyRoutingPresetAsync()
	{
		_ = 1;
		try
		{
			if ((ElyTunEnabled || ProxyProEnabled) && IsConnected)
			{
				AppendLog("[sys] Маршрутизация ElyTun изменена: " + GetRoutingPresetDisplayName() + "; безопасный перезапуск...");
				await RestartActiveXrayAsync();
			}
			else
			{
				StatusText = (ElyTunEnabled ? ("Маршрутизация ElyTun: " + GetRoutingPresetDisplayName()) : (ProxyProEnabled ? ("Маршрутизация Proxy Pro: " + GetRoutingPresetDisplayName()) : ("Профиль " + GetRoutingPresetDisplayName() + " сохранён")));
			}
			await SaveSettingsAsync();
		}
		catch (Exception ex)
		{
			StatusText = "Ошибка смены маршрутизации ElyTun";
			AppendLog("[err] Не удалось применить профиль маршрутизации: " + ex.Message);
		}
	}

	private string GetRoutingPresetDisplayName()
	{
		if (RoutingPreset != RoutingPreset.RoscomVpnDefault)
		{
			return "Свои правила";
		}
		return "RoscomVPN DEFAULT";
	}

	private async Task RequestElyTunAsync()
	{
		if (_isRelaunchingForElevation)
		{
			return;
		}

		if (ElyTunElevationService.IsRunningAsAdministrator())
		{
			ElyTunEnabled = true;
			return;
		}

		_isRelaunchingForElevation = true;
		IsLoading = true;
		try
		{
			VlessNode? nodeToResume = _connectedNode ?? SelectedNode;
			if (nodeToResume != null)
			{
				_lastConnectedNodeId = GetNodeKey(nodeToResume);
			}
			await SaveSettingsAsync();

			using (ElyTunElevationService.StartElevatedForElyTun())
			{
				AppendLog("[sys] Запущен ElyProxy с правами администратора для ElyTun");
				Application.Current.Shutdown();
			}
		}
		catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
		{
			StatusText = "Запуск ElyTun отменён";
			DialogService.Current.Notify("Запуск отменён", "ElyTun не включён. ElyProxy продолжает работать без прав администратора.", NoticeKind.Info);
		}
		catch (Exception ex)
		{
			StatusText = "Не удалось запросить права администратора";
			AppendLog("[err] Не удалось перезапустить ElyProxy от имени администратора: " + ex.Message);
			DialogService.Current.Notify("Не удалось включить ElyTun", ex.Message, NoticeKind.Error);
		}
		finally
		{
			_isRelaunchingForElevation = false;
			IsLoading = false;
		}
	}

	private static string CreateElyTunInterfaceName()
	{
		return $"ElyTun-{Guid.NewGuid():N}".Substring(0, 15);
	}

	private static IReadOnlyList<string> GetElyTunDnsServers()
	{
		try
		{
			List<string> servers = (from address in (from adapter in NetworkInterface.GetAllNetworkInterfaces()
					where adapter.OperationalStatus == OperationalStatus.Up && adapter.NetworkInterfaceType != NetworkInterfaceType.Loopback && adapter.NetworkInterfaceType != NetworkInterfaceType.Tunnel && adapter.NetworkInterfaceType != NetworkInterfaceType.Ppp && !IsVirtualOrVpnAdapter(adapter)
					select adapter).SelectMany((NetworkInterface adapter) => adapter.GetIPProperties().DnsAddresses)
				where address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address)
				select address.ToString()).Distinct<string>(StringComparer.OrdinalIgnoreCase).Take(3).ToList();
			IReadOnlyList<string> result;
			if (servers.Count <= 0)
			{
				IReadOnlyList<string> defaultElyTunDnsServers = DefaultElyTunDnsServers;
				result = defaultElyTunDnsServers;
			}
			else
			{
				IReadOnlyList<string> defaultElyTunDnsServers = servers;
				result = defaultElyTunDnsServers;
			}
			return result;
		}
		catch
		{
			return DefaultElyTunDnsServers;
		}
	}

	private static IReadOnlyList<string> GetActiveAdapterNetworkCidrs()
	{
		HashSet<string> networks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		try
		{
			NetworkInterface[] allNetworkInterfaces = NetworkInterface.GetAllNetworkInterfaces();
			foreach (NetworkInterface adapter in allNetworkInterfaces)
			{
				if (adapter.OperationalStatus != OperationalStatus.Up || adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback || adapter.Name.StartsWith("ElyTun", StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}
				foreach (UnicastIPAddressInformation address in adapter.GetIPProperties().UnicastAddresses)
				{
					IPAddress ip = address.Address;
					if (!IPAddress.IsLoopback(ip) && !ip.Equals(IPAddress.Any) && !ip.Equals(IPAddress.IPv6Any) && address.PrefixLength > 0)
					{
						string cidr = ToNetworkCidr(ip, address.PrefixLength);
						if (cidr != null)
						{
							networks.Add(cidr);
						}
					}
				}
			}
		}
		catch
		{
		}
		return networks.ToArray();
	}

	private static string? ToNetworkCidr(IPAddress address, int prefixLength)
	{
		byte[] bytes = address.GetAddressBytes();
		int maxBits = bytes.Length * 8;
		if (prefixLength <= 0 || prefixLength > maxBits)
		{
			return null;
		}
		byte[] network = bytes.ToArray();
		for (int index = 0; index < network.Length; index++)
		{
			int bitsLeft = prefixLength - index * 8;
			if (bitsLeft < 8)
			{
				network[index] = (byte)((bitsLeft > 0) ? ((byte)(network[index] & (255 << 8 - bitsLeft))) : 0);
			}
		}
		return $"{new IPAddress(network)}/{prefixLength}";
	}

	private static bool IsVirtualOrVpnAdapter(NetworkInterface adapter)
	{
		string text = (adapter.Name + " " + adapter.Description).ToLowerInvariant();
		return new string[10] { "elytun", "happ", "tun", "tap", "vpn", "wintun", "wireguard", "sing-tun", "virtualbox", "radmin" }.Any(text.Contains);
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
				_ = DialogService.Current.AlertAsync("Нужны права администратора", "ElyTun меняет системные маршруты Windows. Включите ElyTun в селекторе режимов и подтвердите системный запрос Windows на запуск от имени администратора.");
			}
			return false;
		}
		string singBoxPath = _singBoxTunManager.GetSingBoxPath();
		if (!File.Exists(singBoxPath))
		{
			DisableElyTunAfterPreflightFailure();
			StatusText = "ElyTun: sing-box не найден";
			AppendLog("[err] ElyTun требует sing-box: " + singBoxPath);
			if (showMessage)
			{
				_ = DialogService.Current.AlertAsync("sing-box не найден", "ElyTun использует sing-box с автоматической маршрутизацией Windows.\n\nДобавьте sing-box.exe в папку sing-box рядом с ElyProxy.exe.");
			}
			return false;
		}
		if (TryFindConflictingTunAdapter(out string conflictingAdapter))
		{
			AppendLog("[warn] Найден активный VPN/TUN-адаптер: " + conflictingAdapter + ". Его локальная сеть будет направлена напрямую.");
		}
		if (TryFindZapretProcess(out string zapret))
		{
			AppendLog("[warn] Обнаружен " + zapret + ". ElyTun запускается без strict route, чтобы не конфликтовать с WinDivert/WFP. Если фильтр перехватывает сам VLESS-сервер, исключите его IP в zapret.");
		}
		return true;
	}

	private bool EnsureProxyProCanRun(bool showMessage)
	{
		string singBoxPath = _singBoxProxyManager.GetSingBoxPath();
		if (File.Exists(singBoxPath))
		{
			return true;
		}
		_proxyProEnabled = false;
		OnPropertyChanged("ProxyProEnabled");
		StatusText = "Proxy Pro: sing-box не найден";
		AppendLog("[err] Proxy Pro требует sing-box: " + singBoxPath);
		if (showMessage)
		{
			_ = DialogService.Current.AlertAsync("sing-box не найден", "Proxy Pro использует sing-box как локальный HTTP/SOCKS-прокси.\n\nДобавьте sing-box.exe в папку sing-box рядом с ElyProxy.exe.");
		}
		return false;
	}

	private static bool TryFindExternalXrayProcess(string expectedXrayPath, out string description)
	{
		description = string.Empty;
		Process[] processesByName = Process.GetProcessesByName("xray");
		foreach (Process process in processesByName)
		{
			try
			{
				string path = process.MainModule?.FileName;
				if (string.IsNullOrWhiteSpace(path) || AreSamePath(path, expectedXrayPath))
				{
					continue;
				}
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
			NetworkInterface adapter = NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault((NetworkInterface item) => item.OperationalStatus == OperationalStatus.Up && !item.Name.StartsWith("ElyTun", StringComparison.OrdinalIgnoreCase) && IsKnownTunAdapter(item));
			if (adapter == null)
			{
				return false;
			}
			description = adapter.Name + " (" + adapter.Description + ")";
			return true;
		}
		catch
		{
			return false;
		}
	}

	private static bool TryFindZapretProcess(out string description)
	{
		description = string.Empty;
		string[] array = new string[3] { "winws", "nfqws", "tpws" };
		foreach (string processName in array)
		{
			Process process = null;
			try
			{
				process = Process.GetProcessesByName(processName).FirstOrDefault();
				if (process == null)
				{
					continue;
				}
				description = $"{process.ProcessName}.exe (PID {process.Id})";
				return true;
			}
			catch
			{
			}
			finally
			{
				process?.Dispose();
			}
		}
		return false;
	}

	private static bool IsKnownTunAdapter(NetworkInterface adapter)
	{
		string text = (adapter.Name + " " + adapter.Description).ToLowerInvariant();
		if (new string[4] { "teredo", "isatap", "iphttps", "6to4" }.Any(text.Contains))
		{
			return false;
		}
		return new string[9] { "happ", "sing-tun", "wintun", "wireguard", "tap-windows", "openvpn", "outline", "radmin", "vpn" }.Any(text.Contains);
	}

	private static bool AreSamePath(string left, string right)
	{
		try
		{
			return string.Equals(Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
		}
		catch
		{
			return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
		}
	}

	private void DisableElyTunAfterPreflightFailure()
	{
		_elyTunEnabled = false;
		OnPropertyChanged("ElyTunEnabled");
		SaveSettingsAsync();
	}

	private static bool IsRunningAsAdministrator()
	{
		return ElyTunElevationService.IsRunningAsAdministrator();
	}

	private static bool TryGetXrayVersion(string xrayPath, out Version version)
	{
		version = new Version(0, 0, 0);
		try
		{
			using Process process = Process.Start(new ProcessStartInfo
			{
				FileName = xrayPath,
				Arguments = "version",
				WorkingDirectory = (Path.GetDirectoryName(xrayPath) ?? string.Empty),
				UseShellExecute = false,
				CreateNoWindow = true,
				RedirectStandardOutput = true,
				RedirectStandardError = true
			});
			if (process == null)
			{
				return false;
			}
			if (!process.WaitForExit(2500))
			{
				try
				{
					process.Kill(entireProcessTree: true);
				}
				catch
				{
				}
				return false;
			}
			string output = process.StandardOutput.ReadToEnd();
			if (string.IsNullOrWhiteSpace(output))
			{
				output = process.StandardError.ReadToEnd();
			}
			if (!Version.TryParse(output.Split(new char[2] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault()?.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Skip(1).FirstOrDefault(), out Version parsed) || parsed == null)
			{
				return false;
			}
			version = parsed;
			return true;
		}
		catch
		{
			return false;
		}
	}

	private bool EnsureSocksPortAvailableForStart()
	{
		if (IsLocalTcpPortAvailable(SocksPort))
		{
			return true;
		}
		string corePath = (ElyTunEnabled ? _singBoxTunManager.GetSingBoxPath() : (ProxyProEnabled ? _singBoxProxyManager.GetSingBoxPath() : _xrayManager.GetXrayPath()));
		if (PortOwnerService.TryStopStaleCoreOnPort(SocksPort, corePath, out string cleanupMessage))
		{
			AppendLog(cleanupMessage);
			if (IsLocalTcpPortAvailable(SocksPort))
			{
				return true;
			}
		}
		StatusText = $"Порт {SocksPort} занят";
		string owner = (string.IsNullOrWhiteSpace(cleanupMessage) ? PortOwnerService.DescribeTcpPortOwner(SocksPort) : cleanupMessage);
		AppendLog("[err] " + owner);
		return false;
	}

	private static bool IsLocalTcpPortAvailable(int port)
	{
		try
		{
			TcpListener tcpListener = new TcpListener(IPAddress.Loopback, port);
			tcpListener.Start();
			tcpListener.Stop();
			return true;
		}
		catch
		{
			return false;
		}
	}

	private async Task ReconnectAsync(VlessNode node, int connectionGeneration)
	{
		if (_isReconnecting || _isDisposed)
		{
			return;
		}
		_isReconnecting = true;
		AppendLog("[sys] Обрыв соединения. Переподключение через 3 сек.: " + node.DisplayName);
		bool lifecycleLockHeld = false;
		try
		{
			if (!CanTryAutoReconnect())
			{
				return;
			}
			await Task.Delay(TimeSpan.FromSeconds(3.0));
			await _coreLifecycleLock.WaitAsync();
			lifecycleLockHeld = true;
			if (_isDisposed || !AutoReconnect || IsConnected || Volatile.Read(ref _connectionGeneration) != connectionGeneration)
			{
				return;
			}
			IsLoading = true;
			StatusText = "Переподключение...";
			if (ElyTunEnabled && !EnsureElyTunCanRun(showMessage: true))
			{
				_connectedNode = null;
				ConnectionInfo = string.Empty;
				ConnectedNodeName = string.Empty;
			}
			else
			{
				if (ProxyProEnabled && !EnsureProxyProCanRun(showMessage: true))
				{
					return;
				}
				if (!EnsureSocksPortAvailableForStart())
				{
					_connectedNode = null;
					ConnectionInfo = string.Empty;
					ConnectedNodeName = string.Empty;
					return;
				}
				await StartSelectedCoreAsync(node);
				IsConnected = true;
				_connectedNode = node;
				ConnectedNodeName = node.DisplayName;
				UpdateConnectionInfo(node);
				RememberConnectedNode(node);
				if (SystemProxyEnabled)
				{
					await TryActivateSystemProxyAsync();
				}
				if (ProxyProEnabled)
				{
					ActivateProxyPro();
				}
			}
		}
		catch (Exception ex)
		{
			StatusText = "Ошибка переподключения";
			AppendLog("[err] Не удалось переподключиться: " + ex.Message);
		}
		finally
		{
			IsLoading = false;
			if (lifecycleLockHeld)
			{
				_coreLifecycleLock.Release();
			}
			_isReconnecting = false;
		}
	}

	private bool CanTryAutoReconnect()
	{
		if (_autoReconnectPolicy.TryBeginAttempt(DateTime.UtcNow))
		{
			return true;
		}
		StatusText = "Автопереподключение остановлено";
		AppendLog("[err] Автопереподключение остановлено после 3 неудачных попыток.");
		_connectedNode = null;
		ConnectionInfo = string.Empty;
		ConnectedNodeName = string.Empty;
		return false;
	}

	private static string GetNodeKey(VlessNode node)
	{
		return string.Join("|", node.UUID, node.Address, node.Port, node.Network, node.Security, node.SNI, node.PublicKey, node.ShortId);
	}

	private static bool NodesMatch(VlessNode left, VlessNode right)
	{
		if (left.Address == right.Address && left.Port == right.Port && left.UUID == right.UUID && left.Network == right.Network)
		{
			return left.Security == right.Security;
		}
		return false;
	}

	private string[] GetSystemProxyRules()
	{
		string[] rules = (from rule in SystemProxyRulesText.Split(new char[5] { '\r', '\n', '|', ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
			select rule.Trim() into rule
			where !string.IsNullOrWhiteSpace(rule)
			select rule).Distinct<string>(StringComparer.OrdinalIgnoreCase).ToArray();
		if (rules.Length == 0)
		{
			return PacServerService.DefaultBypassRules;
		}
		return rules;
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
		catch
		{
		}
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
			Process.Start(new ProcessStartInfo
			{
				FileName = url,
				UseShellExecute = true
			});
		}
		catch
		{
		}
	}

	private void OnLogReceived(string message)
	{
		if (!_isDisposed)
		{
			if (Interlocked.Increment(ref _pendingLogMessageCount) > 1000)
			{
				Interlocked.Decrement(ref _pendingLogMessageCount);
				Interlocked.Increment(ref _droppedLogMessageCount);
			}
			else
			{
				_pendingLogMessages.Enqueue(message);
			}
		}
	}

	private void OnStatusChanged(bool running)
	{
		bool suppressedDisconnect = !running && Volatile.Read(ref _coreLifecycleTransition) > 0;

		_dispatcher.InvokeAsync((Action)delegate
		{
			if (suppressedDisconnect)
			{
				return;
			}

			IsConnected = running;
			StatusText = (running ? "Подключено" : "Отключено");
			if (!running)
			{
				ConnectionInfo = string.Empty;
				ConnectedNodeName = string.Empty;
				DeactivateSystemProxyForDisconnectedServer(silent: true);
				DeactivateProxyPro(silent: true);
				if (AutoReconnect && !_isReconnecting && !_isDisposed && !_isUpdating && _connectedNode != null)
				{
					ReconnectAsync(_connectedNode, Volatile.Read(ref _connectionGeneration));
				}
			}
		});
	}

	private void OnXrayStatusChanged(bool running)
	{
		if (!ElyTunEnabled && !ProxyProEnabled)
		{
			OnStatusChanged(running);
		}
	}

	private void OnSingBoxTunStatusChanged(bool running)
	{
		if (ElyTunEnabled)
		{
			OnStatusChanged(running);
		}
	}

	private void OnSingBoxProxyStatusChanged(bool running)
	{
		if (ProxyProEnabled)
		{
			OnStatusChanged(running);
		}
	}

	private async Task StartSelectedCoreAsync(VlessNode node)
	{
		if (ElyTunEnabled)
		{
			XrayTunOptions options = BuildElyTunOptionsOrNull() ?? throw new InvalidOperationException("Параметры ElyTun не подготовлены.");
			await _singBoxTunManager.StartAsync(node, SocksPort, options);
		}
		else if (ProxyProEnabled)
		{
			XrayTunOptions options2 = BuildProxyProOptionsOrNull() ?? throw new InvalidOperationException("Параметры Proxy Pro не подготовлены.");
			await _singBoxProxyManager.StartAsync(node, SocksPort, options2);
		}
		else
		{
			await _xrayManager.StartAsync(node, SocksPort);
		}
	}

	private bool IsActiveCoreRunning()
	{
		if (ElyTunEnabled)
		{
			return _singBoxTunManager.IsRunning;
		}
		if (ProxyProEnabled)
		{
			return _singBoxProxyManager.IsRunning;
		}
		return _xrayManager.IsRunning;
	}

	private async Task StopAllCoresAsync()
	{
		await _xrayManager.StopAsync();
		await _singBoxTunManager.StopAsync();
		await _singBoxProxyManager.StopAsync();
	}

	private async void OnSubscriptionUpdateTimerTick(object? sender, EventArgs e)
	{
		if (!IsLoading && Subscriptions.Count != 0)
		{
			AppendLog("[sys] Автообновление подписок...");
			await RunSubscriptionMutationAsync(UpdateAllSubscriptionsAsync);
		}
	}

	private void ConfigureSubscriptionUpdateTimer()
	{
		_subscriptionUpdateTimer.Stop();
		if (SubscriptionUpdateIntervalMinutes > 0)
		{
			_subscriptionUpdateTimer.Interval = TimeSpan.FromMinutes(SubscriptionUpdateIntervalMinutes);
			_subscriptionUpdateTimer.Start();
		}
	}

	private void DeactivateSystemProxyForDisconnectedServer(bool silent)
	{
		if (!SystemProxyEnabled)
		{
			return;
		}
		try
		{
			StopSystemProxy(silent);
		}
		catch (Exception ex)
		{
			AppendLog("[err] Не удалось выключить системный прокси: " + ex.Message);
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
			AppendLog("[err] Не удалось включить системный прокси: " + ex.Message);
		}
	}

	private void ActivateProxyPro()
	{
		if (ProxyProEnabled && _windowsProxyRecoveryService.ActiveState == null)
		{
			_windowsProxyRecoveryService.PrepareForManualProxy(SocksPort, GetSystemProxyRules());
			_windowsProxyService.EnableManualProxy(SocksPort, GetSystemProxyRules());
			StatusText = "Proxy Pro включён";
			AppendLog("[sys] Proxy Pro включён: системный HTTP/HTTPS/SOCKS-прокси");
		}
	}

	private void DeactivateProxyPro(bool silent)
	{
		if (_windowsProxyRecoveryService.ActiveState?.Mode == WindowsProxyRecoveryMode.Manual)
		{
			WindowsProxyRecoveryResult recovery = _windowsProxyRecoveryService.RestoreActive();
			if (recovery.Status == WindowsProxyRecoveryStatus.Failed)
			{
				AppendLog("[err] " + recovery.Message);
				DialogService.Current.Notify("Не удалось восстановить прокси Windows", recovery.Message ?? "Настройки оставлены для повторной попытки при следующем запуске.", NoticeKind.Error);
			}
			else if (recovery.Status == WindowsProxyRecoveryStatus.UserSettingsPreserved)
			{
				AppendLog("[warn] " + recovery.Message);
			}

			if (!silent)
			{
				StatusText = "Proxy Pro выключен";
				AppendLog("[sys] Proxy Pro выключен");
			}
		}
	}

	private async Task ApplyProxyProSettingAsync(bool enabled)
	{
		_ = 1;
		try
		{
			if (enabled && !EnsureProxyProCanRun(showMessage: true))
			{
				return;
			}
			if (enabled && SystemProxyEnabled)
			{
				StopSystemProxy(silent: true);
				_systemProxyEnabled = false;
				OnPropertyChanged("SystemProxyEnabled");
			}
			if (enabled && ElyTunEnabled)
			{
				_elyTunEnabled = false;
				OnPropertyChanged("ElyTunEnabled");
			}
			if (!enabled)
			{
				DeactivateProxyPro(silent: false);
			}
			if (IsConnected)
			{
				await RestartActiveXrayAsync();
				if (enabled)
				{
					ActivateProxyPro();
				}
			}
			else
			{
				StatusText = (enabled ? "Proxy Pro включится после подключения" : "Proxy Pro выключен");
			}
			await SaveSettingsAsync();
		}
		catch (Exception ex)
		{
			if (enabled)
			{
				DeactivateProxyPro(silent: true);
				_proxyProEnabled = false;
				OnPropertyChanged("ProxyProEnabled");
			}
			StatusText = "Ошибка Proxy Pro";
			AppendLog("[err] Не удалось изменить Proxy Pro: " + ex.Message);
		}
	}

	private async Task ApplyElyTunSettingAsync(bool enabled)
	{
		_ = 1;
		try
		{
			if (!enabled || EnsureElyTunCanRun(showMessage: true))
			{
				if (enabled && SystemProxyEnabled)
				{
					StopSystemProxy(silent: true);
					_systemProxyEnabled = false;
					OnPropertyChanged("SystemProxyEnabled");
					AppendLog("[sys] Системный прокси выключен: ElyTun использует полный TUN-режим");
				}
				if (enabled && ProxyProEnabled)
				{
					DeactivateProxyPro(silent: true);
					_proxyProEnabled = false;
					OnPropertyChanged("ProxyProEnabled");
					AppendLog("[sys] Proxy Pro выключен: ElyTun использует TUN-режим");
				}
				if (IsConnected)
				{
					await RestartActiveXrayAsync();
				}
				else
				{
					StatusText = (enabled ? "ElyTun включится после подключения" : "ElyTun выключен");
				}
				await SaveSettingsAsync();
			}
		}
		catch (Exception ex)
		{
			if (enabled)
			{
				_elyTunEnabled = false;
				OnPropertyChanged("ElyTunEnabled");
			}
			StatusText = "Ошибка ElyTun";
			AppendLog("[err] Не удалось изменить режим ElyTun: " + ex.Message);
		}
	}

	private async Task RestartActiveXrayAsync()
	{
		await _coreLifecycleLock.WaitAsync();
		try
		{
			await RestartActiveCoreAsync();
		}
		finally
		{
			_coreLifecycleLock.Release();
		}
	}

	private async Task RestartActiveCoreAsync()
	{
		if (_connectedNode == null || !IsConnected || (ElyTunEnabled && !EnsureElyTunCanRun(showMessage: true)) || (ProxyProEnabled && !EnsureProxyProCanRun(showMessage: true)))
		{
			return;
		}
		Interlocked.Increment(ref _coreLifecycleTransition);
		try
		{
			IsLoading = true;
			StatusText = (ElyTunEnabled ? "Перезапуск ElyTun..." : (ProxyProEnabled ? "Перезапуск Proxy Pro..." : "Перезапуск Xray..."));
			if (ProxyProEnabled)
			{
				DeactivateProxyPro(silent: true);
			}
			await StopAllCoresAsync();
			IsConnected = false;
			await StartSelectedCoreAsync(_connectedNode);
			IsConnected = true;
			UpdateConnectionInfo(_connectedNode);
			if (SystemProxyEnabled)
			{
				await TryActivateSystemProxyAsync();
			}
			if (ProxyProEnabled)
			{
				ActivateProxyPro();
			}
			if (ElyTunEnabled)
			{
				VerifyElyTunConnectivityAsync();
			}
		}
		catch (Exception ex)
		{
			IsConnected = IsActiveCoreRunning();
			if (!IsConnected)
			{
				_connectedNode = null;
				ConnectionInfo = string.Empty;
				ConnectedNodeName = string.Empty;
			}
			StatusText = "Ошибка перезапуска соединения";
			AppendLog("[err] Не удалось перезапустить соединение: " + ex.Message);
		}
		finally
		{
			IsLoading = false;
			Interlocked.Decrement(ref _coreLifecycleTransition);
		}
	}

	private async Task VerifyElyTunConnectivityAsync()
	{
		await Task.Delay(TimeSpan.FromSeconds(3.0));
		try
		{
			using HttpClientHandler handler = new HttpClientHandler
			{
				UseProxy = false,
				AllowAutoRedirect = false
			};
			using HttpClient http = new HttpClient(handler)
			{
				Timeout = TimeSpan.FromSeconds(8.0)
			};
			http.DefaultRequestHeaders.UserAgent.ParseAdd("ElyProxy/ElyTun");
			Stopwatch sw = Stopwatch.StartNew();
			HttpResponseMessage response = await http.GetAsync("https://www.gstatic.com/generate_204", HttpCompletionOption.ResponseHeadersRead);
			try
			{
				sw.Stop();
				int statusCode = (int)response.StatusCode;
				if (statusCode >= 200 && statusCode < 400)
				{
					await _dispatcher.InvokeAsync((Action)delegate
					{
						StatusText = $"ElyTun работает: {sw.ElapsedMilliseconds} ms";
						AppendLog($"[sys] ElyTun self-test OK: HTTP {response.StatusCode}, {sw.ElapsedMilliseconds} ms");
					});
					return;
				}
				await _dispatcher.InvokeAsync((Action)delegate
				{
					StatusText = "ElyTun: HTTP-тест не прошёл";
					AppendLog($"[err] ElyTun self-test failed: HTTP {response.StatusCode}");
				});
			}
			finally
			{
				if (response != null)
				{
					((IDisposable)response).Dispose();
				}
			}
		}
		catch (Exception ex)
		{
			Exception ex2 = ex;
			await _dispatcher.InvokeAsync((Action)delegate
			{
				StatusText = "ElyTun: сеть не отвечает";
				AppendLog("[err] ElyTun self-test failed: " + ex2.GetType().Name + ": " + ex2.Message);
			});
		}
	}

	private async Task ApplySystemProxySettingAsync(bool enabled)
	{
		_ = 3;
		try
		{
			if (enabled && ElyTunEnabled)
			{
				_elyTunEnabled = false;
				OnPropertyChanged("ElyTunEnabled");
				AppendLog("[sys] ElyTun выключен: системный прокси использует PAC-режим");
				if (IsConnected)
				{
					await RestartActiveXrayAsync();
				}
			}
			if (enabled && ProxyProEnabled)
			{
				DeactivateProxyPro(silent: true);
				_proxyProEnabled = false;
				OnPropertyChanged("ProxyProEnabled");
				if (IsConnected)
				{
					await RestartActiveXrayAsync();
				}
			}
			if (enabled && IsConnected)
			{
				await StartSystemProxyAsync();
			}
			else if (enabled)
			{
				StatusText = "Системный прокси включится после подключения";
			}
			else
			{
				StopSystemProxy(silent: false);
			}
			await SaveSettingsAsync();
		}
		catch (Exception ex)
		{
			if (enabled)
			{
				_pacServerService.Stop();
				_systemProxyEnabled = false;
				OnPropertyChanged("SystemProxyEnabled");
			}
			StatusText = "Ошибка системного прокси";
			AppendLog("[err] Не удалось изменить системный прокси: " + ex.Message);
		}
	}

	private async Task RestartSystemProxyAsync()
	{
		_ = 1;
		try
		{
			// Keep the old PAC URL registered until the replacement server is ready. The recovery journal
			// can still identify ElyProxy ownership if the process exits during a port change.
			await StartSystemProxyAsync();
			await SaveSettingsAsync();
		}
		catch (Exception ex)
		{
			StatusText = "Ошибка системного прокси";
			AppendLog("[err] Не удалось перезапустить системный прокси: " + ex.Message);
		}
	}

	private async Task StartSystemProxyAsync()
	{
		ValidatePacPort();
		_windowsProxyRecoveryService.PrepareForPac(PacUrl);
		await _pacServerService.StartAsync(PacPort, SocksPort, GetSystemProxyRules());
		_windowsProxyService.EnablePac(PacUrl);
		_previousAutoConfigUrl = _windowsProxyRecoveryService.ActiveState?.OriginalSettings.AutoConfigUrl?.StringValue;
		StatusText = "Системный прокси включён";
		AppendLog("[sys] Системный прокси включён");
	}

	private void StopSystemProxy(bool silent)
	{
		WindowsProxyRecoveryResult recovery = _windowsProxyRecoveryService.RestoreActive();
		if (recovery.Status == WindowsProxyRecoveryStatus.Failed)
		{
			AppendLog("[err] " + recovery.Message);
			DialogService.Current.Notify("Не удалось восстановить прокси Windows", recovery.Message ?? "Настройки оставлены для повторной попытки при следующем запуске.", NoticeKind.Error);
		}
		else if (recovery.Status == WindowsProxyRecoveryStatus.UserSettingsPreserved)
		{
			AppendLog("[warn] " + recovery.Message);
		}

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
		int pacPort = PacPort;
		if ((pacPort < 1024 || pacPort > 65535) ? true : false)
		{
			throw new InvalidOperationException("PAC порт должен быть в диапазоне 1024-65535.");
		}
	}

	private void AppendLog(string message)
	{
		string timestamp = DateTime.Now.ToString("HH:mm:ss");
		LogText += $"[{timestamp}] {message}\n";
		if (LogText.Length > 50000)
		{
			string logText = LogText;
			int length = logText.Length;
			int num = length - 25000;
			LogText = logText.Substring(num, length - num);
		}
	}

	private void OnLogFlushTimerTick(object? sender, EventArgs e)
	{
		List<string> messages = new List<string>(200);
		string message;
		while (messages.Count < 200 && _pendingLogMessages.TryDequeue(out message))
		{
			Interlocked.Decrement(ref _pendingLogMessageCount);
			messages.Add(message);
		}
		int dropped = Interlocked.Exchange(ref _droppedLogMessageCount, 0);
		if (messages.Count == 0 && dropped == 0)
		{
			return;
		}
		StringBuilder log = new StringBuilder(LogText);
		foreach (string message2 in messages)
		{
			string timestamp = DateTime.Now.ToString("HH:mm:ss");
			log.Append('[').Append(timestamp).Append("] ")
				.Append(message2)
				.AppendLine();
		}
		if (dropped > 0)
		{
			string timestamp2 = DateTime.Now.ToString("HH:mm:ss");
			log.Append('[').Append(timestamp2).Append("] [warn] Лог перегружен: пропущено ")
				.Append(dropped)
				.AppendLine(" сообщений.");
		}
		string text = log.ToString();
		string logText;
		if (text.Length <= 50000)
		{
			logText = text;
		}
		else
		{
			int length = text.Length;
			int num = length - 25000;
			logText = text.Substring(num, length - num);
		}
		LogText = logText;
	}

	private void ClearLog()
	{
		string result;
		while (_pendingLogMessages.TryDequeue(out result))
		{
		}
		Interlocked.Exchange(ref _pendingLogMessageCount, 0);
		Interlocked.Exchange(ref _droppedLogMessageCount, 0);
		LogText = string.Empty;
	}

	private async Task CheckForUpdatesFromCommandAsync()
	{
		await CheckForUpdatesAsync(showNoUpdateMessage: true);
	}

	private async Task<bool> CheckForUpdatesAsync(bool showNoUpdateMessage)
	{
		if (IsCheckingForUpdates || _isUpdating)
		{
			return false;
		}
		IsCheckingForUpdates = true;
		SetUpdateState("Проверка обновлений…", "Проверка…", Brushes.DeepSkyBlue);
		try
		{
			UpdateCheckResult result = await _updateService.CheckAsync();
			if (!result.IsAvailable)
			{
				SetUpdateState("Установлена актуальная версия " + UpdateService.CurrentVersion.ToString(3), "v" + UpdateService.CurrentVersion.ToString(3) + " · актуально", Brushes.MediumSeaGreen);
				AppendLog("[sys] Обновления: установлена актуальная версия");
				if (showNoUpdateMessage)
				{
					DialogService.Current.Notify("Обновления", "У вас установлена актуальная версия ElyProxy.", NoticeKind.Success);
				}
				return false;
			}
			SetUpdateState("Доступна версия " + result.Version.ToString(3), "v" + result.Version.ToString(3) + " · доступно", Brushes.Gold);
			AppendLog("[sys] Обновления: доступна версия " + result.Version.ToString(3));
			if (AutoUpdate)
			{
				return await DownloadAndInstallUpdateAsync(result);
			}
			bool flag = await DialogService.Current.ConfirmAsync($"Доступна версия ElyProxy {result.Version.ToString(3)}", $"{result.Manifest.ReleaseNotes}\n\nСкачать и установить её сейчас?", "Установить", "Позже");
			if (flag)
			{
				flag = await DownloadAndInstallUpdateAsync(result);
			}
			return flag;
		}
		catch (Exception ex)
		{
			SetUpdateState("Не удалось проверить обновления", "Обновления · ошибка", Brushes.IndianRed);
			AppendLog("[warn] Обновления: " + ex.Message);
			if (showNoUpdateMessage)
			{
				DialogService.Current.Notify("Обновления", "Не удалось проверить обновления: " + ex.Message, NoticeKind.Error);
			}
			return false;
		}
		finally
		{
			IsCheckingForUpdates = false;
		}
	}

	private async Task<bool> DownloadAndInstallUpdateAsync(UpdateCheckResult result)
	{
		_isUpdating = true;
		IsLoading = true;
		try
		{
			SetUpdateState("Скачивание версии " + result.Version.ToString(3) + "…", "Обновление…", Brushes.DeepSkyBlue);
			Progress<double> progress = new Progress<double>(delegate(double value)
			{
				SetUpdateState($"Скачивание версии {result.Version.ToString(3)}: {value:0}%", $"Обновление · {value:0}%", Brushes.DeepSkyBlue);
			});
			string packagePath = await _updateService.DownloadVerifiedPackageAsync(result.Manifest, progress);
			SetUpdateState("Проверка пройдена, подготовка установки…", "Установка…", Brushes.DeepSkyBlue);
			AppendLog("[sys] Обновления: пакет " + result.Version.ToString(3) + " проверен по SHA-256");
			await StopForUpdateAsync();
			await SaveSettingsAsync();
			UpdateService.LaunchUpdater(packagePath);
			((DispatcherObject)Application.Current).Dispatcher.BeginInvoke(Application.Current.Shutdown);
			return true;
		}
		catch (Exception ex)
		{
			_isUpdating = false;
			SetUpdateState("Не удалось установить обновление", "Обновления · ошибка", Brushes.IndianRed);
			AppendLog("[err] Обновления: " + ex.Message);
			_ = DialogService.Current.AlertAsync("Не удалось установить обновление", "Текущая версия сохранена.\n\n" + ex.Message);
			return false;
		}
		finally
		{
			IsLoading = false;
		}
	}

	private async Task StopForUpdateAsync()
	{
		if (IsConnected)
		{
			await StopAllCoresAsync();
		}
		if (SystemProxyEnabled)
		{
			StopSystemProxy(silent: true);
		}
		DeactivateProxyPro(silent: true);
	}

	private void SetUpdateState(string status, string badgeText, Brush badgeBrush)
	{
		UpdateStatus = status;
		UpdateBadgeText = badgeText;
		UpdateBadgeBrush = badgeBrush;
	}

	private async Task LoadAllDataAsync()
	{
		_ = 4;
		try
		{
			StorageLoadResult<AppSettings> settingsResult = await _storageService.LoadSettingsAsync();
			_canSaveSettings = settingsResult.CanSave;
			ReportStorageLoadResult("Настройки", settingsResult);
			AppSettings settings = settingsResult.Data;
			bool isAdministrator = ElyTunElevationService.IsRunningAsAdministrator();
			bool elevatedElyTunLaunch = _launchElyTunOnStartup && isAdministrator;
			bool deferSavedElyTunAutoConnect = !isAdministrator && (settings.ElyTunEnabled || _launchElyTunOnStartup);
			_isLoadingSettings = true;
			SocksPort = ((settings.SocksPort > 0) ? settings.SocksPort : 1080);
			AutoConnect = settings.AutoConnect;
			AutoReconnect = settings.AutoReconnect;
			ShowLogs = settings.ShowLogs;
			PingMode = settings.PingMode;
			ProxyPingUrl = settings.ProxyPingUrl;
			ProxyPingConcurrency = settings.ProxyPingConcurrency;
			MainViewModel mainViewModel = this;
			int pacPort = settings.PacPort;
			mainViewModel.PacPort = ((pacPort >= 1024 && pacPort <= 65535) ? settings.PacPort : 18080);
			_previousAutoConfigUrl = settings.PreviousAutoConfigUrl;
			ProxyProEnabled = !elevatedElyTunLaunch && settings.ProxyProEnabled;
			ElyTunEnabled = elevatedElyTunLaunch || (!ProxyProEnabled && settings.ElyTunEnabled && isAdministrator);
			ElyTunIpv6Enabled = settings.ElyTunIpv6Enabled;
			RoutingPreset = (Enum.IsDefined(settings.RoutingPreset) ? settings.RoutingPreset : RoutingPreset.Custom);
			SystemProxyEnabled = !elevatedElyTunLaunch && !ProxyProEnabled && !ElyTunEnabled && (settings.SystemProxyEnabled || settings.PacModeEnabled);
			MainViewModel mainViewModel2 = this;
			IEnumerable<string> rules;
			if (settings.SystemProxyBypassRules.Count <= 0)
			{
				IEnumerable<string> defaultBypassRules = PacServerService.DefaultBypassRules;
				rules = defaultBypassRules;
			}
			else
			{
				IEnumerable<string> defaultBypassRules = settings.SystemProxyBypassRules;
				rules = defaultBypassRules;
			}
			mainViewModel2.SystemProxyRulesText = FormatRules(rules);
			RecoverProxySettingsOnStartup(settings);
			ApplicationBypassApplications.Clear();
			foreach (string path in (settings.ApplicationBypassPaths ?? new List<string>())
				.Select(RunningApplicationService.NormalizePath)
				.Where(path => !string.IsNullOrWhiteSpace(path))
				.Distinct(StringComparer.OrdinalIgnoreCase))
			{
				ApplicationBypassApplications.Add(RunningApplication.FromPath(path));
			}
			SubscriptionUpdateIntervalMinutes = settings.SubscriptionUpdateIntervalMinutes;
			AutoUpdate = settings.AutoUpdate;
			ThemeMode = settings.ThemeMode;
			BackdropMode = settings.BackdropMode;
			MotionMode = settings.MotionMode;
			ServerBadgeMode = settings.ServerBadgeMode;
			_lastConnectedNodeId = settings.LastConnectedNodeId;
			_manualNodes.Clear();
			List<VlessNode> manualNodes = settings.ManualNodes;
			if (manualNodes != null && manualNodes.Count > 0)
			{
				_manualNodes.AddRange(settings.ManualNodes);
			}
			try
			{
				if (settings.AutoStartWithWindows && !_autoStartService.IsEnabled())
				{
					_autoStartService.SetEnabled(enabled: true);
				}
				AutoStartWithWindows = settings.AutoStartWithWindows || _autoStartService.IsEnabled();
			}
			catch (Exception ex)
			{
				AutoStartWithWindows = settings.AutoStartWithWindows;
				AppendLog("[err] Не удалось проверить автозапуск: " + ex.Message);
			}
			finally
			{
				_isLoadingSettings = false;
				ConfigureSubscriptionUpdateTimer();
			}
			StorageLoadResult<List<Subscription>> subscriptionsResult = await _storageService.LoadSubscriptionsAsync();
			_canSaveSubscriptions = subscriptionsResult.CanSave;
			ReportStorageLoadResult("Подписки", subscriptionsResult);
			foreach (Subscription sub in subscriptionsResult.Data)
			{
				Subscriptions.Add(sub);
			}
			StorageLoadResult<List<ProxyProfile>> profilesResult = await _storageService.LoadProfilesAsync();
			_canSaveProfiles = profilesResult.CanSave;
			ReportStorageLoadResult("Профили", profilesResult);
			foreach (ProxyProfile p in profilesResult.Data)
			{
				Profiles.Add(p);
			}
			_dataReady = true;
			CommandManager.InvalidateRequerySuggested();
			RefreshAllNodes();
			AppendLog($"[sys] Загружено: {Subscriptions.Count} подписок, {AllNodes.Count} серверов, {Profiles.Count} профилей");
			SelectLastConnectedNode();
			if (_launchElyTunOnStartup)
			{
				if (!isAdministrator)
				{
					StatusText = "Для ElyTun нужны права администратора";
					AppendLog("[err] Запрос ElyTun передан процессу без прав администратора");
					return;
				}

				if (!await ElyTunElevationService.WaitForParentExitAsync(_handoffParentProcessId, TimeSpan.FromSeconds(30.0)))
				{
					AppendLog("[warn] Предыдущий процесс ElyProxy не завершился за 30 секунд; подключение продолжится с проверкой порта");
				}

				if (SelectedNode != null)
				{
					AppendLog("[sys] Подключение в ElyTun после запроса прав администратора: " + SelectedNode.DisplayName);
					await ConnectToNodeAsync(SelectedNode);
				}
				else
				{
					StatusText = "ElyTun включён — выберите сервер";
					await SaveSettingsAsync();
				}
				return;
			}

			if (!(await CheckForUpdatesAsync(showNoUpdateMessage: false)) && AutoConnect && !deferSavedElyTunAutoConnect)
			{
				if (SelectedNode != null)
				{
					AppendLog("[sys] Автоподключение к последнему серверу: " + SelectedNode.DisplayName);
					await ConnectToNodeAsync(SelectedNode);
				}
				else if (!string.IsNullOrWhiteSpace(_lastConnectedNodeId))
				{
					AppendLog("[sys] Последний сервер для автоподключения не найден");
				}
			}
			else if (deferSavedElyTunAutoConnect)
			{
				StatusText = "ElyTun выключен до подтверждения прав администратора";
				AppendLog("[sys] Автоподключение в сохранённом режиме ElyTun отложено: приложение запущено без прав администратора");
			}
		}
		catch (Exception ex2)
		{
			AppendLog("[err] Ошибка загрузки данных: " + ex2.Message);
			_isLoadingSettings = false;
		}
	}

	private void RecoverProxySettingsOnStartup(AppSettings settings)
	{
		WindowsProxyRecoveryResult recovery = _windowsProxyRecoveryService.RecoverPendingAtStartup();
		switch (recovery.Status)
		{
		case WindowsProxyRecoveryStatus.Restored:
			AppendLog("[warn] " + recovery.Message);
			DialogService.Current.Notify("Прокси Windows восстановлен", recovery.Message ?? "", NoticeKind.Warning);
			return;
		case WindowsProxyRecoveryStatus.UserSettingsPreserved:
			AppendLog("[sys] " + recovery.Message);
			return;
		case WindowsProxyRecoveryStatus.Failed:
			AppendLog("[err] " + recovery.Message);
			_ = DialogService.Current.AlertAsync("Не удалось восстановить прокси Windows", recovery.Message ?? "Файл восстановления оставлен для следующего запуска.");
			return;
		}

		// 1.3.8 stored the previous PAC URL in settings but did not persist a full recovery journal.
		string? activePacUrl = _windowsProxyService.GetAutoConfigUrl();
		if (!string.IsNullOrWhiteSpace(activePacUrl)
			&& string.Equals(activePacUrl, PacUrl, StringComparison.OrdinalIgnoreCase)
			&& (settings.SystemProxyEnabled || settings.PacModeEnabled))
		{
			_windowsProxyService.DisablePac(PacUrl, settings.PreviousAutoConfigUrl);
			_previousAutoConfigUrl = null;
			AppendLog("[warn] Восстановлены настройки PAC из предыдущей версии ElyProxy.");
			DialogService.Current.Notify("Настройки прокси восстановлены", "ElyProxy вернул прежний PAC-адрес после аварийного завершения.", NoticeKind.Warning);
			return;
		}

		if (settings.ProxyProEnabled
			&& _windowsProxyService.IsOwnedByManualProxy(
				_windowsProxyService.CaptureCurrentSettings(),
				SocksPort,
				GetSystemProxyRules()))
		{
			const string legacyMessage = "Обнаружен локальный прокси от старой версии ElyProxy. Та версия не сохраняла предыдущие параметры Windows, поэтому автоматическое восстановление невозможно. Откройте Параметры Windows → Сеть и Интернет → Прокси и проверьте настройки.";
			AppendLog("[warn] " + legacyMessage);
			DialogService.Current.Notify("Проверьте прокси Windows", legacyMessage, NoticeKind.Warning);
		}
	}

	private void ReportStorageLoadResult<T>(string label, StorageLoadResult<T> result)
	{
		switch (result.Status)
		{
		case StorageLoadStatus.Migrated:
			AppendLog($"[sys] {label}: старый формат автоматически обновлён.");
			break;
		case StorageLoadStatus.RecoveredFromBackup:
			AppendLog($"[warn] {label}: данные восстановлены из локальной резервной копии. {result.ErrorMessage}");
			DialogService.Current.Notify($"{label} восстановлены", "ElyProxy восстановил данные из локальной копии. Повреждённый исходный файл сохранён рядом с пометкой .corrupt-*.json.", NoticeKind.Warning);
			break;
		case StorageLoadStatus.Unavailable:
			string message = result.ErrorMessage ?? "Файл данных недоступен.";
			AppendLog($"[err] {label}: {message}");
			_ = DialogService.Current.AlertAsync($"Не удалось загрузить: {label.ToLowerInvariant()}", message + "\n\nИсходный файл сохранён; ElyProxy не будет записывать поверх него.");
			break;
		}
	}

	private async Task SaveSettingsAsync()
	{
		if (!_canSaveSettings)
		{
			return;
		}

		try
		{
			AppSettings settings = new AppSettings
			{
				SocksPort = SocksPort,
				AutoStartWithWindows = AutoStartWithWindows,
				AutoConnect = AutoConnect,
				AutoReconnect = AutoReconnect,
				ShowLogs = ShowLogs,
				AutoUpdate = AutoUpdate,
				ThemeMode = ThemeMode,
				BackdropMode = BackdropMode,
				MotionMode = MotionMode,
				ServerBadgeMode = ServerBadgeMode,
				PingMode = PingMode,
				ProxyPingUrl = ProxyPingUrl,
				ProxyPingConcurrency = ProxyPingConcurrency,
				SystemProxyEnabled = SystemProxyEnabled,
				SystemProxyBypassRules = GetSystemProxyRules().ToList(),
				ApplicationBypassPaths = ApplicationBypassApplications.Select(application => application.ExecutablePath).ToList(),
				PacModeEnabled = SystemProxyEnabled,
				PacPort = PacPort,
				ProxyProEnabled = ProxyProEnabled,
				ElyTunEnabled = ElyTunEnabled,
				ElyTunIpv6Enabled = ElyTunIpv6Enabled,
				RoutingPreset = RoutingPreset,
				PreviousAutoConfigUrl = _previousAutoConfigUrl,
				SubscriptionUpdateIntervalMinutes = SubscriptionUpdateIntervalMinutes,
				LastConnectedNodeId = _lastConnectedNodeId,
				ManualNodes = _manualNodes.ToList()
			};
			await _storageService.SaveSettingsAsync(settings);
			_settingsSaveErrorReported = false;
		}
		catch (Exception ex)
		{
			StatusText = "Не удалось сохранить настройки";
			if (!_settingsSaveErrorReported)
			{
				_settingsSaveErrorReported = true;
				AppendLog("[err] Настройки не сохранены: " + ex.Message);
				DialogService.Current.Notify("Не удалось сохранить настройки", ex.Message, NoticeKind.Error);
			}
		}
	}

	public void Dispose()
	{
		_isDisposed = true;
		_subscriptionUpdateTimer.Stop();
		_logFlushTimer.Stop();
		_subscriptionUpdateTimer.Tick -= OnSubscriptionUpdateTimerTick;
		try
		{
			if (SystemProxyEnabled)
			{
				StopSystemProxy(silent: true);
			}
			DeactivateProxyPro(silent: true);
		}
		catch
		{
		}
		_pacServerService.Dispose();
		_xrayManager.Dispose();
		_singBoxTunManager.Dispose();
		_singBoxProxyManager.Dispose();
		_subscriptionService.Dispose();
		GC.SuppressFinalize(this);
	}
}
