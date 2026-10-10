using System.Windows;
using ElyProxy.Controls.Motion;
using ElyProxy.Models;
using ElyProxy.Utils;
using Microsoft.Win32;

namespace ElyProxy.Services;

public enum AppThemeMode
{
    System,
    Light,
    Dark
}

/// <summary>
/// Owns the Apple design tokens and the motion preference. There is no window
/// backdrop: Mica/Acrylic blur was removed because it behaved poorly (stale
/// frames, inconsistent switching), so every surface is fully opaque.
/// Deliberately does not use WPF-UI's theme manager either: its theme watcher
/// re-applied a backdrop of its own, fighting the window state we set.
/// </summary>
public sealed class ThemeService
{
    private const string PersonalizeKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private static readonly Uri LightTokensUri = new("pack://application:,,,/ElyProxy;component/Theme/Tokens.Light.xaml", UriKind.Absolute);
    private static readonly Uri DarkTokensUri = new("pack://application:,,,/ElyProxy;component/Theme/Tokens.Dark.xaml", UriKind.Absolute);
    private static readonly Uri HighContrastTokensUri = new("pack://application:,,,/ElyProxy;component/Theme/Tokens.HighContrast.xaml", UriKind.Absolute);

    private bool _listening;

    private ThemeService()
    {
        SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
    }

    public static ThemeService Current { get; } = new();

    public AppThemeMode ThemeMode { get; private set; } = AppThemeMode.System;

    public MotionMode MotionMode { get; private set; } = MotionMode.System;

    public bool IsDarkTheme { get; private set; } = true;

    public event EventHandler? Changed;

    /// <summary>
    /// Applies the saved appearance before the main window is created, so the
    /// first frame already uses the right theme instead of flashing the default.
    /// </summary>
    public void PreloadFromDisk()
    {
        try
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ElyProxy",
                "settings.json");

            if (!File.Exists(path))
            {
                return;
            }

            var settings = JsonHelper.Deserialize<AppSettings>(File.ReadAllText(path));
            if (settings is null)
            {
                return;
            }

            ThemeMode = Enum.TryParse<AppThemeMode>(settings.ThemeMode, ignoreCase: true, out var theme) ? theme : AppThemeMode.System;
            MotionMode = Enum.TryParse<MotionMode>(settings.MotionMode, ignoreCase: true, out var motion) ? motion : MotionMode.System;

            ApplyAll();
        }
        catch
        {
            // A malformed settings file must never block startup.
        }
    }

    public void SetThemeMode(AppThemeMode mode)
    {
        if (ThemeMode == mode)
        {
            return;
        }

        ThemeMode = mode;
        ApplyTheme();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetMotionMode(MotionMode mode)
    {
        if (MotionMode == mode)
        {
            return;
        }

        MotionMode = mode;
        MotionPreferences.Mode = mode;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Re-applies the current theme (used on first load and after settings load).</summary>
    public void ApplyAll()
    {
        MotionPreferences.Mode = MotionMode;
        ApplyTheme();
    }

    private void ApplyTheme()
    {
        IsDarkTheme = ThemeMode switch
        {
            AppThemeMode.Light => false,
            AppThemeMode.Dark => true,
            _ => !IsSystemLightTheme()
        };

        if (ThemeMode == AppThemeMode.System)
        {
            StartListening();
        }
        else
        {
            StopListening();
        }

        SwapTokens(IsDarkTheme, SystemParameters.HighContrast);
    }

    private static bool IsSystemLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            return key?.GetValue("AppsUseLightTheme") is not int value || value != 0;
        }
        catch
        {
            return false;
        }
    }

    private void StartListening()
    {
        if (_listening)
        {
            return;
        }

        try
        {
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            _listening = true;
        }
        catch
        {
            // SystemEvents needs a message pump; without it the theme just
            // stops following Windows until the next restart.
        }
    }

    private void StopListening()
    {
        if (!_listening)
        {
            return;
        }

        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _listening = false;
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is not (UserPreferenceCategory.General or UserPreferenceCategory.VisualStyle))
        {
            return;
        }

        var app = Application.Current;
        if (app is null)
        {
            return;
        }

        app.Dispatcher.BeginInvoke(
            new Action(() =>
            {
                if (ThemeMode != AppThemeMode.System)
                {
                    return;
                }

                ApplyTheme();
                Changed?.Invoke(this, EventArgs.Empty);
            }),
            System.Windows.Threading.DispatcherPriority.Background);
    }

    private static void SwapTokens(bool dark, bool highContrast)
    {
        var app = Application.Current;
        if (app is null)
        {
            return;
        }

        var target = highContrast
            ? HighContrastTokensUri
            : dark ? DarkTokensUri : LightTokensUri;
        var marker = highContrast ? "highcontrast" : dark ? "dark" : "light";

        var dictionaries = app.Resources.MergedDictionaries;
        for (var index = 0; index < dictionaries.Count; index++)
        {
            var source = dictionaries[index].Source?.OriginalString;
            if (source is null || !source.Contains("tokens.", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (source.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            // Assigning Source on the existing dictionary invalidates every
            // DynamicResource that resolved through it.
            dictionaries[index].Source = target;
            return;
        }

        dictionaries.Add(new ResourceDictionary { Source = target });
    }

    private void OnSystemParametersChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (string.Equals(e.PropertyName, nameof(SystemParameters.ClientAreaAnimation), StringComparison.Ordinal))
        {
            MotionPreferences.NotifySystemSettingChanged();
            return;
        }

        if (string.Equals(e.PropertyName, nameof(SystemParameters.HighContrast), StringComparison.Ordinal))
        {
            ApplyAll();
        }
    }
}
