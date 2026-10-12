using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace ElyProxy.Services;

public static class ZapretCompatibilityService
{
    public static async Task ApplyAsync(bool restore)
    {
        string helper = HelperServiceInstaller.GetHelperPath();
        if (!File.Exists(helper))
            throw new FileNotFoundException("Не найден ElyProxy.Helper.exe. Переустановите ElyProxy.");
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = helper,
            Arguments = restore ? "zapret-restore" : "zapret-enable",
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden
        }) ?? throw new InvalidOperationException("Не удалось запустить настройку Zapret.");
        await process.WaitForExitAsync();
        if (process.ExitCode == 0) return;
        throw new InvalidOperationException(process.ExitCode switch
        {
            2 => "Служба zapret не установлена. Эта настройка предназначена для Zapret, установленного как служба Windows.",
            3 => "Конфигурация Zapret не поддерживается или не прошла проверку. Нужна служба, запускающая winws.exe напрямую, с поддержкой --wf-raw и --wf-save, без собственного --wf-raw. Также проверьте наличие физического сетевого адаптера. Подробности: %ProgramData%\\ElyProxy\\zapret-compatibility.log.",
            4 => "Настройки Zapret изменены другой программой после настройки совместимости. Они не были перезаписаны. Сначала восстановите прежнюю конфигурацию Zapret вручную.",
            5 => "Не удалось восстановить Zapret после ошибки. Проверьте службу zapret; исходная командная строка сохранена в её параметре ElyProxyOriginalImagePath в реестре Windows.",
            6 => "Другая настройка Zapret уже выполняется. Дождитесь её завершения.",
            _ => "Не удалось настроить Zapret. Проверьте права администратора и состояние службы. Подробности: %ProgramData%\\ElyProxy\\zapret-compatibility.log. При ошибке изменения автоматически отменяются; при сбое восстановления выводится отдельное сообщение."
        });
    }
}
