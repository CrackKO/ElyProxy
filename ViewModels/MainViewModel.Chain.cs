using System.Windows.Input;
using ElyProxy.Core;
using ElyProxy.Models;
using ElyProxy.Services;
using ElyProxy.Views.Sheets;

namespace ElyProxy.ViewModels;

public partial class MainViewModel
{
    private bool _exitProxyEnabled;
    private VlessNode? _exitProxy;
    private ICommand? _configureExitProxyCommand;
    public bool ExitProxyEnabled
    {
        get => _exitProxyEnabled;
        set
        {
            if (!_isLoadingSettings && (IsConnected || IsLoading))
            {
                DialogService.Current.Notify("Выходной прокси", "Сначала отключитесь от сервера.");
                OnPropertyChanged(nameof(ExitProxyEnabled));
                return;
            }
            if (SetProperty(ref _exitProxyEnabled, value, nameof(ExitProxyEnabled)) && !_isLoadingSettings) SaveSettingsAsync();
        }
    }
    public string ExitProxySummary => _exitProxy is null ? "Не настроен" : $"{_exitProxy.ProtocolDisplay} · {_exitProxy.Address}:{_exitProxy.Port}";
    public ICommand ConfigureExitProxyCommand => _configureExitProxyCommand ??= new AsyncRelayCommand(ConfigureExitProxyAsync, () => !IsConnected && !IsLoading);
    private async Task ConfigureExitProxyAsync()
    {
        var sheet = new AddServerSheet();
        if (!await DialogService.Current.ShowCustomAsync("Выходной прокси", sheet, "Сохранить") || !sheet.IsValid) return;
        var exit = new ParserService().ParseUri(sheet.Uri);
        if (exit is null || exit.Protocol is not ("socks" or "http"))
        {
            await DialogService.Current.AlertAsync("Выберите обычный прокси", "Для второго узла нужен SOCKS5, HTTP или HTTPS. Используйте способ «По параметрам».");
            return;
        }
        _exitProxy = exit;
        OnPropertyChanged(nameof(ExitProxySummary));
        SaveSettingsAsync();
    }
    private bool EnsureProxyChainSupported(VlessNode node, bool showMessage)
    {
        if (!ExitProxyEnabled) return true;
        try
        {
            if (_exitProxy is null) throw new NotSupportedException("Настройте выходной прокси в Настройки → Сеть.");
            if (ElyTunEnabled && TunCore is TunCoreKind.Hysteria or TunCoreKind.Tun2Proxy || UsesHysteriaCore(node))
                throw new NotSupportedException("Для цепочки выберите ядро sing-box или Xray вместо Hysteria / tun2proxy.");
            ProxyChainPolicy.Validate(node, _exitProxy);
            return true;
        }
        catch (NotSupportedException ex)
        {
            StatusText = "Цепочка не настроена";
            AppendLog("[err] " + ex.Message);
            if (showMessage) _ = DialogService.Current.AlertAsync("Выходной прокси", ex.Message);
            return false;
        }
    }
}
