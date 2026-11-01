using ElyProxy.Core;

namespace ElyProxy.Models;

public sealed record XrayDpiSettings(bool Fragment = false, string Packets = "tlshello", string Length = "100-200",
    string Interval = "10-20", string Noise = "", string NoisePacket = "", string NoiseDelay = "10-16", string Fingerprint = "");
public sealed record SingBoxDpiSettings(bool Fragment = false, string FallbackDelay = "10", bool Records = false,
    bool Spoof = false, string SpoofSni = "", string SpoofMethod = "wrong-sequence", string Fingerprint = "");
public sealed record DpiSettings(XrayDpiSettings Xray, SingBoxDpiSettings SingBox)
{
    public static DpiSettings Off => new(new(), new());
    public XrayTunOptions Apply(XrayTunOptions options, bool singBox) => singBox ? options with
    {
        FragmentEnabled = SingBox.Fragment, FragmentInterval = SingBox.FallbackDelay,
        RecordFragment = SingBox.Records, TlsSpoofEnabled = SingBox.Spoof,
        TlsSpoofSni = SingBox.SpoofSni, TlsSpoofMethod = SingBox.SpoofMethod,
        NoiseType = "", DpiFingerprint = SingBox.Fingerprint
    } : options with
    {
        FragmentEnabled = Xray.Fragment, FragmentPackets = Xray.Packets, FragmentLength = Xray.Length,
        FragmentInterval = Xray.Interval, NoiseType = Xray.Noise, NoisePacket = Xray.NoisePacket,
        NoiseDelay = Xray.NoiseDelay, DpiFingerprint = Xray.Fingerprint, RecordFragment = false, TlsSpoofEnabled = false
    };
    public static DpiSettings Migrate(AppSettings settings) => new(
        new(settings.FragmentMode != FragmentModeKind.Off, settings.FragmentMode == FragmentModeKind.Auto ? "tlshello" : settings.FragmentPackets, settings.FragmentMode == FragmentModeKind.Auto ? "100-200" : settings.FragmentLength,
            settings.FragmentMode == FragmentModeKind.Auto ? "10-20" : settings.FragmentInterval, settings.NoiseType, settings.NoisePacket, settings.NoiseDelay, settings.DpiFingerprint),
        new(settings.FragmentMode != FragmentModeKind.Off, settings.FragmentInterval.Split('-')[0], settings.FragmentRecord,
            settings.TlsSpoofEnabled, settings.TlsSpoofSni, settings.TlsSpoofMethod, settings.DpiFingerprint));
}
