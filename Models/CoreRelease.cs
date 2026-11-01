namespace ElyProxy.Models;

public sealed record CoreRelease(
	string CoreId,
	string DisplayName,
	string Version,
	string AssetName,
	string AssetUrl,
	string Sha256,
	bool IsArchive);
