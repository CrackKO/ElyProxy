namespace SevenLive.Models;

public sealed record ElementSearchHit(
    long SnapshotId,
    DateTime CapturedUtc,
    string ProcessName,
    string Title,
    string ControlType,
    string? Name,
    string? Value);
