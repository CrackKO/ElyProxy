using ElyProxy.Core;

namespace ElyProxy.Services;

public sealed record DpiProfile(string Id, string Name, string Description,
    bool Fragment = false, string Length = "100-200", string Interval = "10-20",
    bool Records = false, string Packets = "tlshello", string Core = "Both")
{
    public override string ToString() => Name;

    public XrayTunOptions Apply(XrayTunOptions options) => options with
    {
        FragmentEnabled = Fragment, FragmentPackets = Packets, FragmentLength = Length,
        FragmentInterval = Interval, RecordFragment = Records,
        NoiseType = "", NoisePacket = "", TlsSpoofEnabled = false, TlsSpoofSni = ""
    };
}

/// <summary>Experimental candidates, not claims of ISP-specific effectiveness.</summary>
public static class DpiProfileCatalog
{
    public static IReadOnlyList<DpiProfile> All { get; } = new DpiProfile[]
    {
        new("off", "Без обработки DPI", "Отключает фрагментацию, шум и TLS spoofing. Полезен как контрольный вариант."),
        new("records", "TLS-записи · sing-box", "Только разделение TLS-записей, без TCP-фрагментации. Первый вариант для sing-box.", Records: true, Core: "SingBox"),
        new("fast", "Мягкая фрагментация", "Xray: 200–400 байт, пауза 1–3 мс. sing-box: фрагментация с резервной паузой 1 мс.", true, "200-400", "1-3"),
        new("balanced", "Сбалансированный", "Xray: 100–200 байт, 10–20 мс. sing-box: резервная пауза 10 мс. Прежний режим «Авто».", true),
        new("fine", "Мелкие фрагменты · Xray", "50–100 байт, 5–10 мс. Экспериментальный вариант; может увеличить задержку.", true, "50-100", "5-10", Core: "Xray"),
        new("slow", "Увеличенная пауза", "Xray: 100–200 байт, 30–50 мс. sing-box: резервная пауза 30 мс. Может замедлить подключение.", true, "100-200", "30-50"),
        new("combined", "Записи + фрагментация · sing-box", "Оба механизма вместе; пробуйте после отдельных вариантов. Резервная пауза 10 мс.", true, Records: true, Core: "SingBox"),
        new("writes", "Первые записи TCP · Xray", "Деление первых трёх записей по 100–200 байт. Эксперимент для TCP-транспортов.", true, Packets: "1-3", Core: "Xray")
    };

    public static IReadOnlyList<DpiProfile> ForCore(bool singBox) =>
        All.Where(p => p.Core == "Both" || p.Core == (singBox ? "SingBox" : "Xray")).ToArray();

    public static DpiProfile? Find(string? id) => All.FirstOrDefault(p => p.Id == id);
}
