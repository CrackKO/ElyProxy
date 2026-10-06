using System.Windows;
using System.Windows.Media;
using ElyProxy.Controls.Motion;
using ElyProxy.Interop;
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

public enum AppBackdropMode
{
    Auto,
    Mica,
    Acrylic,
    None
}

/// <summary>
/// Owns the Apple design tokens, the window backdrop and the motion preference.
/// Deliberately does not use WPF-UI's theme manager: its backdrop helper left the
/// accent policy applied (so switching stuck) and its theme watcher re-applied a
/// backdrop of its own, fighting the window state we set.
/// </summary>
public sealed class ThemeService
{
    private const string PersonalizeKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private static readonly Uri LightTokensUri = new("pack://application:,,,/ElyProxy;component/Theme/Tokens.Light.xaml", UriKind.Absolute);
    private static readonly Uri DarkTokensUri = new("pack://application:,,,/ElyProxy;component/Theme/Tokens.Dark.xaml", UriKind.Absolute);
    private static readonly Uri HighContrastTokensUri = new("pack://application:,,,/ElyProxy;component/Theme/Tokens.HighContrast.xaml", UriKind.Absolute);

    private Window? _window;
    private bool _listening;

    private ThemeService()
    {
        SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
    }

    public static ThemeService Current { get; } = new();

    public AppThemeMode ThemeMode { get; private set; } = AppThemeMode.System;

    public AppBackdropMode BackdropMode { get; private set; } = AppBackdropMode.Auto;

    public MotionMode MotionMode { get; private set; } = MotionMode.System;

    public bool IsDarkTheme { get; private set; } = true;

    public event EventHandler? Changed;

    public void Attach(Window window)
    {
        if (_window is not null)
        {
            _window.SourceInitialized -= OnWindowSourceInitialized;
        }

        _window = window;
        window.SourceInitialized += OnWindowSourceInitialized;
        ApplyAll();
    }

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
            BackdropMode = Enum.TryParse<AppBackdropMode>(settings.BackdropMode, ignoreCase: true, out var backdrop) ? backdrop : AppBackdropMode.Auto;
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

    public void SetBackdropMode(AppBackdropMode mode)
    {
        if (BackdropMode == mode)
        {
            return;
        }

        BackdropMode = mode;
        ApplyBackdrop();
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
        ApplyBackdrop();
    }

    private BackdropKind ResolveBackdrop()
    {
        if (BackdropMode == AppBackdropMode.None
            || SystemParameters.HighContrast
            || !MotionPreferences.SystemTransparencyEnabled)
        {
            return BackdropKind.None;
        }

        var isWindows11 = Environment.OSVersion.Version.Build >= 22000;

        return BackdropMode switch
        {
            // Mica needs Windows 11; on Windows 10 fall back to Acrylic.
            AppBackdropMode.Mica => isWindows11 ? BackdropKind.Mica : BackdropKind.Acrylic,
            AppBackdropMode.Acrylic => BackdropKind.Acrylic,
            _ => isWindows11 ? BackdropKind.Mica : BackdropKind.Acrylic
        };
    }

    private void ApplyBackdrop()
    {
        var kind = ResolveBackdrop();

        // With a real backdrop the shell surfaces stay translucent (the blur
        // shows through); without one they become opaque so the difference is
        // actually visible instead of "nothing changed".
        SetShellSurface(kind != BackdropKind.None);

        var window = _window;
        if (window is null)
        {
            return;
        }

        // Keep the WPF host transparent in every mode. Giving it a nearly
        // transparent theme-coloured brush creates a second composition surface
        // on Windows 10, which is what caused stale light/dark frames with Acrylic.
        window.Background = Brushes.Transparent;

        var strongerBlur = BackdropMode == AppBackdropMode.Acrylic;
        WindowBackdropInterop.Apply(window, kind, IsDarkTheme, strongerBlur);
        ForceRepaint(window);
    }

    private void OnWindowSourceInitialized(object? sender, EventArgs e)
    {
        if (sender is Window window && ReferenceEquals(window, _window))
        {
            ApplyBackdrop();
        }
    }

    private static void SetShellSurface(bool translucent)
    {
        var app = Application.Current;
        if (app is null)
        {
            return;
        }

        var brush = (translucent
            ? app.TryFindResource("MaterialThinBrush")
            : app.TryFindResource("Bg2Brush")) as Brush;

        if (brush is not null)
        {
            app.Resources["ShellSurfaceBrush"] = brush;
        }
    }

    private static void ForceRepaint(Window window)
    {
        window.InvalidateVisual();
        if (window.Content is UIElement content)
        {
            content.InvalidateVisual();
        }

        window.UpdateLayout();
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
