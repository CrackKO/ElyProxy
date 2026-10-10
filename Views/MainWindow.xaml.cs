using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ElyProxy.Controls.Motion;
using ElyProxy.Interop;
using ElyProxy.Services;
using ElyProxy.ViewModels;
using ElyProxy.Views.Sections;

namespace ElyProxy.Views;

public partial class MainWindow : Window
{
    private const double LogDrawerHeight = 220;

    private readonly MainViewModel _viewModel;
    private readonly UserControl?[] _sections = new UserControl?[4];

    private System.Windows.Forms.NotifyIcon? _trayIcon;
    private TrayMenuWindow? _trayMenu;
    private bool _navigationReady;
    private string _rawLog = string.Empty;

    public MainWindow()
    {
        InitializeComponent();

        _viewModel = new MainViewModel(App.LaunchElyTunOnStartup, App.ElyTunHandoffParentProcessId);
        DataContext = _viewModel;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        InitTrayIcon();
        InitShortcuts();

        DialogService.Current.Attach(SheetHost, ToastHost);

        SourceInitialized += (_, _) => WindowChromeHelper.Configure(this, MaximizeButton);
        Loaded += OnLoaded;
        App.ActivationRequested += OnActivationRequested;
        Closed += (_, _) => App.ActivationRequested -= OnActivationRequested;
    }

    private void OnActivationRequested()
    {
        if (!IsVisible)
        {
            RestoreFromTray();
        }
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }
        Activate();
        Topmost = true;
        Topmost = false;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _navigationReady = true;
        NavList.SelectedIndex = 0;
        ApplyLogDrawerState(animate: false);

        _rawLog = _viewModel.LogText ?? string.Empty;
        ApplyLogFilter();
    }

    private void InitShortcuts()
    {
        for (var index = 0; index < 4; index++)
        {
            var target = index;
            InputBindings.Add(new KeyBinding(
                new RelayCommand(() => NavList.SelectedIndex = target),
                Key.D1 + index,
                ModifierKeys.Control));
        }

        InputBindings.Add(new KeyBinding(
            new RelayCommand(() => _viewModel.ShowLogs = !_viewModel.ShowLogs),
            Key.L,
            ModifierKeys.Control));
    }

    // ------------------------------------------------------------------ navigation

    private void NavList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_navigationReady || NavList.SelectedIndex < 0)
        {
            return;
        }

        var index = NavList.SelectedIndex;
        _sections[index] ??= CreateSection(index);

        if (ReferenceEquals(SectionHost.Content, _sections[index]))
        {
            return;
        }

        SectionHost.Content = _sections[index];
        PlaySectionTransition();
    }

    private UserControl CreateSection(int index)
    {
        UserControl section = index switch
        {
            1 => new SubscriptionsView(),
            2 => new ProfilesView(),
            3 => new SettingsView(),
            _ => new ServersView()
        };

        section.DataContext = _viewModel;
        return section;
    }

    private void PlaySectionTransition()
    {
        var slide = new TranslateTransform(0, 8);
        SectionHost.RenderTransform = slide;
        SpringAnimator.AnimateTo(slide, TranslateTransform.YProperty, 0, 1.0, 0.35);

        SectionHost.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(0.35, 1, TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
    }

    // ------------------------------------------------------------------ log drawer

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Some view-model properties can change on background threads (for
        // example LogText while a core prints to its log). WPF controls must
        // only be touched from the dispatcher, so hop over when needed.
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.InvokeAsync(() => OnViewModelPropertyChanged(sender, e));
            return;
        }

        if (e.PropertyName == nameof(MainViewModel.ShowLogs))
        {
            ApplyLogDrawerState(animate: true);
            return;
        }

        if (e.PropertyName == nameof(MainViewModel.LogText))
        {
            _rawLog = _viewModel.LogText ?? string.Empty;
            ApplyLogFilter();
        }
    }

    private void LogFilter_Changed(object sender, RoutedEventArgs e) => ApplyLogFilter();

    /// <summary>
    /// Filters the journal in the view only: the view model keeps the full text,
    /// so nothing is lost when the filter changes.
    /// </summary>
    private void ApplyLogFilter()
    {
        // The filter controls raise their change events while the XAML is still
        // being loaded, before the journal text box exists.
        if (LogBox is null || LogSearchBox is null || LogLevelBox is null)
        {
            return;
        }

        var lines = _rawLog.Split('\n');
        var query = LogSearchBox.Text?.Trim() ?? string.Empty;
        var level = LogLevelBox.SelectedIndex;

        var filtered = new List<string>(lines.Length);
        foreach (var line in lines)
        {
            if (line.Length == 0)
            {
                continue;
            }

            if (!MatchesLevel(line, level) ||
                (query.Length > 0 && !line.Contains(query, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            filtered.Add(line);
        }

        LogBox.Text = string.Join(Environment.NewLine, filtered);
        LogBox.ScrollToEnd();
    }

    private static bool MatchesLevel(string line, int level) => level switch
    {
        1 => line.Contains("[err]", StringComparison.Ordinal),
        2 => line.Contains("[warn]", StringComparison.Ordinal),
        3 => line.Contains("[sys]", StringComparison.Ordinal),
        _ => true
    };

    private void ApplyLogDrawerState(bool animate)
    {
        var open = _viewModel.ShowLogs;
        var height = open ? LogDrawerHeight : 0;
        var opacity = open ? 1.0 : 0.0;

        LogDrawerContent.IsHitTestVisible = open;

        if (!animate)
        {
            SpringAnimator.Cancel(LogDrawer, HeightProperty);
            SpringAnimator.Cancel(LogDrawerContent, OpacityProperty);
            LogDrawer.Height = height;
            LogDrawerContent.Opacity = opacity;
            return;
        }

        SpringAnimator.AnimateTo(LogDrawer, HeightProperty, height, 1.0, 0.35);
        SpringAnimator.AnimateTo(LogDrawerContent, OpacityProperty, opacity, 1.0, 0.3, epsilon: 0.005);
    }

    private void LogToggleButton_Click(object sender, RoutedEventArgs e) =>
        _viewModel.ShowLogs = !_viewModel.ShowLogs;

    private void CopyLog_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(_viewModel.LogText ?? string.Empty);
        }
        catch
        {
            // Clipboard can be locked by another process; ignore.
        }
    }

    private void LogBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox textBox)
        {
            textBox.ScrollToEnd();
        }
    }

    // ------------------------------------------------------------------ window chrome

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            WindowChromeHelper.ToggleMaximize(this);
            return;
        }

        // Dragging the title bar of a maximized window restores it under the
        // cursor, like native Windows captions do.
        if (WindowState == WindowState.Maximized)
        {
            RestoreForDrag(e);
        }

        WindowChromeHelper.DragWindow(this);
    }

    private void RestoreForDrag(MouseButtonEventArgs e)
    {
        var positionInWindow = e.GetPosition(this);
        var screenPosition = PointToScreen(positionInWindow);
        var dpi = VisualTreeHelper.GetDpi(this);
        var ratio = ActualWidth > 0 ? positionInWindow.X / ActualWidth : 0.5;
        var restoreWidth = RestoreBounds.Width;
        var titleBarCenter = 24.0;

        WindowState = WindowState.Normal;

        Left = screenPosition.X / dpi.DpiScaleX - restoreWidth * ratio;
        Top = Math.Max(0, screenPosition.Y / dpi.DpiScaleY - titleBarCenter);
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaximizeButton_Click(object sender, RoutedEventArgs e) => WindowChromeHelper.ToggleMaximize(this);

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_StateChanged(object sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            Hide();
            if (_trayIcon is not null)
            {
                _trayIcon.Visible = true;
            }

            return;
        }

        if (MaximizeGlyph is not null)
        {
            MaximizeGlyph.Data = (Geometry)FindResource(
                WindowState == WindowState.Maximized ? "Icon.Restore" : "Icon.Maximize");
        }
    }

    // ------------------------------------------------------------------ tray

    private void InitTrayIcon()
    {
        var iconStream = Application.GetResourceStream(new Uri("pack://application:,,,/ElyProxy;component/icon.ico"))?.Stream;
        _trayIcon = new System.Windows.Forms.NotifyIcon
        {
            Icon = iconStream != null ? new System.Drawing.Icon(iconStream) : System.Drawing.SystemIcons.Shield,
            Text = "ElyProxy",
            Visible = false
        };

        _trayIcon.DoubleClick += (_, _) => RestoreFromTray();
        _trayIcon.MouseClick += (_, args) =>
        {
            if (args.Button == System.Windows.Forms.MouseButtons.Right)
            {
                ShowTrayMenu();
            }
        };
    }

    private void ShowTrayMenu()
    {
        if (_trayMenu is { IsVisible: true })
        {
            _trayMenu.Close();
            return;
        }

        var menu = new TrayMenuWindow();
        menu.ShowRequested += (_, _) => RestoreFromTray();
        menu.TunCoreRequested += (_, core) => _viewModel.TunCore = core;
        menu.ExitRequested += (_, _) =>
        {
            if (_trayIcon is not null)
            {
                _trayIcon.Visible = false;
            }

            Application.Current.Shutdown();
        };
        menu.Closed += (_, _) => _trayMenu = null;

        _trayMenu = menu;
        menu.ShowAtCursor();
    }

    private void RestoreFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
        }
    }

    // ------------------------------------------------------------------ shutdown

    private void Window_Closing(object sender, CancelEventArgs e)
    {
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;

        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }

        _viewModel.Dispose();
    }
}
