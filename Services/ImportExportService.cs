using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using ElyProxy.Models;
using ElyProxy.Utils;

namespace ElyProxy.Services;

public class ImportExportService
{
	private class ExportPayload
	{
		public string? Name { get; set; }

		public List<VlessNode>? Nodes { get; set; }
	}

	public async Task ExportProfileAsync(ProxyProfile profile, string filePath)
	{
		await JsonHelper.SaveToFileAsync(new ExportPayload
		{
			Name = profile.Name,
			Nodes = profile.Nodes
		}, filePath);
	}

	public async Task<ProxyProfile> ImportProfileAsync(string filePath)
	{
		ExportPayload payload = await JsonHelper.LoadFromFileAsync<ExportPayload>(filePath)
			?? throw new InvalidDataException("Файл профиля не содержит данных.");
		return new ProxyProfile
		{
			Id = Guid.NewGuid().ToString("N"),
			Name = payload.Name ?? Path.GetFileNameWithoutExtension(filePath),
			Nodes = payload.Nodes ?? new List<VlessNode>()
		};
	}
}
