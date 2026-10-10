using ElyProxy.Controls.Motion;
using ElyProxy.Services;

namespace ElyProxy.ViewModels;

/// <summary>
/// UI-only additions to the (decompiled) main view model: appearance settings
/// backed by ThemeService and the option lists for the settings screen.
/// Kept in a separate partial file so the generated code stays untouched.
/// </summary>
public partial class MainViewModel
{
    private string _themeMode = "System";
    private string _motionMode = "System";
    private string _serverBadgeMode = "Flag";

    public IReadOnlyList<KeyValuePair<string, string>> ServerBadgeOptions { get; } = new[]
    {
        new KeyValuePair<string, string>("Flag", "Флаг — код страны"),
        new KeyValuePair<string, string>("Monogram", "Сокращение — две буквы"),
        new KeyValuePair<string, string>("None", "Ничего")
    };

    public string ServerBadgeMode
    {
        get => _serverBadgeMode;
        set
        {
            var normalized = Normalize(value, "Flag", "Monogram", "None");
            if (SetProperty(ref _serverBadgeMode, normalized, nameof(ServerBadgeMode)) && !_isLoadingSettings)
            {
                _ = SaveSettingsAsync();
            }
        }
    }

    public IReadOnlyList<KeyValuePair<string, string>> ThemeModeOptions { get; } = new[]
    {
        new KeyValuePair<string, string>("System", "Как в системе"),
        new KeyValuePair<string, string>("Light", "Светлая"),
        new KeyValuePair<string, string>("Dark", "Тёмная")
    };

    public IReadOnlyList<KeyValuePair<string, string>> MotionModeOptions { get; } = new[]
    {
        new KeyValuePair<string, string>("System", "Как в системе"),
        new KeyValuePair<string, string>("On", "Включены"),
        new KeyValuePair<string, string>("Off", "Выключены")
    };

    public string ThemeMode
    {
        get => _themeMode;
        set
        {
            var normalized = Normalize(value, "System", "Light", "Dark");
            if (SetProperty(ref _themeMode, normalized, nameof(ThemeMode)))
            {
                ApplyAppearanceSettings();
            }
        }
    }

    public string MotionMode
    {
        get => _motionMode;
        set
        {
            var normalized = Normalize(value, "System", "On", "Off");
            if (SetProperty(ref _motionMode, normalized, nameof(MotionMode)))
            {
                ApplyAppearanceSettings();
            }
        }
    }

    private void ApplyAppearanceSettings()
    {
        ThemeService.Current.SetThemeMode(ParseMode(_themeMode, AppThemeMode.System));
        ThemeService.Current.SetMotionMode(ParseMode(_motionMode, Controls.Motion.MotionMode.System));

        if (!_isLoadingSettings)
        {
            _ = SaveSettingsAsync();
        }
    }

    private static string Normalize(string? value, params string[] allowed)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return allowed[0];
        }

        foreach (var candidate in allowed)
        {
            if (string.Equals(candidate, value, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        return allowed[0];
    }

    private static T ParseMode<T>(string value, T fallback) where T : struct, Enum =>
        Enum.TryParse<T>(value, ignoreCase: true, out var parsed) ? parsed : fallback;
}
