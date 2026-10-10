namespace SevenLive.Models;

public sealed record ProcessSnapshotCount(string ProcessName, long SnapshotCount);

public sealed record StoreStatistics(
    long Windows,
    long Snapshots,
    long Elements,
    DateTime? FirstSnapshotUtc,
    DateTime? LastSnapshotUtc,
    long DatabaseSizeBytes,
    IReadOnlyList<ProcessSnapshotCount> TopProcesses);
