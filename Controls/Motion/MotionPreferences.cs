using System.Windows;

namespace ElyProxy.Controls.Motion;

/// <summary>
/// Resolves whether motion should be reduced. Mirrors the web
/// <c>prefers-reduced-motion</c> media feature using the Windows
/// "animate controls and elements inside windows" setting plus the
/// user's in-app preference.
/// </summary>
public static class MotionPreferences
{
    private static MotionMode _mode = MotionMode.System;

    public static event EventHandler? Changed;

    public static MotionMode Mode
    {
        get => _mode;
        set
        {
            if (_mode == value)
            {
                return;
            }

            _mode = value;
            Changed?.Invoke(null, EventArgs.Empty);
        }
    }

    /// <summary>Windows accessibility setting for UI animations.</summary>
    public static bool SystemAnimationsEnabled => SystemParameters.ClientAreaAnimation;

    public static bool ReducedMotion =>
        _mode == MotionMode.Off || (_mode == MotionMode.System && !SystemAnimationsEnabled);

    /// <summary>Windows "Transparency effects" personalization toggle.</summary>
    public static bool SystemTransparencyEnabled
    {
        get
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                return key?.GetValue("EnableTransparency") is not int value || value != 0;
            }
            catch
            {
                return true;
            }
        }
    }

    public static void NotifySystemSettingChanged() => Changed?.Invoke(null, EventArgs.Empty);
}

public enum MotionMode
{
    System,
    On,
    Off
}
