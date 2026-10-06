using ElyProxy.Models;
using Newtonsoft.Json;

namespace ElyProxy.Services;

public sealed class WindowsProxyRecoveryService
{
    private readonly IWindowsProxyService _windowsProxyService;
    private readonly string _statePath;

    public WindowsProxyRecoveryState? ActiveState { get; private set; }

    public WindowsProxyRecoveryService(IWindowsProxyService windowsProxyService, StorageService storageService)
    {
        _windowsProxyService = windowsProxyService;
        _statePath = Path.Combine(storageService.DataDirectory, "windows-proxy-recovery.json");
    }

    public void PrepareForPac(string pacUrl)
    {
        WindowsProxyRecoveryState state = GetOrCreateState(WindowsProxyRecoveryMode.Pac);
        state.OwnedManualPort = null;
        state.OwnedBypassRules = Array.Empty<string>();
        if (!state.OwnedPacUrls.Contains(pacUrl, StringComparer.OrdinalIgnoreCase))
        {
            state.OwnedPacUrls.Add(pacUrl);
        }

        ActiveState = state;
        SaveState(state);
    }

    public void PrepareForManualProxy(int port, IEnumerable<string> bypassRules)
    {
        WindowsProxyRecoveryState state = GetOrCreateState(WindowsProxyRecoveryMode.Manual);
        state.OwnedPacUrls.Clear();
        state.OwnedManualPort = port;
        state.OwnedBypassRules = bypassRules.ToArray();
        ActiveState = state;
        SaveState(state);
    }

    public WindowsProxyRecoveryResult RecoverPendingAtStartup()
    {
        if (!File.Exists(_statePath))
        {
            ActiveState = null;
            return new WindowsProxyRecoveryResult(WindowsProxyRecoveryStatus.NoPendingRecovery);
        }

        try
        {
            WindowsProxyRecoveryState state = ReadState();
            WindowsProxySnapshot current = _windowsProxyService.CaptureCurrentSettings();
            if (IsOwnedByElyProxy(state, current))
            {
                RestoreOriginalSettings(state);
                ClearState();
                return new WindowsProxyRecoveryResult(
                    WindowsProxyRecoveryStatus.Restored,
                    "Аварийное завершение обнаружено; прежние настройки прокси Windows восстановлены.");
            }

            ClearState();
            return new WindowsProxyRecoveryResult(
                WindowsProxyRecoveryStatus.UserSettingsPreserved,
                "Настройки прокси изменились после работы ElyProxy и оставлены без изменений.");
        }
        catch (Exception ex)
        {
            return new WindowsProxyRecoveryResult(
                WindowsProxyRecoveryStatus.Failed,
                $"Не удалось восстановить настройки прокси Windows. Файл восстановления сохранён для следующего запуска. {ex.Message}");
        }
    }

    public WindowsProxyRecoveryResult RestoreActive()
    {
        WindowsProxyRecoveryState? state = ActiveState;
        try
        {
            state ??= File.Exists(_statePath) ? ReadState() : null;
            if (state == null)
            {
                return new WindowsProxyRecoveryResult(WindowsProxyRecoveryStatus.NoPendingRecovery);
            }

            WindowsProxySnapshot current = _windowsProxyService.CaptureCurrentSettings();
            if (IsOwnedByElyProxy(state, current))
            {
                RestoreOriginalSettings(state);
                ClearState();
                return new WindowsProxyRecoveryResult(WindowsProxyRecoveryStatus.Restored);
            }

            ClearState();
            return new WindowsProxyRecoveryResult(
                WindowsProxyRecoveryStatus.UserSettingsPreserved,
                "Настройки прокси изменились вручную и оставлены без изменений.");
        }
        catch (Exception ex)
        {
            return new WindowsProxyRecoveryResult(
                WindowsProxyRecoveryStatus.Failed,
                $"Не удалось восстановить прежние настройки прокси Windows. {ex.Message}");
        }
    }

    private WindowsProxyRecoveryState GetOrCreateState(WindowsProxyRecoveryMode mode)
    {
        if (ActiveState != null)
        {
            if (ActiveState.Mode != mode)
            {
                throw new InvalidOperationException("Сначала восстановите незавершённые настройки системного прокси Windows.");
            }

            WindowsProxySnapshot current = _windowsProxyService.CaptureCurrentSettings();
            if (!IsOwnedByElyProxy(ActiveState, current) && current != ActiveState.OriginalSettings)
            {
                throw new InvalidOperationException("Настройки прокси Windows изменились вручную. ElyProxy оставил их без изменений и не будет включать режим поверх них.");
            }

            return ActiveState;
        }

        if (File.Exists(_statePath))
        {
            throw new InvalidOperationException("Сначала восстановите незавершённые настройки системного прокси Windows.");
        }

        return new WindowsProxyRecoveryState
        {
            Mode = mode,
            OriginalSettings = _windowsProxyService.CaptureCurrentSettings()
        };
    }

    private bool IsOwnedByElyProxy(WindowsProxyRecoveryState state, WindowsProxySnapshot current) =>
        state.Mode switch
        {
            WindowsProxyRecoveryMode.Pac => _windowsProxyService.IsOwnedByPac(current, state.OwnedPacUrls),
            WindowsProxyRecoveryMode.Manual => state.OwnedManualPort.HasValue
                && _windowsProxyService.IsOwnedByManualProxy(current, state.OwnedManualPort.Value, state.OwnedBypassRules),
            _ => false
        };

    private void RestoreOriginalSettings(WindowsProxyRecoveryState state)
    {
        if (state.Mode == WindowsProxyRecoveryMode.Pac)
        {
            // PAC mode changes only AutoConfigURL; preserve any other Windows proxy edits made later.
            _windowsProxyService.RestorePacSettings(state.OriginalSettings);
            return;
        }

        _windowsProxyService.RestoreManualProxySettings(state.OriginalSettings);
    }

    private WindowsProxyRecoveryState ReadState()
    {
        WindowsProxyRecoveryState? state = JsonConvert.DeserializeObject<WindowsProxyRecoveryState>(File.ReadAllText(_statePath));
        if (state == null || state.SchemaVersion != 1)
        {
            throw new InvalidDataException("Файл восстановления прокси имеет неподдерживаемый формат.");
        }

        return state;
    }

    private void SaveState(WindowsProxyRecoveryState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_statePath)!);
        WriteAtomic(_statePath, JsonConvert.SerializeObject(state, Formatting.Indented));
    }

    private void ClearState()
    {
        if (File.Exists(_statePath))
        {
            File.Delete(_statePath);
        }

        ActiveState = null;
    }

    private static void WriteAtomic(string path, string content)
    {
        string temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(content);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(path))
            {
                File.Replace(temporaryPath, path, null);
            }
            else
            {
                File.Move(temporaryPath, path);
            }
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
