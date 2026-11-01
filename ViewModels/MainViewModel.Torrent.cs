using ElyProxy.Services;

namespace ElyProxy.ViewModels;

public partial class MainViewModel
{
    private TorrentBlockerService? _torrentBlocker;
    private bool _blockTorrent;
    private string _torrentProtectionStatus = "Контроль активен";

    public string TorrentProtectionStatus => BlockTorrent ? _torrentProtectionStatus : "Выключено";

    public bool BlockTorrent
    {
        get => _blockTorrent;
        set
        {
            if (!SetProperty(ref _blockTorrent, value, nameof(BlockTorrent))) return;
            _torrentProtectionStatus = "Контроль активен";
            OnPropertyChanged(nameof(TorrentProtectionStatus));
            if (_torrentBlocker == null)
            {
                _torrentBlocker = new TorrentBlockerService(OnTorrentBlockerLog);
                _torrentBlocker.ScanCompleted += OnTorrentScanCompleted;
            }
            _torrentBlocker.SetEnabled(value);
            if (!_isLoadingSettings)
            {
                AppendLog(value
                    ? "[sys] Блокировка Torrent включена: известные торрент-клиенты будут принудительно закрываться, пока ElyProxy открыт."
                    : "[sys] Блокировка Torrent выключена.");
                _ = SaveSettingsAsync();
            }
        }
    }

    private void OnTorrentBlockerLog(string message)
    {
        if (_isDisposed || _dispatcher.HasShutdownStarted) return;
        AppendLog(message);
        if (!message.StartsWith("[err]", StringComparison.Ordinal)) return;
        _dispatcher.BeginInvoke(new Action(() =>
        {
            if (_isDisposed || !BlockTorrent) return;
            _torrentProtectionStatus = "Не удалось закрыть клиент";
            OnPropertyChanged(nameof(TorrentProtectionStatus));
            DialogService.Current.Notify("Проверьте торрент-клиент", message[6..], NoticeKind.Warning);
        }));
    }

    private void OnTorrentScanCompleted(bool success)
    {
        string status = success ? "Контроль активен" : "Не удалось закрыть клиент";
        if (_dispatcher.HasShutdownStarted || _torrentProtectionStatus == status) return;
        _dispatcher.BeginInvoke(new Action(() =>
        {
            if (_isDisposed || !BlockTorrent) return;
            _torrentProtectionStatus = status;
            OnPropertyChanged(nameof(TorrentProtectionStatus));
        }));
    }
}
