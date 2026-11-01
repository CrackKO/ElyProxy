using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ElyProxy.Controls.Motion;
using ElyProxy.Services;

namespace ElyProxy.Controls;

/// <summary>
/// Apple-style modal sheet host: dimming scrim, thick material card that scales
/// in with a spring, footer built from our own buttons. Replaces the Fluent
/// ContentDialog so dialogs match the rest of the interface. Inline validation
/// is honoured through <see cref="ISheetValidation"/>.
/// </summary>
public partial class SheetHost : UserControl
{
    private TaskCompletionSource<bool>? _completion;
    private bool _closing;

    public SheetHost()
    {
        InitializeComponent();
    }

    public bool IsOpen => _completion is not null;

    public Task<bool> ShowAsync(
        string title,
        object content,
        string primaryText,
        string closeText,
        bool destructive = false)
    {
        if (IsOpen)
        {
            return Task.FromResult(false);
        }

        _completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _closing = false;

        TitleText.Text = title;
        BodyHost.Content = content;

        PrimaryButton.Content = primaryText;
        PrimaryButton.Visibility = string.IsNullOrWhiteSpace(primaryText) ? Visibility.Collapsed : Visibility.Visible;
        PrimaryButton.Style = (Style)FindResource(destructive ? "DestructiveButton" : "PrimaryButton");
        PrimaryButton.ClearValue(IsEnabledProperty);
        if (content is ISheetValidation)
        {
            PrimaryButton.SetBinding(
                IsEnabledProperty,
                new Binding(nameof(ISheetValidation.IsValid)) { Source = content, Mode = BindingMode.OneWay });
        }

        CloseButton.Content = closeText;

        Overlay.BeginAnimation(OpacityProperty, null);
        Overlay.Opacity = 1;
        Overlay.Visibility = Visibility.Visible;

        var scale = new ScaleTransform(0.96, 0.96);
        Card.RenderTransform = scale;
        Card.Opacity = 0;
        Scrim.Opacity = 0;

        SpringAnimator.AnimateTo(scale, ScaleTransform.ScaleXProperty, 1, 1.0, 0.3);
        SpringAnimator.AnimateTo(scale, ScaleTransform.ScaleYProperty, 1, 1.0, 0.3);
        SpringAnimator.AnimateTo(Card, OpacityProperty, 1, 1.0, 0.25);
        SpringAnimator.AnimateTo(Scrim, OpacityProperty, 1, 1.0, 0.25);

        if (PrimaryButton.Visibility == Visibility.Visible)
        {
            PrimaryButton.Focus();
        }
        else
        {
            CloseButton.Focus();
        }

        return _completion.Task;
    }

    public void Close(bool result = false) => Complete(result);

    private void Complete(bool result)
    {
        if (_completion is null || _closing)
        {
            return;
        }

        _closing = true;
        var completion = _completion;
        _completion = null;

        if (MotionPreferences.ReducedMotion)
        {
            HideOverlay();
        }
        else
        {
            var fade = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(130))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            fade.Completed += (_, _) => HideOverlay();
            Overlay.BeginAnimation(OpacityProperty, fade);
        }

        completion.TrySetResult(result);
    }

    private void HideOverlay()
    {
        Overlay.Visibility = Visibility.Collapsed;
        Overlay.BeginAnimation(OpacityProperty, null);
        Overlay.Opacity = 1;
        BodyHost.Content = null;
        _closing = false;
    }

    private void Primary_Click(object sender, RoutedEventArgs e) => Complete(true);

    private void Close_Click(object sender, RoutedEventArgs e) => Complete(false);

    private void Scrim_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => Complete(false);

    private void Sheet_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Complete(false);
            return;
        }

        if (e.Key == Key.Enter && PrimaryButton.Visibility == Visibility.Visible && PrimaryButton.IsEnabled)
        {
            e.Handled = true;
            Complete(true);
        }
    }
}
