using ElyProxy.Core;
using ElyProxy.Models;

namespace ElyProxy.Services;

public static class DpiSettingsValidator
{
    public static void Validate(XrayTunOptions options, VlessNode node, bool singBox)
    {
        static void Range(string value, int minimum, int maximum, string name)
        {
            var parts = value.Split('-');
            if (parts.Length is < 1 or > 2 || !int.TryParse(parts[0], out int first) ||
                !int.TryParse(parts[^1], out int last) || first < minimum || last < first || last > maximum)
                throw new InvalidDataException($"Некорректное значение DPI «{name}»: {value}");
        }
        if (options.FragmentEnabled)
        {
            if (node.Security is not ("tls" or "reality"))
                throw new InvalidDataException("TLS-фрагментация требует TLS или REALITY у сервера.");
            Range(options.FragmentInterval, 0, 5000, "интервал");
            if (!singBox)
            {
                Range(options.FragmentLength, 1, 65535, "длина");
                if (options.FragmentPackets != "tlshello") Range(options.FragmentPackets, 1, 100, "записи TCP");
            }
        }
        if (options.RecordFragment && (!singBox || node.Security is not ("tls" or "reality")))
            throw new InvalidDataException("TLS-записи требуют sing-box и TLS/REALITY.");
        if (options.TlsSpoofEnabled && (!singBox || node.Security != "tls" || string.IsNullOrWhiteSpace(options.TlsSpoofSni)))
            throw new InvalidDataException("Spoofing требует sing-box, TLS и заполненный SNI.");
        if (!string.IsNullOrEmpty(options.NoiseType))
        {
            if (singBox || node.Port == 53) throw new InvalidDataException("Noise недоступен для sing-box или DNS-сервера.");
            Range(options.NoiseDelay, 0, 5000, "пауза Noise");
            switch (options.NoiseType)
            {
                case "rand": Range(options.NoisePacket, 1, 1400, "размер Noise"); break;
                case "base64":
                    try { if (Convert.FromBase64String(options.NoisePacket).Length is 0 or > 1400) throw new FormatException(); }
                    catch (FormatException) { throw new InvalidDataException("Noise: некорректный base64 или размер пакета."); }
                    break;
                case "str":
                    if (System.Text.Encoding.UTF8.GetByteCount(options.NoisePacket) is 0 or > 1400)
                        throw new InvalidDataException("Noise: строка должна занимать 1–1400 байт UTF-8.");
                    break;
                default: throw new InvalidDataException("Неизвестный тип Noise.");
            }
        }
    }
}
