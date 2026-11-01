using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using ElyProxy.Controls.Motion;
using ElyProxy.Services;

namespace ElyProxy.Controls;

/// <summary>
/// Apple-style activity ring: a real arc (not a dashed circle) whose length
/// breathes while the whole ring rotates. Rendered directly and driven by the
/// compositor clock, so it stays smooth at any size.
/// </summary>
public class BusyRing : FrameworkElement
{
    private static readonly Stopwatch Clock = Stopwatch.StartNew();

    private long _lastTick;
    private double _rotation;
    private double _phase;
    private bool _hooked;
    private bool _themed;
    private Brush? _trackBrush;
    private Brush? _arcBrush;

    public static readonly DependencyProperty RingThicknessProperty = DependencyProperty.Register(
        nameof(RingThickness),
        typeof(double),
        typeof(BusyRing),
        new FrameworkPropertyMetadata(2.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public BusyRing()
    {
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        IsVisibleChanged += OnIsVisibleChanged;
    }

    public double RingThickness
    {
        get => (double)GetValue(RingThicknessProperty);
        set => SetValue(RingThicknessProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsNaN(Width) ? 18.0 : Width;
        var height = double.IsNaN(Height) ? 18.0 : Height;
        return new Size(width, height);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 1)
        {
            return;
        }

        var thickness = Math.Max(1.0, RingThickness);
        var radius = (size - thickness) / 2.0;
        var center = new Point(ActualWidth / 2.0, ActualHeight / 2.0);

        drawingContext.DrawEllipse(null, CreatePen(TrackBrush, thickness, round: false), center, radius, radius);

        var reduced = MotionPreferences.ReducedMotion;
        var sweep = reduced ? 260.0 : 130.0 + 120.0 * (0.5 + 0.5 * Math.Sin(_phase * Math.PI / 180.0));
        drawingContext.DrawGeometry(
            null,
            CreatePen(ArcBrush, thickness, round: true),
            CreateArc(center, radius, _rotation, sweep));
    }

    private static Pen CreatePen(Brush brush, double thickness, bool round) =>
        new(brush, thickness)
        {
            StartLineCap = round ? PenLineCap.Round : PenLineCap.Flat,
            EndLineCap = round ? PenLineCap.Round : PenLineCap.Flat
        };

    private static Geometry CreateArc(Point center, double radius, double startAngle, double sweepAngle)
    {
        var figure = new PathFigure
        {
            StartPoint = PointOnCircle(center, radius, startAngle),
            IsClosed = false,
            IsFilled = false
        };

        figure.Segments.Add(new ArcSegment(
            PointOnCircle(center, radius, startAngle + sweepAngle),
            new Size(radius, radius),
            0,
            sweepAngle > 180,
            SweepDirection.Clockwise,
            true));

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        geometry.Freeze();
        return geometry;
    }

    private static Point PointOnCircle(Point center, double radius, double angleDegrees)
    {
        var radians = angleDegrees * Math.PI / 180.0;
        return new Point(center.X + radius * Math.Cos(radians), center.Y + radius * Math.Sin(radians));
    }

    private Brush TrackBrush => _trackBrush ??= Resolve("FillBrush", Brushes.Gray);

    private Brush ArcBrush => _arcBrush ??= Resolve("AccentBrush", Brushes.DodgerBlue);

    private Brush Resolve(string key, Brush fallback) =>
        TryFindResource(key) as Brush
        ?? Application.Current?.TryFindResource(key) as Brush
        ?? fallback;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!_themed)
        {
            _themed = true;
            ThemeService.Current.Changed += OnThemeChanged;
        }

        Hook();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_themed)
        {
            _themed = false;
            ThemeService.Current.Changed -= OnThemeChanged;
        }

        Unhook();
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        _trackBrush = null;
        _arcBrush = null;
        InvalidateVisual();
    }

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible)
        {
            Hook();
        }
        else
        {
            Unhook();
        }
    }

    private void Hook()
    {
        if (_hooked || !IsVisible || !IsLoaded)
        {
            return;
        }

        _lastTick = Clock.ElapsedTicks;
        CompositionTarget.Rendering += OnRendering;
        _hooked = true;
    }

    private void Unhook()
    {
        if (!_hooked)
        {
            return;
        }

        CompositionTarget.Rendering -= OnRendering;
        _hooked = false;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        var now = Clock.ElapsedTicks;
        var dt = (now - _lastTick) / (double)Stopwatch.Frequency;
        _lastTick = now;

        if (dt <= 0 || dt > 0.5)
        {
            return;
        }

        var rotationSpeed = MotionPreferences.ReducedMotion ? 160.0 : 380.0;
        _rotation = (_rotation + rotationSpeed * dt) % 360.0;
        _phase = (_phase + 240.0 * dt) % 360.0;

        InvalidateVisual();
    }
}
