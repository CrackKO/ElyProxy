namespace SevenLive.Models;

public sealed record WindowSnapshot(
    string ContentHash,
    DateTime CapturedUtc,
    bool IsActive,
    IReadOnlyList<UiElementInfo> Elements);
