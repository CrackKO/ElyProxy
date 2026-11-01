using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ElyProxy.ViewModels;

namespace ElyProxy.Views.Sections;

public partial class ConnectionPanel : UserControl
{
    private readonly DispatcherTimer _timer;
    public ConnectionPanel()
    {
        InitializeComponent();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => UpdateSession();
        Loaded += (_, _) => { UpdateSession(); _timer.Start(); };
        Unloaded += (_, _) => _timer.Stop();
    }

    private void UpdateSession()
    {
        if (DataContext is not MainViewModel vm) return;
        SessionLabel.Text = vm.IsConnected ? "● Подключено" + (vm.SessionStartedAt is { } start ? " · " + (DateTime.UtcNow - start).ToString(@"hh\:mm\:ss") : "") : "● Отключено";
        SessionLabel.SetResourceReference(ForegroundProperty, vm.IsConnected ? "GreenBrush" : "FgDimBrush");
    }
}
