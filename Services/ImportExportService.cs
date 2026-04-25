using ElyProxy.Models;
using ElyProxy.Utils;

namespace ElyProxy.Services;

public class ImportExportService
{
    public async Task ExportProfileAsync(ProxyProfile profile, string filePath)
    {
        var payload = new ExportPayload
        {
            Name = profile.Name,
            Nodes = profile.Nodes
        };
        await JsonHelper.SaveToFileAsync(payload, filePath);
    }

    public async Task<ProxyProfile?> ImportProfileAsync(string filePath)
    {
        try
        {
            var payload = await JsonHelper.LoadFromFileAsync<ExportPayload>(filePath);
            if (payload == null) return null;

            return new ProxyProfile
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = payload.Name ?? Path.GetFileNameWithoutExtension(filePath),
                Nodes = payload.Nodes ?? new()
            };
        }
        catch
        {
            return null;
        }
    }

    private class ExportPayload
    {
        public string? Name { get; set; }
        public List<VlessNode>? Nodes { get; set; }
    }
}
