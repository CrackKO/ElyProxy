using Microsoft.Win32;

namespace ElyProxy.Services;

public sealed record WindowsProxySnapshot(
    WindowsProxyValueSnapshot? ProxyEnable,
    WindowsProxyValueSnapshot? ProxyServer,
    WindowsProxyValueSnapshot? ProxyOverride,
    WindowsProxyValueSnapshot? AutoConfigUrl)
{
    internal static WindowsProxySnapshot Capture(RegistryKey? key) => new(
        WindowsProxyValueSnapshot.Capture(key, "ProxyEnable"),
        WindowsProxyValueSnapshot.Capture(key, "ProxyServer"),
        WindowsProxyValueSnapshot.Capture(key, "ProxyOverride"),
        WindowsProxyValueSnapshot.Capture(key, "AutoConfigURL"));
}

public sealed record WindowsProxyValueSnapshot
{
    public RegistryValueKind Kind { get; set; }

    public string? StringValue { get; set; }

    public int? DwordValue { get; set; }

    public long? QwordValue { get; set; }

    public string[]? MultiStringValue { get; set; }

    public byte[]? BinaryValue { get; set; }

    internal static WindowsProxyValueSnapshot? Capture(RegistryKey? key, string name)
    {
        if (key == null || Array.IndexOf(key.GetValueNames(), name) < 0)
        {
            return null;
        }

        RegistryValueKind kind = key.GetValueKind(name);
        object? value = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        var snapshot = new WindowsProxyValueSnapshot { Kind = kind };
        switch (kind)
        {
            case RegistryValueKind.String:
            case RegistryValueKind.ExpandString:
                snapshot.StringValue = value as string;
                break;
            case RegistryValueKind.DWord:
                snapshot.DwordValue = Convert.ToInt32(value);
                break;
            case RegistryValueKind.QWord:
                snapshot.QwordValue = Convert.ToInt64(value);
                break;
            case RegistryValueKind.MultiString:
                snapshot.MultiStringValue = value as string[];
                break;
            case RegistryValueKind.Binary:
                snapshot.BinaryValue = value as byte[];
                break;
            default:
                throw new InvalidDataException($"Неподдерживаемый тип значения реестра для {name}: {kind}.");
        }

        return snapshot;
    }

    internal object ToRegistryValue() => Kind switch
    {
        RegistryValueKind.String or RegistryValueKind.ExpandString => StringValue ?? string.Empty,
        RegistryValueKind.DWord => DwordValue ?? 0,
        RegistryValueKind.QWord => QwordValue ?? 0L,
        RegistryValueKind.MultiString => MultiStringValue ?? Array.Empty<string>(),
        RegistryValueKind.Binary => BinaryValue ?? Array.Empty<byte>(),
        _ => throw new InvalidDataException($"Неподдерживаемый тип значения реестра: {Kind}.")
    };
}

public enum WindowsProxyRecoveryMode
{
    Pac,
    Manual
}

public sealed class WindowsProxyRecoveryState
{
    public int SchemaVersion { get; set; } = 1;

    public WindowsProxyRecoveryMode Mode { get; set; }

    public WindowsProxySnapshot OriginalSettings { get; set; } = new(null, null, null, null);

    public List<string> OwnedPacUrls { get; set; } = new();

    public int? OwnedManualPort { get; set; }

    public string[] OwnedBypassRules { get; set; } = Array.Empty<string>();
}

public enum WindowsProxyRecoveryStatus
{
    NoPendingRecovery,
    Restored,
    UserSettingsPreserved,
    Failed
}

public sealed record WindowsProxyRecoveryResult(WindowsProxyRecoveryStatus Status, string? Message = null);
