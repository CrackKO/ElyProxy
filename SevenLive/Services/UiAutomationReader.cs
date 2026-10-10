using System.Drawing;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using SevenLive.Models;

namespace SevenLive.Services;

public sealed class UiAutomationReader
{
    private static readonly HashSet<ControlType> ContentControlTypes = new()
    {
        ControlType.Button,
        ControlType.Text,
        ControlType.Edit,
        ControlType.Hyperlink,
        ControlType.CheckBox,
        ControlType.RadioButton,
        ControlType.ComboBox,
        ControlType.ListItem,
        ControlType.MenuItem,
        ControlType.TabItem,
        ControlType.TreeItem,
        ControlType.DataItem,
        ControlType.Document,
        ControlType.Image,
        ControlType.SplitButton,
        ControlType.HeaderItem,
    };

    private readonly UIA3Automation _automation;
    private readonly int _maxDepth;
    private readonly int _maxElements;
    private readonly int _maxVisits;
    private readonly bool _useRawView;

    public UiAutomationReader(UIA3Automation automation, int maxDepth, int maxElements, bool useRawView)
    {
        _automation = automation;
        _maxDepth = maxDepth;
        _maxElements = maxElements;
        _maxVisits = maxElements * 4;
        _useRawView = useRawView;
    }

    public IReadOnlyList<UiElementInfo> ReadVisibleElements(IntPtr windowHandle)
    {
        AutomationElement? window;
        try
        {
            window = _automation.FromHandle(windowHandle);
        }
        catch
        {
            return Array.Empty<UiElementInfo>();
        }

        if (window == null)
        {
            return Array.Empty<UiElementInfo>();
        }

        var context = new WalkContext();
        try
        {
            context.WindowBounds = window.BoundingRectangle;
        }
        catch
        {
            context.WindowBounds = Rectangle.Empty;
        }

        context.SkipBoundsCheck = IsUnusableBounds(context.WindowBounds);

        try
        {
            var walker = _useRawView
                ? _automation.TreeWalkerFactory.GetRawViewWalker()
                : _automation.TreeWalkerFactory.GetControlViewWalker();
            Walk(window, walker, 0, null, context);
        }
        catch
        {
        }

        return context.Elements;
    }

    private void Walk(AutomationElement element, ITreeWalker walker, int depth, int? parentId, WalkContext context)
    {
        if (depth >= _maxDepth || context.Elements.Count >= _maxElements || context.Visited >= _maxVisits)
        {
            return;
        }

        AutomationElement? child;
        try
        {
            child = walker.GetFirstChild(element);
        }
        catch
        {
            return;
        }

        while (child != null)
        {
            if (context.Elements.Count >= _maxElements || context.Visited >= _maxVisits)
            {
                return;
            }

            AutomationElement? sibling;
            try
            {
                sibling = walker.GetNextSibling(child);
            }
            catch
            {
                sibling = null;
            }

            context.Visited++;

            try
            {
                var visibility = GetVisibility(child, context);
                if (visibility != ElementVisibility.Hidden)
                {
                    var recordedParentId = parentId;
                    if (visibility == ElementVisibility.Visible)
                    {
                        var info = TryCreateElementInfo(child, depth, parentId, context);
                        if (info != null)
                        {
                            context.Elements.Add(info);
                            recordedParentId = info.ElementId;
                        }
                    }

                    Walk(child, walker, depth + 1, recordedParentId, context);
                }
            }
            catch
            {
            }

            child = sibling;
        }
    }

    private static ElementVisibility GetVisibility(AutomationElement element, WalkContext context)
    {
        var offscreen = Read(() => element.Properties.IsOffscreen.ValueOrDefault, false);
        var bounds = Read(() => element.Properties.BoundingRectangle.ValueOrDefault, Rectangle.Empty);
        var hasBounds = bounds.Width > 0 && bounds.Height > 0;

        if (context.SkipBoundsCheck)
        {
            return offscreen ? ElementVisibility.Unknown : ElementVisibility.Visible;
        }

        var intersects = hasBounds && (context.WindowBounds.IsEmpty || context.WindowBounds.IntersectsWith(bounds));
        if (!offscreen)
        {
            return intersects || !hasBounds ? ElementVisibility.Visible : ElementVisibility.Hidden;
        }

        return hasBounds ? ElementVisibility.Hidden : ElementVisibility.Unknown;
    }

    private static bool IsUnusableBounds(Rectangle bounds)
    {
        return bounds.IsEmpty
            || bounds.Width <= 1
            || bounds.Height <= 1
            || bounds.X <= -30000
            || bounds.Y <= -30000;
    }

    private static UiElementInfo? TryCreateElementInfo(AutomationElement element, int depth, int? parentId, WalkContext context)
    {
        var controlType = Read(() => element.Properties.ControlType.ValueOrDefault, ControlType.Unknown);
        var name = Read(() => element.Properties.Name.ValueOrDefault, string.Empty) ?? string.Empty;
        var automationId = Read(() => element.Properties.AutomationId.ValueOrDefault, string.Empty) ?? string.Empty;
        var className = Read(() => element.Properties.ClassName.ValueOrDefault, string.Empty) ?? string.Empty;
        var value = ReadValue(element);
        var bounds = Read(() => element.Properties.BoundingRectangle.ValueOrDefault, Rectangle.Empty);
        var isEnabled = Read(() => element.Properties.IsEnabled.ValueOrDefault, false);

        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(value))
        {
            var legacy = ReadLegacy(element);
            if (string.IsNullOrEmpty(name))
            {
                name = legacy.Name;
            }

            if (string.IsNullOrEmpty(value))
            {
                value = legacy.Value;
            }
        }

        if (!ShouldRecord(controlType, name, automationId, value))
        {
            return null;
        }

        return new UiElementInfo(
            context.NextId++,
            parentId,
            depth,
            controlType.ToString(),
            name,
            automationId,
            className,
            value,
            bounds,
            isEnabled);
    }

    private static T Read<T>(Func<T> reader, T fallback)
    {
        try
        {
            return reader();
        }
        catch
        {
            return fallback;
        }
    }

    private static bool ShouldRecord(ControlType controlType, string name, string automationId, string value)
    {
        if (!string.IsNullOrWhiteSpace(name) || !string.IsNullOrWhiteSpace(value) || !string.IsNullOrWhiteSpace(automationId))
        {
            return true;
        }

        return ContentControlTypes.Contains(controlType);
    }

    private static string ReadValue(AutomationElement element)
    {
        try
        {
            var pattern = element.Patterns.Value.PatternOrDefault;
            return pattern?.Value.ValueOrDefault ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static (string Name, string Value) ReadLegacy(AutomationElement element)
    {
        try
        {
            var pattern = element.Patterns.LegacyIAccessible.PatternOrDefault;
            if (pattern == null)
            {
                return (string.Empty, string.Empty);
            }

            return (pattern.Name.ValueOrDefault ?? string.Empty, pattern.Value.ValueOrDefault ?? string.Empty);
        }
        catch
        {
            return (string.Empty, string.Empty);
        }
    }

    private sealed class WalkContext
    {
        public List<UiElementInfo> Elements { get; } = new();
        public Rectangle WindowBounds { get; set; } = Rectangle.Empty;
        public bool SkipBoundsCheck { get; set; }
        public int NextId { get; set; }
        public int Visited { get; set; }
    }

    private enum ElementVisibility
    {
        Visible,
        Hidden,
        Unknown,
    }
}
