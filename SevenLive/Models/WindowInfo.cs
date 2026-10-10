using System.Drawing;

namespace SevenLive.Models;

public sealed record WindowInfo(
    nint Handle,
    int ProcessId,
    string ProcessName,
    string Title,
    bool IsActive,
    Rectangle Bounds)
{
    public string HandleHex => "0x" + Handle.ToInt64().ToString("X8");
}
