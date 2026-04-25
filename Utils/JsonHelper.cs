using Newtonsoft.Json;

namespace ElyProxy.Utils;

public static class JsonHelper
{
    private static readonly JsonSerializerSettings Settings = new()
    {
        Formatting = Formatting.Indented,
        NullValueHandling = NullValueHandling.Ignore
    };

    public static string Serialize<T>(T obj) =>
        JsonConvert.SerializeObject(obj, Settings);

    public static T? Deserialize<T>(string json) =>
        JsonConvert.DeserializeObject<T>(json, Settings);

    public static async Task SaveToFileAsync<T>(T obj, string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var json = Serialize(obj);
        await File.WriteAllTextAsync(path, json);
    }

    public static async Task<T?> LoadFromFileAsync<T>(string path)
    {
        if (!File.Exists(path)) return default;

        try
        {
            var json = await File.ReadAllTextAsync(path);
            return Deserialize<T>(json);
        }
        catch
        {
            return default;
        }
    }
}
