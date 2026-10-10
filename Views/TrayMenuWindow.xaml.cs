using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace ElyProxy.Views;

/// <summary>
/// Apple-style tray menu. A borderless, shadowed popup anchored to the cursor
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
        // Measure first so the window can be anchored above the cursor.
        Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = DesiredSize;

        var cursor = System.Windows.Forms.Cursor.Position;
        var workArea = SystemParameters.WorkArea;
        var dpi = VisualTreeHelper.GetDpi(this);

        var left = cursor.X / dpi.DpiScaleX - size.Width / 2.0;
        var top = cursor.Y / dpi.DpiScaleY - size.Height - 8;

        if (top < workArea.Top + 8)
        {
            top = cursor.Y / dpi.DpiScaleY + 8;
        }

        Left = Math.Clamp(left, workArea.Left + 8, workArea.Right - size.Width - 8);
        Top = Math.Clamp(top, workArea.Top + 8, workArea.Bottom - size.Height - 8);

        Show();

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
