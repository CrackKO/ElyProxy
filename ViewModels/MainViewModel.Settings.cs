using System.Collections.ObjectModel;
using ElyProxy.Services;

namespace ElyProxy.ViewModels;

public partial class MainViewModel
{
    public sealed record SettingsCoreVersion(string Id, string Name, string Version, string Status);
    public ObservableCollection<SettingsCoreVersion> SettingsCoreVersions { get; } = new(
        CoreUpdateService.CoreIds.Select(id => new SettingsCoreVersion(id, CoreUpdateService.GetDisplayName(id), "—", "Не проверено")));

    private bool _settingsVersionsReading;
    public async Task RefreshSettingsCoreVersionsAsync()
    {
        if (!_canSaveSettings || _settingsVersionsReading || IsCoreUpdateBusy) return;
        _settingsVersionsReading = true;
        try
        {
            foreach (string id in CoreUpdateService.CoreIds)
                SetSettingsCoreVersion(id, await CoreVersionService.GetInstalledVersionAsync(id));
        }
        finally { _settingsVersionsReading = false; }
    }

    private void SetSettingsCoreVersion(string id, string version)
    {
        var previous = SettingsCoreVersions.FirstOrDefault(row => row.Id == id);
        if (previous == null) return;
        SettingsCoreVersions[SettingsCoreVersions.IndexOf(previous)] = previous with
        {
            Version = version,
            Status = version == "не установлено" ? "Не установлено" : version == "неизвестно" ? "Версия неизвестна" : "Установлено"
        };
    }
}
