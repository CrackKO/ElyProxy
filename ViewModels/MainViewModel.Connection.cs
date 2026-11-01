using ElyProxy.Core;
using ElyProxy.Models;
using PerAppModeKind = ElyProxy.Models.PerAppMode;
using ElyProxy.Services;
using System.Windows.Input;

namespace ElyProxy.ViewModels;

public partial class MainViewModel
{
    private sealed record ActiveConnection(VlessNode Node, ResolvedConnectionPlan? Plan, XrayTunOptions? Options,
        bool Tun, bool Pro, bool Pac, TunCoreKind TunCore, int SocksPort, int PacPort, Hysteria2Engine HysteriaEngine);
    private ActiveConnection? _activeConnection;
    private ActiveConnection CaptureConnection(VlessNode node) => new(
        Newtonsoft.Json.JsonConvert.DeserializeObject<VlessNode>(Newtonsoft.Json.JsonConvert.SerializeObject(node))!,
        _resolvedPlan, _preparedOptions, ElyTunEnabled, ProxyProEnabled, SystemProxyEnabled, TunCore, SocksPort, PacPort, _hysteria2Engine);
    private void ConfirmConnection(VlessNode node)
    {
        _activeConnection = CaptureConnection(node);
        IsConnected = true;
        _coreLifecycleLock.State = ConnectionState.Connected;
        HasPendingConnectionSettings = false;
    }
    private async Task RestoreConnectionAsync(ActiveConnection previous, int generation)
    {
        if (_coreLifecycleLock.ShuttingDown || generation != Volatile.Read(ref _connectionGeneration)) return;
        await StopAllCoresAsync();
        _coreLifecycleLock.State = ConnectionState.Recovering;
        _resolvedPlan = previous.Plan; _preparedOptions = previous.Options; _preparedEndpointCidrs = null;
        if (previous.Options is { } options)
        {
            var old = DpiFor(previous.Node);
            _serverDpi[previous.Node.LibraryId] = previous.Plan?.Core == ProxyCoreKind.SingBox
                ? old with { SingBox = new(options.FragmentEnabled, options.FragmentInterval, options.RecordFragment,
                    options.TlsSpoofEnabled, options.TlsSpoofSni, options.TlsSpoofMethod, options.DpiFingerprint) }
                : old with { Xray = new(options.FragmentEnabled, options.FragmentPackets, options.FragmentLength,
                    options.FragmentInterval, options.NoiseType, options.NoisePacket, options.NoiseDelay, options.DpiFingerprint) };
        }
        _elyTunEnabled = previous.Tun; _proxyProEnabled = previous.Pro; _systemProxyEnabled = previous.Pac;
        _tunCore = previous.TunCore; _socksPort = previous.SocksPort; _pacPort = previous.PacPort;
        _hysteria2Engine = previous.HysteriaEngine;
        _connectedNode = previous.Node;
        foreach (var name in new[] { nameof(ElyTunEnabled), nameof(ProxyProEnabled), nameof(SystemProxyEnabled), nameof(TunCore), nameof(TunCoreIndex), nameof(SocksPort), nameof(PacPort), nameof(ResolvedCoreDescription) }) OnPropertyChanged(name);
        await StartSelectedCoreAsync(previous.Node);
        if (_coreLifecycleLock.ShuttingDown || generation != Volatile.Read(ref _connectionGeneration))
            throw new OperationCanceledException("Восстановление отменено");
        if (SystemProxyEnabled) await StartSystemProxyAsync();
        if (ProxyProEnabled) ActivateProxyPro();
        ConfirmConnection(previous.Node);
        ConnectedNodeName = previous.Node.DisplayName; UpdateConnectionInfo(previous.Node);
        StatusText = "Восстановлено предыдущее соединение";
    }
    private DpiSettings _dpiDefaults = DpiSettings.Off;
    private Dictionary<string, DpiSettings> _serverDpi = new();
    private DpiSettings DpiFor(VlessNode node) => _serverDpi.GetValueOrDefault(node.LibraryId) ?? _dpiDefaults;
    private void LoadDpiEditor()
    {
        var node = DpiProbeNode ?? SelectedNode;
        var settings = node == null ? _dpiDefaults : DpiFor(node);
        bool loading = _isLoadingSettings;
        _isLoadingSettings = true;
        try
        {
            var x = settings.Xray; var b = settings.SingBox;
            FragmentMode = (DpiProbeSingBox ? b.Fragment : x.Fragment) ? FragmentModeKind.Custom : FragmentModeKind.Off;
            FragmentPackets = x.Packets; FragmentLength = x.Length;
            FragmentInterval = DpiProbeSingBox ? b.FallbackDelay : x.Interval;
            FragmentRecord = DpiProbeSingBox && b.Records;
            NoiseTypeIndex = DpiProbeSingBox ? 0 : x.Noise switch { "rand" => 1, "str" => 2, "base64" => 3, _ => 0 };
            NoisePacket = x.NoisePacket; NoiseDelay = x.NoiseDelay;
            TlsSpoofEnabled = DpiProbeSingBox && b.Spoof; TlsSpoofSni = b.SpoofSni;
            TlsSpoofMethodIndex = b.SpoofMethod switch { "wrong-checksum" => 1, "wrong-ack" => 2, "wrong-md5" => 3, "wrong-timestamp" => 4, _ => 0 };
            DpiFingerprint = DpiProbeSingBox ? b.Fingerprint : x.Fingerprint;
        }
        finally { _isLoadingSettings = loading; }
    }
    private void SaveDpiEditor()
    {
        var node = DpiProbeNode ?? SelectedNode;
        var old = node == null ? _dpiDefaults : DpiFor(node);
        var updated = DpiProbeSingBox ? old with { SingBox = new(
            FragmentMode != FragmentModeKind.Off, FragmentInterval.Split('-')[0], FragmentRecord,
            TlsSpoofEnabled, TlsSpoofSni, _tlsSpoofMethod, DpiFingerprint) }
            : old with { Xray = new(FragmentMode != FragmentModeKind.Off, FragmentPackets, FragmentLength,
                FragmentInterval, _noiseType, NoisePacket, NoiseDelay, DpiFingerprint) };
        if (node == null) _dpiDefaults = updated;
        else _serverDpi[node.LibraryId] = updated;
    }
    private ProxyCoreKind? _proxyCoreSelection;
    private ResolvedConnectionPlan? _resolvedPlan;
    private XrayTunOptions? _preparedOptions;
    private IReadOnlyList<string>? _preparedEndpointCidrs;
    private bool _hasPendingConnectionSettings;
    private bool _shutdownComplete;
    private TaskCompletionSource? _dpiTuningFinished;
    public int ProxyCoreIndex
    {
        get => _proxyCoreSelection == ProxyCoreKind.Hysteria ? 4 : _proxyCoreSelection.HasValue ? (int)_proxyCoreSelection.Value : 3;
        set
        {
            _proxyCoreSelection = value == 4 ? ProxyCoreKind.Hysteria : value is >= 0 and <= 2 ? (ProxyCoreKind)value : null;
            OnPropertyChanged(nameof(ProxyCoreIndex));
            if (!_isLoadingSettings) MarkConnectionSettingsPending();
        }
    }
    private TunCoreKind EffectiveTunCore => TunCore == TunCoreKind.Auto
        ? _resolvedPlan?.Core == ProxyCoreKind.Hysteria ? TunCoreKind.Hysteria : _resolvedPlan?.Core == ProxyCoreKind.Xray ? TunCoreKind.Xray : TunCoreKind.SingBox : TunCore;
    private bool UsesSingBoxProxy => _resolvedPlan?.Core is ProxyCoreKind.SingBox or ProxyCoreKind.Hysteria || (_resolvedPlan == null && ProxyProEnabled);
    public string ResolvedCoreDescription => _resolvedPlan?.Reason ?? "Ядро будет определено перед подключением";
    public bool HasPendingConnectionSettings
    {
        get => _hasPendingConnectionSettings;
        private set
        {
            if (SetProperty(ref _hasPendingConnectionSettings, value, nameof(HasPendingConnectionSettings)))
                CommandManager.InvalidateRequerySuggested();
        }
    }
    private void MarkConnectionSettingsPending() { HasPendingConnectionSettings = true; }
    public ICommand ApplyConnectionSettingsCommand => new AsyncRelayCommand(async () =>
    {
        await _coreLifecycleLock.WaitAsync();
        try
        {
            if (_isDisposed || _coreLifecycleLock.ShuttingDown) return;
            SaveDpiEditor();
            if (IsConnected) await RestartActiveCoreAsync();
            await SaveSettingsAsync();
            HasPendingConnectionSettings = false;
        }
        finally { _coreLifecycleLock.Release(); }
    }, () => !IsLoading && HasPendingConnectionSettings);

    private void PrepareCoreSelection(VlessNode node)
    {
        if (ElyTunEnabled && TunCore == TunCoreKind.Hysteria)
        {
            if (node.Protocol is not ("hysteria2" or "hy2")) throw new NotSupportedException("Hysteria TUN поддерживает только Hysteria2");
            _resolvedPlan = new(ProxyCoreKind.Hysteria, "Ядро выбрано вручную: Hysteria 2");
            return;
        }
        _resolvedPlan = ResolveCoreForNode(node);
        OnPropertyChanged(nameof(ResolvedCoreDescription));
        AppendLog("[sys] " + ResolvedCoreDescription);
    }

    private ResolvedConnectionPlan ResolveCoreForNode(VlessNode node)
    {
        var dpi = DpiFor(node);
        bool xrayDpi = dpi.Xray.Fragment || !string.IsNullOrEmpty(dpi.Xray.Noise) || !string.IsNullOrEmpty(dpi.Xray.Fingerprint);
        bool singBoxDpi = dpi.SingBox.Fragment || dpi.SingBox.Records || dpi.SingBox.Spoof || !string.IsNullOrEmpty(dpi.SingBox.Fingerprint);
        var requested = ElyTunEnabled ? TunCore switch
        {
            TunCoreKind.Auto => ProxyCoreKind.Auto,
            TunCoreKind.Xray => ProxyCoreKind.Xray,
            TunCoreKind.SingBox => ProxyCoreKind.SingBox,
            _ => ProxyCoreKind.Auto
        } : _proxyCoreSelection ?? (ProxyProEnabled ? ProxyCoreKind.SingBox : ProxyCoreKind.Xray);
        bool requireXray = xrayDpi && !singBoxDpi;
        bool requireSingBox = singBoxDpi && !xrayDpi || RoutingPreset == RoutingPreset.RoscomVpnDefault && _perAppMode != PerAppModeKind.OnlySelected;
        bool allowHysteria = !xrayDpi && !singBoxDpi && GetApplicationBypassPaths().Count == 0 && !requireSingBox;
        if (!EngineAuto && RequiresHysteriaCore(node)) requested = ProxyCoreKind.Hysteria;
        _engineCandidates = CoreCandidateSelector.Evaluate(node, requested,
            File.Exists(_singBoxProxyManager.GetSingBoxPath()), File.Exists(_xrayManager.GetXrayPath()),
            requireXray, requireSingBox, _coreHistory, EngineMode,
            File.Exists(_hysteriaManager.GetHysteriaPath()), allowHysteria);
        foreach (var candidate in _engineCandidates)
            AppendLog($"[EngineManager] {candidate.Core}: {(candidate.Compatible ? $"score={candidate.Score}" : candidate.Rejection)}");
        var selected = _engineCandidates.FirstOrDefault(c => c.Compatible)
            ?? throw new NotSupportedException(string.Join("; ", _engineCandidates.Select(c => c.Rejection)));
        return new(selected.Core, $"Ядро: {selected.Core}" + (EngineAuto ? " (Авто)" : ""));
    }

    private async Task ValidatePreparedConnectionAsync(VlessNode node)
    {
        _preparedEndpointCidrs = null;
        if (ElyTunEnabled && EffectiveTunCore == TunCoreKind.Tun2Proxy)
            _preparedEndpointCidrs = await ServerAddressResolver.ResolveEndpointCidrsAsync(node.Address, TimeSpan.FromSeconds(3));
        _preparedOptions = ElyTunEnabled ? BuildElyTunOptionsOrNull() : WithFragmentSettings(
            new XrayTunOptions("Proxy", 1500, GetElyTunDnsServers(), ElyTunIpv6Enabled,
                GetSystemProxyRules(), Array.Empty<string>(), RoutingPreset, GetApplicationBypassPaths()));
        _preparedOptions = DpiFor(node).Apply(_preparedOptions!, _resolvedPlan?.Core == ProxyCoreKind.SingBox);
        if (ProxyProEnabled && _resolvedPlan?.Core == ProxyCoreKind.Xray && _preparedOptions != null)
            _preparedOptions = _preparedOptions with { HttpPort = GetFreeTcpPort() };
        if (_preparedOptions == null) throw new InvalidOperationException("Параметры не подготовлены");
        DpiSettingsValidator.Validate(_preparedOptions, node, _resolvedPlan?.Core == ProxyCoreKind.SingBox);
        if (_preparedOptions.TlsSpoofEnabled && !CanRunElyTunWithoutElevation())
            throw new InvalidOperationException("TLS spoofing требует прав администратора или работающей службы-помощника.");
        if (RoutingPreset == RoutingPreset.RoscomVpnDefault && _perAppMode != PerAppModeKind.OnlySelected &&
            (_resolvedPlan?.Core != ProxyCoreKind.SingBox || ElyTunEnabled && EffectiveTunCore == TunCoreKind.Hysteria))
            throw new NotSupportedException("RoscomVPN DEFAULT требует sing-box. Выберите Auto или sing-box.");
        if (ElyTunEnabled && EffectiveTunCore is TunCoreKind.Hysteria or TunCoreKind.Tun2Proxy && GetApplicationBypassPaths().Count > 0)
            throw new NotSupportedException("Выбранный TUN-компонент не поддерживает правила по приложениям.");
        if (ElyTunEnabled && EffectiveTunCore == TunCoreKind.Hysteria)
        {
            var dpi = DpiFor(node);
            if (dpi.Xray.Fragment || dpi.SingBox.Fragment || dpi.SingBox.Records || dpi.SingBox.Spoof || !string.IsNullOrEmpty(dpi.Xray.Noise))
                throw new NotSupportedException("Встроенные DPI-профили не поддерживаются ядром Hysteria. Выберите sing-box.");
            return;
        }
        if (_resolvedPlan?.Core == ProxyCoreKind.Hysteria) return;
        bool singBox = ElyTunEnabled ? EffectiveTunCore == TunCoreKind.SingBox || EffectiveTunCore == TunCoreKind.Tun2Proxy && _resolvedPlan?.Core == ProxyCoreKind.SingBox : UsesSingBoxProxy;
        string json = singBox ? ElyTunEnabled && EffectiveTunCore != TunCoreKind.Tun2Proxy
            ? new SingBoxConfigBuilder().BuildTun(node, SocksPort, _preparedOptions)
            : new SingBoxConfigBuilder().BuildSystemProxy(node, SocksPort, _preparedOptions)
            : new ConfigBuilder().Build(node, SocksPort, _preparedOptions, ElyTunEnabled && EffectiveTunCore != TunCoreKind.Tun2Proxy);
        await CoreConfigurationValidator.ValidateAsync(singBox ? _singBoxProxyManager.GetSingBoxPath() : _xrayManager.GetXrayPath(), json, singBox);
    }

    private async Task CheckPreparedPathAsync(CancellationToken cancellationToken = default)
    {
        using var handler = new System.Net.Http.SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            ConnectTimeout = TimeSpan.FromSeconds(3),
            UseProxy = !ElyTunEnabled,
            Proxy = ElyTunEnabled ? null : new System.Net.WebProxy($"socks5://127.0.0.1:{SocksPort}")
        };
        if (ElyTunEnabled)
        {
            string name = _preparedOptions?.InterfaceName ?? throw new IOException("Нет подготовленного TUN");
            handler.ConnectCallback = (context, token) => ConnectionReadiness.ConnectThroughTunAsync(name, context.DnsEndPoint, token);
        }
        using var http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        var result = await new ElyTunConnectivityService(http).CheckAsync(cancellationToken);
        if (!result.Reachable) throw new IOException("Передача HTTPS через выбранную сессию не подтверждена. " + result.Details);
        AppendLog("[sys] Сетевая сессия проверена: " + result.Details);
    }

    public async Task ShutdownAsync()
    {
        if (_shutdownComplete) return;
        CancelEngineAttempt();
        _coreLifecycleLock.BeginShutdown();
        _runtimeChanges.Cancel();
        Interlocked.Increment(ref _connectionGeneration);
        CancelServerProbe();
        if (_serverProbeFinished != null) await _serverProbeFinished.Task;
        _dpiTuningCancellation?.Cancel();
        if (_dpiTuningFinished != null) await _dpiTuningFinished.Task;
        _elyTunConnectivityCancellation?.Cancel();
        await _coreLifecycleLock.WaitAsync();
        try
        {
            await StopAllCoresAsync();
            IsConnected = false;
            _coreLifecycleLock.State = ConnectionState.Disconnected;
            _shutdownComplete = true;
        }
        catch
        {
            _coreLifecycleLock.State = ConnectionState.CleanupFailed;
            throw;
        }
        finally { _coreLifecycleLock.Release(); }
    }
}



