using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ElyProxy.Controls.Motion;

namespace ElyProxy.Controls;

/// <summary>
/// iOS/macOS style segmented control. The selection indicator slides between
/// segments with a spring (critically damped, velocity-aware), never a
/// fixed-duration storyboard, so rapid switching stays continuous. The
/// transform is driven from code because a Freezable inside a control template
/// cannot bind to the templated parent.
/// </summary>
public class SegmentedControl : ListBox
{
    private Border? _indicator;
    private TranslateTransform? _indicatorTransform;
    private bool _ready;

    static SegmentedControl()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(SegmentedControl),
            new FrameworkPropertyMetadata(typeof(SegmentedControl)));
    }

    public SegmentedControl()
    {
        SelectionMode = SelectionMode.Single;
        HorizontalAlignment = HorizontalAlignment.Left;
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        _indicator = GetTemplateChild("PART_Indicator") as Border;
        if (_indicator is not null)
        {
            // Templates freeze declared Freezables, so always install our own.
            _indicatorTransform = new TranslateTransform();
            _indicator.RenderTransform = _indicatorTransform;
        }

        UpdateIndicator(animate: false);
        _ready = true;
    }

    protected override void OnSelectionChanged(SelectionChangedEventArgs e)
    {
        base.OnSelectionChanged(e);
        UpdateIndicator(animate: _ready);
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        UpdateIndicator(animate: false);
    }

    private void UpdateIndicator(bool animate)
    {
        if (_indicator is null || _indicatorTransform is null)
        {
            return;
        }

        var count = Items.Count;
        if (count == 0)
        {
            return;
        }

        var contentWidth = Math.Max(0, ActualWidth - Padding.Left - Padding.Right);
        var itemWidth = contentWidth / count;
        var to = Math.Max(0, SelectedIndex) * itemWidth;

        _indicator.Width = itemWidth;

        if (!animate)
        {
            SpringAnimator.Cancel(_indicatorTransform, TranslateTransform.XProperty);
            _indicatorTransform.X = to;
            return;
        }

        SpringAnimator.AnimateTo(_indicatorTransform, TranslateTransform.XProperty, to, dampingRatio: 1.0, response: 0.3);
    }
}
