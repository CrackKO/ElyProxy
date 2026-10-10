using System.Security.Cryptography;
using System.Text;
using SevenLive.Models;

namespace SevenLive.Services;

public static class SnapshotHasher
{
    public static string Compute(IReadOnlyList<UiElementInfo> elements)
    {
        var builder = new StringBuilder();
        foreach (var element in elements)
        {
            builder.Append(element.ControlType).Append('\u001F')
                .Append(element.Name).Append('\u001F')
                .Append(element.AutomationId).Append('\u001F')
                .Append(element.Value).Append('\n');
        }

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return Convert.ToHexString(bytes);
    }
}
