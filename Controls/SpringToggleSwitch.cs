using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ElyProxy.Controls.Motion;

namespace ElyProxy.Controls;

/// <summary>
/// Apple-style switch. The thumb travels with a spring: a small bounce on user
/// input (the gesture carried momentum), critically damped for programmatic
/// changes. The transform is driven from code because a Freezable inside a
/// control template cannot bind to the templated parent.
/// </summary>
public class SpringToggleSwitch : CheckBox
{
    public const double DefaultTravel = 20.0;

    private Border? _thumb;
    private TranslateTransform? _thumbTransform;

    public static readonly DependencyProperty ThumbOffsetProperty = DependencyProperty.Register(
        nameof(ThumbOffset),
        typeof(double),
        typeof(SpringToggleSwitch),
        new PropertyMetadata(0.0));

    public static readonly DependencyProperty TravelDistanceProperty = DependencyProperty.Register(
        nameof(TravelDistance),
        typeof(double),
        typeof(SpringToggleSwitch),
        new PropertyMetadata(DefaultTravel));

    public double ThumbOffset
    {
        get => (double)GetValue(ThumbOffsetProperty);
        set => SetValue(ThumbOffsetProperty, value);
    }

    public double TravelDistance
    {
        get => (double)GetValue(TravelDistanceProperty);
        set => SetValue(TravelDistanceProperty, value);
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        _thumb = GetTemplateChild("Thumb") as Border;
        if (_thumb is null)
        {
            return;
        }

        // Templates freeze declared Freezables, so always install our own.
        _thumbTransform = new TranslateTransform();
        _thumb.RenderTransform = _thumbTransform;
        _thumbTransform.X = IsChecked == true ? TravelDistance : 0;
        SetCurrentValue(ThumbOffsetProperty, _thumbTransform.X);
    }

    protected override void OnChecked(RoutedEventArgs e)
    {
        base.OnChecked(e);
        Animate(userInitiated: true);
    }

    protected override void OnUnchecked(RoutedEventArgs e)
    {
        base.OnUnchecked(e);
        Animate(userInitiated: true);
    }

    private void Animate(bool userInitiated)
    {
        var to = IsChecked == true ? TravelDistance : 0;

        if (_thumbTransform is null)
        {
            SetCurrentValue(ThumbOffsetProperty, to);
            return;
        }

        var damping = userInitiated && !MotionPreferences.ReducedMotion ? 0.8 : 1.0;
        SpringAnimator.AnimateTo(_thumbTransform, TranslateTransform.XProperty, to, damping, 0.3);
        SetCurrentValue(ThumbOffsetProperty, to);
    }
}
