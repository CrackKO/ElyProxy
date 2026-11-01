using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace ElyProxy.Interop;

/// <summary>
/// Custom window chrome helpers: native Windows 11 Snap Layouts over the
/// maximize button, work-area aware maximization (a borderless WindowChrome
/// window otherwise maximizes under the taskbar) and mouse caption dragging.
/// </summary>
public static class WindowChromeHelper
{
    private const int WmNcHitTest = 0x0084;
    private const int WmGetMinMaxInfo = 0x0024;
    private const int HtMaxButton = 8;
    private const uint MonitorDefaultToNearest = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public NativePoint Reserved;
        public NativePoint MaxSize;
        public NativePoint MaxPosition;
        public NativePoint MinTrackSize;
        public NativePoint MaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }

    public static void Configure(Window window, FrameworkElement maximizeButton)
    {
        if (PresentationSource.FromVisual(window) is HwndSource source)
        {
            Attach(source, window, maximizeButton);
            return;
        }

        void OnSourceInitialized(object? sender, EventArgs e)
        {
            window.SourceInitialized -= OnSourceInitialized;
            if (PresentationSource.FromVisual(window) is HwndSource initializedSource)
            {
                Attach(initializedSource, window, maximizeButton);
            }
        }

        window.SourceInitialized += OnSourceInitialized;
    }

    private static void Attach(HwndSource source, Window window, FrameworkElement maximizeButton)
    {
        // Snap Layouts only exist on Windows 11; the work-area fix is needed everywhere.
        var supportsSnapLayouts = Environment.OSVersion.Version.Build >= 22000;

        source.AddHook((IntPtr hwnd, int message, IntPtr _, IntPtr lParam, ref bool handled) =>
        {
            if (message == WmGetMinMaxInfo)
            {
                ApplyWorkArea(hwnd, window, lParam);
                handled = true;
                return IntPtr.Zero;
            }

            if (!supportsSnapLayouts
                || message != WmNcHitTest
                || !maximizeButton.IsVisible
                || maximizeButton.ActualWidth <= 0)
            {
                return IntPtr.Zero;
            }

            var raw = lParam.ToInt64();
            var screenPoint = new Point((short)(raw & 0xFFFF), (short)((raw >> 16) & 0xFFFF));
            var origin = maximizeButton.PointToScreen(new Point(0, 0));
            var bounds = new Rect(origin, new Size(maximizeButton.ActualWidth, maximizeButton.ActualHeight));

            if (bounds.Contains(screenPoint))
            {
                handled = true;
                return new IntPtr(HtMaxButton);
            }

            return IntPtr.Zero;
        });
    }

    private static void ApplyWorkArea(IntPtr hwnd, Window window, IntPtr lParam)
    {
        var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero)
        {
            return;
        }

        var monitorInfo = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref monitorInfo))
        {
            return;
        }

        var info = Marshal.PtrToStructure<MinMaxInfo>(lParam);
        info.MaxPosition.X = monitorInfo.Work.Left - monitorInfo.Monitor.Left;
        info.MaxPosition.Y = monitorInfo.Work.Top - monitorInfo.Monitor.Top;
        info.MaxSize.X = monitorInfo.Work.Right - monitorInfo.Work.Left;
        info.MaxSize.Y = monitorInfo.Work.Bottom - monitorInfo.Work.Top;

        var dpi = VisualTreeHelper.GetDpi(window);
        info.MinTrackSize.X = (int)Math.Ceiling(window.MinWidth * dpi.DpiScaleX);
        info.MinTrackSize.Y = (int)Math.Ceiling(window.MinHeight * dpi.DpiScaleY);

        Marshal.StructureToPtr(info, lParam, fDeleteOld: true);
    }

    public static void DragWindow(Window window)
    {
        if (Mouse.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        try
        {
            window.DragMove();
        }
        catch (InvalidOperationException)
        {
            // DragMove can race with a released button; nothing to do.
        }
    }

    public static void ToggleMaximize(Window window)
    {
        window.WindowState = window.WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
}
