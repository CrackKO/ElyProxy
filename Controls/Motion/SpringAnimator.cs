using System.Diagnostics;
using System.Windows;
using System.Windows.Media;

namespace ElyProxy.Controls.Motion;

/// <summary>
/// Frame-driven spring animator. Springs are used instead of storyboards for
/// anything the user can grab or interrupt: motion always starts from the
/// current on-screen value, carries velocity through re-targets and has no
/// fixed duration. Apple's designer-facing parameters are used directly:
/// damping ratio (1.0 = critically damped, no overshoot) and response in
/// seconds (settle speed, not a duration).
/// </summary>
public static class SpringAnimator
{
    private const double TwoPi = Math.PI * 2.0;
    private const double MaxFrameSeconds = 1.0 / 30.0;
    private const double MinFrameSeconds = 1.0 / 240.0;

    private sealed class Animation
    {
        public required DependencyObject Target { get; init; }

        public required DependencyProperty Property { get; init; }

        public double To { get; set; }

        public double Stiffness { get; set; }

        public double Damping { get; set; }

        public double Velocity { get; set; }

        public double Epsilon { get; set; }
    }

    private static readonly List<Animation> Active = new();
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static long _lastTimestamp;
    private static bool _hooked;

    /// <summary>
    /// Animates <paramref name="property"/> to <paramref name="to"/> from its
    /// current (presentation) value. Re-targeting an in-flight animation keeps
    /// its velocity, so reversals never hit a "brick wall".
    /// </summary>
    public static void AnimateTo(
        DependencyObject target,
        DependencyProperty property,
        double to,
        double dampingRatio = 1.0,
        double response = 0.35,
        double initialVelocity = 0,
        double epsilon = 0.01)
    {
        if (MotionPreferences.ReducedMotion || response <= 0)
        {
            target.SetValue(property, to);
            Cancel(target, property);
            return;
        }

        var existing = Find(target, property);
        if (existing is null)
        {
            existing = new Animation
            {
                Target = target,
                Property = property,
                Stiffness = TwoPi * TwoPi / (response * response),
                Damping = 4.0 * Math.PI * dampingRatio / response
            };
            Active.Add(existing);
        }
        else
        {
            existing.Stiffness = TwoPi * TwoPi / (response * response);
            existing.Damping = 4.0 * Math.PI * dampingRatio / response;
        }

        existing.To = to;
        existing.Epsilon = epsilon;
        if (initialVelocity != 0)
        {
            existing.Velocity = initialVelocity;
        }

        EnsureHooked();
    }

    public static void Cancel(DependencyObject target, DependencyProperty property)
    {
        var existing = Find(target, property);
        if (existing is not null)
        {
            Active.Remove(existing);
        }
    }

    private static Animation? Find(DependencyObject target, DependencyProperty property) =>
        Active.FirstOrDefault(a => ReferenceEquals(a.Target, target) && a.Property == property);

    private static void EnsureHooked()
    {
        if (_hooked)
        {
            return;
        }

        _lastTimestamp = Clock.ElapsedTicks;
        CompositionTarget.Rendering += OnRendering;
        _hooked = true;
    }

    private static void OnRendering(object? sender, EventArgs e)
    {
        var now = Clock.ElapsedTicks;
        var dt = (now - _lastTimestamp) / (double)Stopwatch.Frequency;
        _lastTimestamp = now;
        dt = Math.Clamp(dt, MinFrameSeconds, MaxFrameSeconds);

        for (var index = Active.Count - 1; index >= 0; index--)
        {
            var animation = Active[index];
            var current = (double)animation.Target.GetValue(animation.Property);
            var offset = current - animation.To;

            if (Math.Abs(offset) < animation.Epsilon && Math.Abs(animation.Velocity) < animation.Epsilon)
            {
                animation.Target.SetValue(animation.Property, animation.To);
                Active.RemoveAt(index);
                continue;
            }

            var acceleration = -animation.Stiffness * offset - animation.Damping * animation.Velocity;
            animation.Velocity += acceleration * dt;
            animation.Target.SetValue(animation.Property, current + animation.Velocity * dt);
        }

        if (Active.Count == 0)
        {
            CompositionTarget.Rendering -= OnRendering;
            _hooked = false;
        }
    }
}
