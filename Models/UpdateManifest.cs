namespace ElyProxy.Models;

/// <summary>
/// Public update description served from the fixed ElyProxy update endpoint.
/// The package hash is always checked before an updater process is launched.
/// </summary>
public sealed class UpdateManifest
{
    public string Version { get; set; } = string.Empty;
    public string PackageUrl { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public string ReleaseNotes { get; set; } = string.Empty;
    public bool Mandatory { get; set; }
}
