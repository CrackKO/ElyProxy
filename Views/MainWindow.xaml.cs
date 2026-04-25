using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using ElyProxy.ViewModels;

namespace ElyProxy.Views;

public partial class MainWindow : Window
{
    private System.Windows.Forms.NotifyIcon? _trayIcon;
    private readonly MainViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel();
        DataContext = _viewModel;
        InitTrayIcon();
    }

    private void InitTrayIcon()
    {
        var iconStream = Application.GetResourceStream(new Uri("pack://application:,,,/icon.ico"))?.Stream;
        _trayIcon = new System.Windows.Forms.NotifyIcon
        {
            Icon = iconStream != null ? new System.Drawing.Icon(iconStream) : System.Drawing.SystemIcons.Shield,
            Text = "ElyProxy",
            Visible = false,
        };

        _trayIcon.DoubleClick += (_, _) => RestoreFromTray();

        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Показать", null, (_, _) => RestoreFromTray());
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("Выход", null, (_, _) =>
        {
            _trayIcon.Visible = false;
            Application.Current.Shutdown();
        });
        _trayIcon.ContextMenuStrip = menu;
    }

    private void RestoreFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
        if (_trayIcon != null)
            _trayIcon.Visible = false;
    }

    private void Window_StateChanged(object sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            Hide();
            if (_trayIcon != null)
                _trayIcon.Visible = true;
        }
    }

    private void Window_Closing(object sender, CancelEventArgs e)
    {
        if (_trayIcon != null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }

        _viewModel.Dispose();
    }

    private void LogBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is System.Windows.Controls.TextBox tb)
            tb.ScrollToEnd();
    }
}
