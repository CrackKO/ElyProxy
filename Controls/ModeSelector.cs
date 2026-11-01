using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ElyProxy.Controls;

/// <summary>
/// Four-way mode selector (Off / Proxy Lite / Proxy Pro / ElyTun) mapped onto
/// the three existing exclusive mode flags of the view model. The view model
/// already enforces mutual exclusion, so no proxy logic changes are needed.
/// </summary>
public class ModeSelector : SegmentedControl
{
    private bool _syncing;

    public static readonly DependencyProperty ProxyLiteEnabledProperty = DependencyProperty.Register(
        nameof(ProxyLiteEnabled),
        typeof(bool),
        typeof(ModeSelector),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnModeFlagChanged));

    public static readonly DependencyProperty ProxyProEnabledProperty = DependencyProperty.Register(
        nameof(ProxyProEnabled),
        typeof(bool),
        typeof(ModeSelector),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnModeFlagChanged));

    public static readonly DependencyProperty ElyTunEnabledProperty = DependencyProperty.Register(
        nameof(ElyTunEnabled),
        typeof(bool),
        typeof(ModeSelector),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnModeFlagChanged));

    public static readonly DependencyProperty ElyTunRequestCommandProperty = DependencyProperty.Register(
        nameof(ElyTunRequestCommand),
        typeof(ICommand),
        typeof(ModeSelector),
        new PropertyMetadata(null));

    public ModeSelector()
    {
        Items.Add("Выкл");
        Items.Add("Proxy Lite");
        Items.Add("Proxy Pro");
        Items.Add("ElyTun");
        SelectedIndex = 0;
    }

    public bool ProxyLiteEnabled
    {
        get => (bool)GetValue(ProxyLiteEnabledProperty);
        set => SetValue(ProxyLiteEnabledProperty, value);
    }

    public bool ProxyProEnabled
    {
        get => (bool)GetValue(ProxyProEnabledProperty);
        set => SetValue(ProxyProEnabledProperty, value);
    }

    public bool ElyTunEnabled
    {
        get => (bool)GetValue(ElyTunEnabledProperty);
        set => SetValue(ElyTunEnabledProperty, value);
    }

    public ICommand? ElyTunRequestCommand
    {
        get => (ICommand?)GetValue(ElyTunRequestCommandProperty);
        set => SetValue(ElyTunRequestCommandProperty, value);
    }

    public static readonly DependencyProperty ModeRequestCommandProperty = DependencyProperty.Register(
        nameof(ModeRequestCommand), typeof(ICommand), typeof(ModeSelector));
    public ICommand? ModeRequestCommand
    {
        get => (ICommand?)GetValue(ModeRequestCommandProperty);
        set => SetValue(ModeRequestCommandProperty, value);
    }

    protected override void OnSelectionChanged(SelectionChangedEventArgs e)
    {
        // SelectionChanged may be deferred by WPF while source bindings change.
        // It is presentation, never an instruction to mutate the connection.
        base.OnSelectionChanged(e);
    }

    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source &&
            ItemsControl.ContainerFromElement(this, source) is ListBoxItem item)
        {
            RequestMode(ItemContainerGenerator.IndexFromContainer(item));
            e.Handled = true;
        }
        base.OnPreviewMouseLeftButtonDown(e);
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        int index = e.Key switch
        {
            Key.Left => Math.Max(0, SelectedIndex - 1),
            Key.Right => Math.Min(Items.Count - 1, SelectedIndex + 1),
            Key.Home => 0, Key.End => Items.Count - 1,
            _ => -1
        };
        if (index >= 0) { RequestMode(index); e.Handled = true; }
        base.OnPreviewKeyDown(e);
    }

    private void RequestMode(int index)
    {
        if (index < 0 || index >= Items.Count) return;
        if (ModeRequestCommand?.CanExecute(index) == true) ModeRequestCommand.Execute(index);
    }

    private static void OnModeFlagChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((ModeSelector)d).SyncFromFlags();



    private void SyncFromFlags()
    {
        if (_syncing)
        {
            return;
        }

        _syncing = true;
        try
        {
            SelectedIndex = ElyTunEnabled ? 3 : ProxyProEnabled ? 2 : ProxyLiteEnabled ? 1 : 0;
        }
        finally
        {
            _syncing = false;
        }
    }
}
