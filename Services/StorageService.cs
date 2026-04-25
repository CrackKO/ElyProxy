using ElyProxy.Models;
using ElyProxy.Utils;

namespace ElyProxy.Services;

public class StorageService
{
    private static readonly string BasePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ElyProxy");

    private string SubscriptionsPath => Path.Combine(BasePath, "subscriptions.json");
    private string ProfilesPath => Path.Combine(BasePath, "profiles.json");
    private string SettingsPath => Path.Combine(BasePath, "settings.json");

    public StorageService()
    {
        Directory.CreateDirectory(BasePath);
    }

    public async Task<List<Subscription>> LoadSubscriptionsAsync()
    {
        return await JsonHelper.LoadFromFileAsync<List<Subscription>>(SubscriptionsPath) ?? new();
    }

    public async Task SaveSubscriptionsAsync(IEnumerable<Subscription> subscriptions)
    {
        await JsonHelper.SaveToFileAsync(subscriptions.ToList(), SubscriptionsPath);
    }

    public async Task<List<ProxyProfile>> LoadProfilesAsync()
    {
        return await JsonHelper.LoadFromFileAsync<List<ProxyProfile>>(ProfilesPath) ?? new();
    }

    public async Task SaveProfilesAsync(IEnumerable<ProxyProfile> profiles)
    {
        await JsonHelper.SaveToFileAsync(profiles.ToList(), ProfilesPath);
    }

    public async Task<AppSettings> LoadSettingsAsync()
    {
        return await JsonHelper.LoadFromFileAsync<AppSettings>(SettingsPath) ?? new();
    }

    public async Task SaveSettingsAsync(AppSettings settings)
    {
        await JsonHelper.SaveToFileAsync(settings, SettingsPath);
    }
}
