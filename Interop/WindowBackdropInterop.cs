using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace ElyProxy.Interop;

public enum BackdropKind
{
    None,
    Mica,
    Acrylic
}

/// <summary>
/// Backdrop switching done directly through DWM/composition instead of relying
/// on WPF-UI's helper, because that helper leaves the accent policy applied and
/// switching between Mica/Acrylic/None stops working after the first change.
/// Every apply clears the previous state first and forces a recomposition.
/// </summary>
public static class WindowBackdropInterop
{
    private const int WcaAccentPolicy = 19;
    private const int AccentDisabled = 0;
    private const int AccentEnableAcrylicBlurBehind = 4;

    private const int DwmwaSystemBackdropType = 38;
    private const int DwmsbtNone = 1;
    private const int DwmsbtMainWindow = 2;
    private const int DwmsbtTransientWindow = 3;

    private const uint RdwInvalidate = 0x0001;
    private const uint RdwAllChildren = 0x0080;
    private const uint RdwUpdateNow = 0x0100;
    private const uint RdwFrame = 0x0400;

    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public int AccentState;
        public int AccentFlags;
        public int GradientColor;
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public int Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    /// <summary>
    /// Applies the requested backdrop. On Windows 10 the acrylic tint is kept
    /// light on purpose: an opaque tint hides the blur completely, which made
    /// "Mica/Acrylic/Off" look identical.
    /// </summary>
    public static void Apply(Window window, BackdropKind kind, bool darkTheme, bool strongerBlur = false)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var isWindows11 = Environment.OSVersion.Version.Build >= 22000;
        // Clear the old composition state first. Replacing an Acrylic accent in
        // place is unreliable on Windows 10 and leaves a cached old theme frame.
        SetAccent(hwnd, new AccentPolicy { AccentState = AccentDisabled });

        var applied = false;

        if (isWindows11)
        {
            var value = kind switch
            {
                BackdropKind.Mica => DwmsbtMainWindow,
                BackdropKind.Acrylic => DwmsbtTransientWindow,
                _ => DwmsbtNone
            };

            applied = DwmSetWindowAttribute(hwnd, DwmwaSystemBackdropType, ref value, sizeof(int)) == 0;
        }

        if (!applied && kind != BackdropKind.None)
        {
            // Windows 10, or a Windows 11 build without the backdrop attribute:
            // use the composition accent instead (acrylic blur).
            var alpha = strongerBlur ? 0x40 : 0x59;
            var tint = darkTheme
                ? unchecked((int)((uint)alpha << 24))
                : unchecked((int)(((uint)alpha << 24) | 0xFFFFFF));

            SetAccent(hwnd, new AccentPolicy
            {
                AccentState = AccentEnableAcrylicBlurBehind,
                AccentFlags = 0,
                GradientColor = tint
            });
        }

        // Force DWM to recompose, otherwise the backdrop stays stuck after a switch.
        SetWindowPos(
            hwnd,
            IntPtr.Zero,
            0,
            0,
            0,
            0,
            SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);

        // And force the window (including children) to repaint right away, so the
        // new palette is not left waiting for the next resize.
        RedrawWindow(
            hwnd,
            IntPtr.Zero,
            IntPtr.Zero,
            RdwInvalidate | RdwFrame | RdwAllChildren | RdwUpdateNow);
    }

    private static void SetAccent(IntPtr hwnd, AccentPolicy policy)
    {
        var size = Marshal.SizeOf<AccentPolicy>();
        var pointer = Marshal.AllocHGlobal(size);

        try
        {
            Marshal.StructureToPtr(policy, pointer, false);
            var data = new WindowCompositionAttributeData
            {
                Attribute = WcaAccentPolicy,
                Data = pointer,
                SizeOfData = size
            };

            SetWindowCompositionAttribute(hwnd, ref data);
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
        }
    }

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern bool RedrawWindow(IntPtr hwnd, IntPtr updateRect, IntPtr updateRegion, uint flags);
}
