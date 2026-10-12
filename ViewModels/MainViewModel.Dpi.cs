using ElyProxy.Core;
using ElyProxy.Models;
using ElyProxy.Services;
using System.Threading;
using System.Windows.Input;

namespace ElyProxy.ViewModels;

public partial class MainViewModel
{
    private DpiProfile _selectedDpiProfile = DpiProfileCatalog.All[0];
    private string _dpiFingerprint = "";
    private string _dpiTuningStatus = "Выберите сервер во вкладке «Серверы», затем отключите VPN для проверки. Подбор ничего не включает автоматически.";
    private CancellationTokenSource? _dpiTuningCancellation;
    private bool _dpiProbeSingBox;
    private AsyncRelayCommand? _tuneDpiCommand;
    private RelayCommand? _applyDpiCommand, _cancelDpiCommand, _applyDpiTrialCommand;

    public System.Collections.ObjectModel.ObservableCollection<DpiTrial> DpiTrials { get; } = new();
    public bool IsDpiTuning => _dpiTuningCancellation != null;
    private VlessNode? _dpiProbeNode;
    public VlessNode? DpiProbeNode
    {
        get => _dpiProbeNode;
        set { if (SetProperty(ref _dpiProbeNode, value, nameof(DpiProbeNode))) CommandManager.InvalidateRequerySuggested(); }
    }
    public DpiTrial? SelectedDpiTrial
    {
        get => _selectedDpiTrial;
        set
        {
            if (SetProperty(ref _selectedDpiTrial, value, nameof(SelectedDpiTrial)) && value != null)
                SelectedDpiProfile = value.Profile;
            CommandManager.InvalidateRequerySuggested();
        }
    }
    private DpiTrial? _selectedDpiTrial;
    public IReadOnlyList<DpiProfile> DpiProfiles => DpiProfileCatalog.All;
    public DpiProfile SelectedDpiProfile
    {
        get => _selectedDpiProfile;
        set { if (value != null) SetProperty(ref _selectedDpiProfile, value, nameof(SelectedDpiProfile)); }
    }
    public int DpiProbeCoreIndex
    {
        get => DpiProbeSingBox ? 1 : 0;
        set { DpiProbeSingBox = value == 1; OnPropertyChanged(nameof(DpiProbeCoreIndex)); }
    }
    public ICommand ApplyDpiTrialCommand => _applyDpiTrialCommand ??= new RelayCommand(() =>
    {
        if (SelectedDpiTrial == null) return;
        SelectedDpiProfile = SelectedDpiTrial.Profile;
        ApplyDpiProfile();
    }, () => SelectedDpiTrial?.Successes == 4 && _dpiTuningCancellation == null);
    public bool DpiProbeSingBox
    {
        get => _dpiProbeSingBox;
        set => SetProperty(ref _dpiProbeSingBox, value, nameof(DpiProbeSingBox));
    }
    public IReadOnlyList<KeyValuePair<string, string>> DpiFingerprintOptions { get; } = new[]
    {
        new KeyValuePair<string, string>("", "Как в подписке"),
        new KeyValuePair<string, string>("chrome", "Chrome"),
        new KeyValuePair<string, string>("firefox", "Firefox"),
        new KeyValuePair<string, string>("safari", "Safari"),
        new KeyValuePair<string, string>("edge", "Edge")
    };
    public string DpiFingerprint
    {
        get => _dpiFingerprint;
        set
        {
            var normalized = DpiFingerprintOptions.Any(p => p.Key == value) ? value : "";
            if (SetProperty(ref _dpiFingerprint, normalized, nameof(DpiFingerprint)) && !_isLoadingSettings)
                _ = SaveSettingsAsync();
        }
    }
    public string DpiTuningSummary => IsDpiTuning ? "Проверка выполняется…" :
        DpiTrials.Count > 0 ? $"Проверка завершена · {DpiTrials.Count} профилей. Выберите результат для применения." :
        "Проверка запускается вручную при отключённом VPN.";
    public string DpiTuningStatus
    {
        get => _dpiTuningStatus;
        private set { SetProperty(ref _dpiTuningStatus, value, nameof(DpiTuningStatus)); OnPropertyChanged(nameof(DpiTuningSummary)); }
    }
    public ICommand TuneDpiCommand => _tuneDpiCommand ??= new AsyncRelayCommand(TuneDpiAsync,
        () => !IsConnected && !IsLoading && (DpiProbeNode != null || SelectedNode != null) && _dpiTuningCancellation == null);
    public ICommand ApplyDpiProfileCommand => _applyDpiCommand ??= new RelayCommand(ApplyDpiProfile,
        () => _dpiTuningCancellation == null);
    public ICommand CancelDpiTuningCommand => _cancelDpiCommand ??= new RelayCommand(
        () => _dpiTuningCancellation?.Cancel(), () => _dpiTuningCancellation != null);

    private void ApplyDpiProfile()
    {
        // Batch setters so a single preset never schedules multiple core restarts.
        bool loading = _isLoadingSettings;
        _isLoadingSettings = true;
        try
        {
            FragmentMode = SelectedDpiProfile.Fragment ? FragmentModeKind.Custom : FragmentModeKind.Off;
            FragmentPackets = SelectedDpiProfile.Packets;
            FragmentLength = SelectedDpiProfile.Length;
            FragmentInterval = SelectedDpiProfile.Interval;
            FragmentRecord = SelectedDpiProfile.Records;
            NoiseTypeIndex = 0;
            TlsSpoofEnabled = false;
        }
        finally { _isLoadingSettings = loading; }
        _ = SaveSettingsAsync();
        DpiTuningStatus = $"Сохранён профиль «{SelectedDpiProfile.Name}». Вступит в силу при следующем подключении. " +
            "Отпечаток TLS сохраняется отдельно; профиль отключает шум и spoofing.";
    }

    private async Task TuneDpiAsync()
    {
        var node = DpiProbeNode ?? SelectedNode;
        if (node == null || IsConnected || IsLoading) return;
        bool singBox = DpiProbeSingBox;
        // These candidates only affect TCP TLS, not QUIC, plain TCP or other protocols.
        string network = (node.Network ?? "tcp").ToLowerInvariant();
        string security = (node.Security ?? "").ToLowerInvariant();
        if ((security != "tls" && security != "reality") ||
            network is not ("tcp" or "raw" or "ws" or "grpc" or "h2" or "http" or "httpupgrade" or "xhttp" or "splithttp") ||
            node.Protocol is not ("vless" or "vmess" or "trojan") ||
            !(singBox ? ProxyProtocolSupport.SupportsSingBox(node) : ProxyProtocolSupport.SupportsXray(node)))
        {
            DpiTuningStatus = "Подбор доступен для VLESS/VMess/Trojan с TLS или REALITY через TCP-транспорты, поддерживаемые выбранным ядром. QUIC/UDP требует других методов и серверной поддержки.";
            return;
        }
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        _dpiTuningCancellation = cancellation;
        DpiTrials.Clear();
        SelectedDpiTrial = null;
        OnPropertyChanged(nameof(IsDpiTuning)); OnPropertyChanged(nameof(DpiTuningSummary));
        CommandManager.InvalidateRequerySuggested();
        string report = $"{node.DisplayName} · {(singBox ? "sing-box" : "Xray")}\n";
        try
        {
            var progress = new Progress<string>(line => { DpiTuningStatus = report + line; if (!line.EndsWith("…")) report += line + "\n"; });
            var trials = await new DpiTuningService().RunAsync(node, singBox, BuildProbeOptions(), progress, cancellation.Token);
            foreach (var trial in trials) DpiTrials.Add(trial);
            var best = DpiTuningService.Recommend(trials);
            SelectedDpiTrial = best;
            if (best != null) SelectedDpiProfile = best.Profile;
            DpiTuningStatus = report + (best == null
                ? "Ни один профиль не прошёл 4/4 проверки. Это не доказывает DPI: проверьте сервер, DNS и доступность сети. Настройки сохранены без изменений."
                : $"Рекомендация: {best.Profile.Name}. Нажмите «Применить профиль», чтобы сохранить. Результат относится к этому серверу и выбранному ядру сейчас; это не гарантия обхода блокировок.");
        }
        catch (OperationCanceledException) { DpiTuningStatus = report + "Проверка остановлена или истекли 4 минуты. Настройки не изменены."; }
        catch (Exception ex) { DpiTuningStatus = "Ошибка проверки: " + ex.Message; }
        finally { _dpiTuningCancellation = null; OnPropertyChanged(nameof(IsDpiTuning)); OnPropertyChanged(nameof(DpiTuningSummary)); CommandManager.InvalidateRequerySuggested(); }
    }
}
