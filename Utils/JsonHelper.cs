using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace ElyProxy.Utils;

public static class JsonHelper
{
	private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
	{
		Formatting = Formatting.Indented,
		NullValueHandling = NullValueHandling.Ignore
	};

	public static string Serialize<T>(T obj)
	{
		return JsonConvert.SerializeObject(obj, Settings);
	}

	public static T? Deserialize<T>(string json)
	{
		return JsonConvert.DeserializeObject<T>(json, Settings);
	}

	public static async Task SaveToFileAsync<T>(T obj, string path)
	{
		string dir = Path.GetDirectoryName(path);
		if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
		{
			Directory.CreateDirectory(dir);
		}
		string json = Serialize(obj);
		string temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
		try
		{
			await File.WriteAllTextAsync(temporaryPath, json);
			if (File.Exists(path))
			{
				File.Replace(temporaryPath, path, null);
			}
			else
			{
				File.Move(temporaryPath, path);
			}
		}
		finally
		{
			if (File.Exists(temporaryPath))
			{
				File.Delete(temporaryPath);
			}
		}
	}

	public static async Task<T?> LoadFromFileAsync<T>(string path)
	{
		if (!File.Exists(path))
		{
			return default(T);
		}
		return Deserialize<T>(await File.ReadAllTextAsync(path));
	}
}
