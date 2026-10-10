using System;
using System.Collections.Generic;
using ElyProxy.Core;
using ElyProxy.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ElyProxy.Services;

public static class V2rayNJsonExporter
{
	public static string Export(IEnumerable<VlessNode> nodes)
	{
		JArray outbounds = new JArray();
		List<string> skipped = new List<string>();
		foreach (VlessNode node in nodes)
		{
			if (!ProxyProtocolSupport.SupportsXray(node))
			{
				skipped.Add(node.DisplayName + " (" + node.ProtocolDisplay + ")");
				continue;
			}
			JObject config = JObject.Parse(new ConfigBuilder().Build(node));
			JToken? proxy = config["outbounds"]?.FirstOrDefault(outbound => (string?)outbound["tag"] == "proxy");
			if (proxy != null)
			{
				JObject copy = (JObject)proxy.DeepClone();
				copy["tag"] = node.DisplayName;
				outbounds.Add(copy);
			}
		}
		JObject root = new JObject
		{
			["outbounds"] = outbounds
		};
		if (skipped.Count > 0)
		{
			root["comment"] = "Не экспортированы (нет в Xray): " + string.Join(", ", skipped);
		}
		return root.ToString(Formatting.Indented);
	}
}
