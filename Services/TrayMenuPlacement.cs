using System.Windows;

namespace ElyProxy.Services;

/// <summary>All coordinates are physical pixels on the cursor's monitor.</summary>
public static class TrayMenuPlacement
{
    public static Point Calculate(Rect workArea, Point cursor, Size menu, double margin)
    {
        var left = cursor.X - menu.Width / 2;
        var top = cursor.Y - menu.Height - margin;
        if (top < workArea.Top + margin) top = cursor.Y + margin;
        return new Point(
            Math.Clamp(left, workArea.Left + margin, Math.Max(workArea.Left + margin, workArea.Right - menu.Width - margin)),
            Math.Clamp(top, workArea.Top + margin, Math.Max(workArea.Top + margin, workArea.Bottom - menu.Height - margin)));
    }
}
