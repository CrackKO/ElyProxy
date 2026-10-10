namespace SevenLive.Models;

public sealed record SnapshotSummary(
    long Id,
    DateTime CapturedUtc,
    string ProcessName,
    string Title,
    string ContentHash,
    int ElementCount,
    bool IsActive);
