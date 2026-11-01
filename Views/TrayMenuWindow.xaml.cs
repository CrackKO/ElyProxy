using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace ElyProxy.Views;

/// <summary>
/// Opaque tray menu. A borderless popup anchored to the cursor
/// and closed as soon as it loses focus, like a macOS status menu.
/// </summary>
public partial class TrayMenuWindow : Window
{
    public TrayMenuWindow()
    {
        InitializeComponent();
        Deactivated += (_, _) => Close();
        PreviewKeyDown += OnPreviewKeyDown;
    }

    public event EventHandler? ShowRequested;

    public event EventHandler? ExitRequested;

    public event EventHandler<Models.TunCoreKind>? TunCoreRequested;

    public void ShowAtCursor()
    {
        // Create the HWND on the cursor's monitor first, so WPF obtains its DPI.
        // DesiredSize before Show is not the final SizeToContent window size.
        var cursor = System.Windows.Forms.Cursor.Position;
        var area = System.Windows.Forms.Screen.FromPoint(cursor).WorkingArea;
        Opacity = 0;
        WindowStartupLocation = WindowStartupLocation.Manual;
        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        _ = SetWindowPos(hwnd, IntPtr.Zero, cursor.X, cursor.Y, 0, 0, 0x0015);
        var dpi = VisualTreeHelper.GetDpi(this);
        MaxWidth = Math.Max(1, area.Width / dpi.DpiScaleX - 16);
        MaxHeight = Math.Max(1, area.Height / dpi.DpiScaleY - 16);
        Show();
        UpdateLayout();
        dpi = VisualTreeHelper.GetDpi(this);
        var position = Services.TrayMenuPlacement.Calculate(
            new Rect(area.Left, area.Top, area.Width, area.Height),
            new Point(cursor.X, cursor.Y),
            new Size(ActualWidth * dpi.DpiScaleX, ActualHeight * dpi.DpiScaleY),
            8 * Math.Max(dpi.DpiScaleX, dpi.DpiScaleY));
        _ = SetWindowPos(hwnd, IntPtr.Zero, (int)Math.Round(position.X), (int)Math.Round(position.Y), 0, 0, 0x0015);
        Opacity = 1;

        // A click on the tray icon does not always give us the foreground, and a
        // non-foreground popup would close on the first Deactivated event.
        var handle = new WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero)
        {
            _ = SetForegroundWindow(handle);
        }

        Activate();
    }

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
        }
    }

    private void Show_Click(object sender, RoutedEventArgs e)
    {
        Close();
        ShowRequested?.Invoke(this, EventArgs.Empty);
    }

    private void TunCore_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string tag }
            && int.TryParse(tag, out int index)
            && index >= 0
            && index <= 3)
        {
            Close();
            TunCoreRequested?.Invoke(this, (Models.TunCoreKind)index);
        }
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        Close();
        ExitRequested?.Invoke(this, EventArgs.Empty);
    }
}
