using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using SevenLive.Models;

namespace SevenLive.Services;

public sealed class WindowEnumerator
{
    private const int GwlExStyle = -20;
    private const long WsExToolWindow = 0x00000080L;
    private const int DwmwaCloaked = 14;
    private const int MaxProcessNameCache = 512;

    private readonly Dictionary<int, string> _processNames = new();

    public IReadOnlyList<WindowInfo> GetOpenWindows(bool includeUntitled = false)
    {
        var foreground = GetForegroundWindow();
        var windows = new List<WindowInfo>();

        EnumWindows((handle, _) =>
        {
            if (!IsWindowVisible(handle))
            {
                return true;
            }

            if (IsCloaked(handle))
            {
                return true;
            }

            if (!includeUntitled && IsToolWindow(handle))
            {
                return true;
            }

            if (!includeUntitled && string.IsNullOrWhiteSpace(GetTitle(handle)))
            {
                return true;
            }

            var info = CreateWindowInfo(handle, foreground);
            if (info != null)
            {
                windows.Add(info);
            }

            return true;
        }, IntPtr.Zero);

        return windows;
    }

    public WindowInfo? GetForegroundWindowInfo()
    {
        var handle = GetForegroundWindow();
        return handle == IntPtr.Zero ? null : CreateWindowInfo(handle, handle);
    }

    public WindowInfo? FindWindow(string selector)
    {
        if (string.IsNullOrWhiteSpace(selector))
        {
            return null;
        }

        var foreground = GetForegroundWindow();
        var candidates = new List<WindowInfo>();
        EnumWindows((handle, _) =>
        {
            if (!IsWindowVisible(handle) || IsCloaked(handle))
            {
                return true;
            }

            var info = CreateWindowInfo(handle, foreground);
            if (info != null)
            {
                candidates.Add(info);
            }

            return true;
        }, IntPtr.Zero);

        if (selector.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            && long.TryParse(selector.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex))
        {
            var handle = new IntPtr(hex);
            if (!IsWindow(handle))
            {
                return null;
            }

            return candidates.FirstOrDefault(window => window.Handle == handle) ?? CreateWindowInfo(handle, foreground);
        }

        if (int.TryParse(selector, NumberStyles.Integer, CultureInfo.InvariantCulture, out var processId))
        {
            return candidates.FirstOrDefault(window => window.ProcessId == processId && !string.IsNullOrWhiteSpace(window.Title))
                ?? candidates.FirstOrDefault(window => window.ProcessId == processId);
        }

        return candidates.FirstOrDefault(window => window.ProcessName.Contains(selector, StringComparison.OrdinalIgnoreCase))
            ?? candidates.FirstOrDefault(window => window.Title.Contains(selector, StringComparison.OrdinalIgnoreCase));
    }

    private WindowInfo? CreateWindowInfo(IntPtr handle, IntPtr foreground)
    {
        try
        {
            GetWindowThreadProcessId(handle, out var processId);
            GetWindowRect(handle, out var rect);
            var bounds = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
            return new WindowInfo(
                handle,
                (int)processId,
                GetProcessName((int)processId),
                GetTitle(handle),
                handle == foreground,
                bounds);
        }
        catch
        {
            return null;
        }
    }

    private string GetProcessName(int processId)
    {
        if (_processNames.TryGetValue(processId, out var cached))
        {
            return cached;
        }

        string name;
        try
        {
            using var process = Process.GetProcessById(processId);
            name = process.ProcessName;
        }
        catch
        {
            name = "unknown";
        }

        if (_processNames.Count >= MaxProcessNameCache)
        {
            _processNames.Clear();
        }

        _processNames[processId] = name;
        return name;
    }

    private static string GetTitle(IntPtr handle)
    {
        var length = GetWindowTextLength(handle);
        if (length <= 0)
        {
            return string.Empty;
        }

        var buffer = new StringBuilder(length + 2);
        GetWindowText(handle, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    private static bool IsToolWindow(IntPtr handle)
    {
        var style = IntPtr.Size == 8
            ? GetWindowLongPtr64(handle, GwlExStyle)
            : new IntPtr(GetWindowLong32(handle, GwlExStyle));
        return (style.ToInt64() & WsExToolWindow) != 0;
    }

    private static bool IsCloaked(IntPtr handle)
    {
        try
        {
            return DwmGetWindowAttribute(handle, DwmwaCloaked, out var cloaked, sizeof(int)) == 0 && cloaked != 0;
        }
        catch
        {
            return false;
        }
    }

    private delegate bool EnumWindowsProc(IntPtr handle, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr handle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr handle, StringBuilder text, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr handle, out uint processId);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr handle, out NativeRect rect);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong32(IntPtr handle, int index);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr handle, int index);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr handle, int attribute, out int value, int size);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
