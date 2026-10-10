namespace SevenLive.Models;

public sealed record StoredElement(
    long Id,
    long SnapshotId,
    long? ParentElementId,
    int Depth,
    string ControlType,
    string? Name,
    string? AutomationId,
    string? ClassName,
    string? Value,
    int X,
    int Y,
    int Width,
    int Height,
    bool IsEnabled);
