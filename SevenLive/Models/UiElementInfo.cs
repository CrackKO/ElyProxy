using System.Drawing;

namespace SevenLive.Models;

public sealed record UiElementInfo(
    int ElementId,
    int? ParentElementId,
    int Depth,
    string ControlType,
    string Name,
    string AutomationId,
    string ClassName,
    string Value,
    Rectangle Bounds,
    bool IsEnabled);
