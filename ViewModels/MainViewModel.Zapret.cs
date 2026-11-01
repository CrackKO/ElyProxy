using System;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows.Input;
using ElyProxy.Services;

namespace ElyProxy.ViewModels;

public partial class MainViewModel
{
    private ICommand? _configureZapretCommand;
    private ICommand? _restoreZapretCommand;
    private bool _isZapretBusy;
    private string _zapretCompatibilityStatus = "Если с включённым Zapret не работают YouTube или Discord в ElyTun, настройте совместимость.";

    public ICommand ConfigureZapretCommand => _configureZapretCommand ??= new AsyncRelayCommand(() => ConfigureZapretAsync(false), () => !_isZapretBusy && !IsLoading);
    public ICommand RestoreZapretCommand => _restoreZapretCommand ??= new AsyncRelayCommand(() => ConfigureZapretAsync(true), () => !_isZapretBusy && !IsLoading);
    public string ZapretCompatibilityStatus
    {
        get => _zapretCompatibilityStatus;
        private set => SetProperty(ref _zapretCompatibilityStatus, value, nameof(ZapretCompatibilityStatus));
    }

    private async Task ConfigureZapretAsync(bool restore)
    {
        if (_isZapretBusy) return;
        _isZapretBusy = true;
        CommandManager.InvalidateRequerySuggested();
        ZapretCompatibilityStatus = "Ожидание подтверждения Windows и настройка Zapret…";
        try
        {
            await ZapretCompatibilityService.ApplyAsync(restore);
            ZapretCompatibilityStatus = restore
                ? "Исходная конфигурация Zapret восстановлена."
                : "Zapret настроен на физические адаптеры. Переподключите ElyTun и перезапустите соединение в Discord. При добавлении сетевого адаптера или переустановке Zapret повторите настройку.";
            AppendLog("[sys] " + ZapretCompatibilityStatus);
            DialogService.Current.Notify("Совместимость с Zapret", ZapretCompatibilityStatus, NoticeKind.Info);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            ZapretCompatibilityStatus = "Настройка отменена в окне Windows.";
        }
        catch (Exception ex)
        {
            ZapretCompatibilityStatus = ex.Message;
            AppendLog("[err] Настройка Zapret: " + ex.Message);
            await DialogService.Current.AlertAsync("Совместимость с Zapret", ex.Message);
        }
        finally
        {
            _isZapretBusy = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }
}
