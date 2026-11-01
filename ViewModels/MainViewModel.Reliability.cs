using ElyProxy.Services;

namespace ElyProxy.ViewModels;

public partial class MainViewModel
{
    private System.Windows.Input.ICommand? _networkStateCommand;
    public System.Windows.Input.ICommand NetworkStateCommand => _networkStateCommand ??= new AsyncRelayCommand(async () =>
    {
        var report = await NetworkDiagnosticsService.CaptureAsync(SocksPort);
        await DialogService.Current.AlertAsync("Состояние сети", report);
    }, () => !IsLoading);
    private bool HasOwnedCoreRunning() => _xrayManager.IsRunning || _singBoxTunManager.IsRunning
        || _singBoxProxyManager.IsRunning || _tun2ProxyManager.IsRunning || _hysteriaManager.IsRunning;
    private readonly LatestOperation _runtimeChanges = new();
    private async Task ApplyLatestRuntimeChangeAsync()
    {
        int version = _runtimeChanges.Request();
        await Task.Delay(450);
        if (!_runtimeChanges.IsCurrent(version) || _isDisposed || _coreLifecycleLock.ShuttingDown) return;
        await _coreLifecycleLock.WaitAsync();
        try
        {
            if (!_runtimeChanges.IsCurrent(version) || _isDisposed || _coreLifecycleLock.ShuttingDown) return;
            if (IsConnected) await RestartActiveCoreAsync();
            else if (HasOwnedCoreRunning() || _windowsProxyRecoveryService.ActiveState != null) await StopAllCoresAsync();
            await SaveSettingsAsync();
        }
        catch (Exception ex)
        {
            StatusText = "Изменения соединения не применены";
            AppendLog("[err] Настройки соединения: " + ex.GetBaseException().Message);
        }
        finally { _coreLifecycleLock.Release(); }
    }
    private Task ClearProxySettingsAsync()
    {
        // Recovery journal, not mutable mode flags, determines ownership.
        var recovery = _windowsProxyRecoveryService.RestoreActive();
        _pacServerService.Stop();
        if (recovery.Status == WindowsProxyRecoveryStatus.Failed)
            throw new IOException(recovery.Message);
        return Task.CompletedTask;
    }
    private async Task RollbackConnectionAsync()
    {
        try { await StopAllCoresAsync(); }
        catch (Exception ex) { _coreLifecycleLock.State = ConnectionState.CleanupFailed; AppendLog("[err] Неполный откат подключения: " + ex.GetBaseException().Message); }
        IsConnected = false;
        if (HasOwnedCoreRunning() || _windowsProxyRecoveryService.ActiveState != null)
            _coreLifecycleLock.State = ConnectionState.CleanupFailed;
        if (!IsConnected)
        {
            _connectedNode = null; ConnectionInfo = string.Empty; ConnectedNodeName = string.Empty;
        }
        NotifyConnectionPanel();
    }
    public System.Windows.Input.ICommand SelectConnectionModeCommand => new RelayCommand(parameter =>
    {
        if (parameter is not int index || index is < 0 or > 3 || IsLoading || _coreLifecycleLock.ShuttingDown) return;
        if (index == 3 && !CanRunElyTunWithoutElevation())
        {
            if (RequestElyTunCommand.CanExecute(null)) RequestElyTunCommand.Execute(null);
            return;
        }
        if (_systemProxyEnabled == (index == 1) && _proxyProEnabled == (index == 2) && _elyTunEnabled == (index == 3)) return;
        _systemProxyEnabled = index == 1;
        _proxyProEnabled = index == 2;
        _elyTunEnabled = index == 3;
        OnPropertyChanged(nameof(SystemProxyEnabled));
        OnPropertyChanged(nameof(ProxyProEnabled));
        OnPropertyChanged(nameof(ElyTunEnabled));
        OnPropertyChanged(nameof(IsPerAppSupported));
        _ = ApplyLatestRuntimeChangeAsync();
    }, _ => !IsLoading && !_coreLifecycleLock.ShuttingDown);

    private void SelectExclusiveMode(string mode)
    {
        if (mode != "tun" && _elyTunEnabled) { _elyTunEnabled = false; OnPropertyChanged(nameof(ElyTunEnabled)); }
        if (mode != "pro" && _proxyProEnabled) { _proxyProEnabled = false; OnPropertyChanged(nameof(ProxyProEnabled)); }
        if (mode != "pac" && _systemProxyEnabled) { _systemProxyEnabled = false; OnPropertyChanged(nameof(SystemProxyEnabled)); }
    }
}
