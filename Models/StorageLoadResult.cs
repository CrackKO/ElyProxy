namespace ElyProxy.Models;

public enum StorageLoadStatus
{
    Missing,
    Loaded,
    Migrated,
    RecoveredFromBackup,
    Unavailable
}

public sealed record StorageLoadResult<T>(
    T Data,
    StorageLoadStatus Status,
    string? ErrorMessage = null)
{
    public bool CanSave => Status != StorageLoadStatus.Unavailable;
}
