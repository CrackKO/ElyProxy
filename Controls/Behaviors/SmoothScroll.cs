using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ElyProxy.Controls.Motion;

namespace ElyProxy.Controls.Behaviors;

/// <summary>
/// Inertial mouse-wheel scrolling. The wheel retargets a spring instead of
/// jumping by a fixed step, so consecutive notches carry velocity and the
/// motion can be interrupted by grabbing the scrollbar (any external scroll
/// cancels the animation and takes over from the live offset).
/// Disabled automatically when the user prefers reduced motion.
/// </summary>
public static class SmoothScroll
{
    private const double WheelStepPixels = 96.0;
    private const double VelocityWindowMs = 220.0;
    private const double MaxVelocity = 6000.0;

    private sealed class ScrollState
    {
        public long LastWheelTimestamp;
        public double Velocity;
        public double Target;
        public bool Animating;
    }

    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled",
        typeof(bool),
        typeof(SmoothScroll),
        new PropertyMetadata(false, OnIsEnabledChanged));

    private static readonly DependencyProperty OffsetProperty = DependencyProperty.RegisterAttached(
        "Offset",
        typeof(double),
        typeof(SmoothScroll),
        new PropertyMetadata(0.0, OnOffsetChanged));

    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
        "State",
        typeof(ScrollState),
        typeof(SmoothScroll),
        new PropertyMetadata(null));

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ScrollViewer viewer)
        {
            return;
        }

        if (e.NewValue is true)
        {
            viewer.PreviewMouseWheel += OnPreviewMouseWheel;
            viewer.ScrollChanged += OnScrollChanged;
            viewer.SetValue(StateProperty, new ScrollState());
            return;
        }

        viewer.PreviewMouseWheel -= OnPreviewMouseWheel;
        viewer.ScrollChanged -= OnScrollChanged;
        viewer.ClearValue(StateProperty);
    }

    private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer viewer || e.Handled)
        {
            return;
        }

        if (MotionPreferences.ReducedMotion || viewer.ScrollableHeight <= 0)
        {
            return;
        }

        e.Handled = true;

        var state = viewer.GetValue(StateProperty) as ScrollState ?? new ScrollState();
        viewer.SetValue(StateProperty, state);

        var now = Stopwatch.GetTimestamp();
        var elapsedMs = state.LastWheelTimestamp == 0
            ? double.MaxValue
            : (now - state.LastWheelTimestamp) * 1000.0 / Stopwatch.Frequency;
        state.LastWheelTimestamp = now;

        var direction = e.Delta > 0 ? -1.0 : 1.0;
        var intervalSeconds = elapsedMs <= VelocityWindowMs
            ? Math.Max(0.016, elapsedMs / 1000.0)
            : 0.12;
        state.Velocity = Math.Clamp(direction * WheelStepPixels / intervalSeconds, -MaxVelocity, MaxVelocity);

        var target = Math.Clamp(viewer.VerticalOffset + direction * WheelStepPixels, 0, viewer.ScrollableHeight);
        state.Target = target;
        state.Animating = true;

        viewer.SetValue(OffsetProperty, viewer.VerticalOffset);
        SpringAnimator.AnimateTo(
            viewer,
            OffsetProperty,
            target,
            dampingRatio: 1.0,
            response: 0.28,
            initialVelocity: state.Velocity);
    }

    private static void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (sender is not ScrollViewer viewer || viewer.GetValue(StateProperty) is not ScrollState state)
        {
            return;
        }

        var animated = (double)viewer.GetValue(OffsetProperty);

        // Anything that moves the viewer away from the spring (scrollbar drag,
        // keyboard, focus) takes over and cancels the animation.
        if (Math.Abs(viewer.VerticalOffset - animated) > 2.0)
        {
            SpringAnimator.Cancel(viewer, OffsetProperty);
            state.Animating = false;
            viewer.SetValue(OffsetProperty, viewer.VerticalOffset);
            return;
        }

        if (state.Animating && Math.Abs(animated - state.Target) < 1.0)
        {
            state.Animating = false;
        }

        if (!state.Animating)
        {
            viewer.SetValue(OffsetProperty, viewer.VerticalOffset);
        }
    }

    private static void OnOffsetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ScrollViewer viewer)
        {
            return;
        }

        var target = (double)e.NewValue;
        if (Math.Abs(viewer.VerticalOffset - target) > 0.5)
        {
            viewer.ScrollToVerticalOffset(target);
        }
    }
}
