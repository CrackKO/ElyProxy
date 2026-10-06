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

    protected override void OnSelectionChanged(SelectionChangedEventArgs e)
    {
        base.OnSelectionChanged(e);
        if (_syncing || SelectedIndex < 0)
        {
            return;
        }

        if (SelectedIndex == 3 && ElyTunRequestCommand is { } requestCommand)
        {
            SyncFromFlags();
            if (requestCommand.CanExecute(null))
            {
                requestCommand.Execute(null);
            }

            return;
        }

        _syncing = true;
        try
        {
            SetCurrentValue(ProxyLiteEnabledProperty, SelectedIndex == 1);
            SetCurrentValue(ProxyProEnabledProperty, SelectedIndex == 2);
            SetCurrentValue(ElyTunEnabledProperty, SelectedIndex == 3);
        }
        finally
        {
            _syncing = false;
        }
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
