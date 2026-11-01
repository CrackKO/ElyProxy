using System;
using System.Collections.Generic;
using System.Linq;
using ElyProxy.Models;
using YamlDotNet.Serialization;

namespace ElyProxy.Services;

public sealed class ClashYamlParser
{
	private const int MaxContentLength = 8 * 1024 * 1024;

	private const int MaxProxies = 10000;

	private static readonly IDeserializer Deserializer = new DeserializerBuilder().Build();

	public static bool LooksLikeClashYaml(string content)
	{
		foreach (string line in content.Split('\n'))
		{
			string trimmed = line.TrimStart();
			if (trimmed.StartsWith("proxies:", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("proxy-groups:", StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}
		return false;
	}

	public List<VlessNode> Parse(string yaml)
	{
		List<VlessNode> nodes = new List<VlessNode>();
		if (string.IsNullOrWhiteSpace(yaml) || yaml.Length > MaxContentLength)
		{
			return nodes;
		}
		object? root;
		try
		{
			root = Deserializer.Deserialize<object?>(yaml);
		}
		catch
		{
			return nodes;
		}
		Dictionary<string, object?>? map = NormalizeMap(root);
		if (map == null || !map.TryGetValue("proxies", out object? proxies) || proxies is not List<object?> list)
		{
			return nodes;
		}
		foreach (object? item in list)
		{
			if (nodes.Count >= MaxProxies)
			{
				break;
			}
			if (item is not Dictionary<string, object?> proxyMap)
			{
				continue;
			}
			VlessNode? node = ProxyNodeMapper.MapNode(proxyMap);
			if (node != null)
			{
				nodes.Add(node);
			}
		}
		return nodes;
	}

	public static Dictionary<string, object?>? NormalizeMap(object? value)
	{
		return value switch
		{
			Dictionary<string, object?> stringMap => stringMap,
			Dictionary<object, object> map => map.ToDictionary(pair => pair.Key?.ToString() ?? string.Empty, pair => Normalize(pair.Value)),
			_ => null
		};
	}

	public static object? Normalize(object? value)
	{
		return value switch
		{
			Dictionary<object, object> map => map.ToDictionary(pair => pair.Key?.ToString() ?? string.Empty, pair => Normalize(pair.Value)),
			List<object> list => list.Select(Normalize).ToList(),
			_ => value
		};
	}
}
