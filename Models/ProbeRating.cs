namespace ElyProxy.Models;

/// <summary>Presentation assessment, separate from the unchanged diagnostic verdict.</summary>
public sealed record ProbeRating(int? Score, string Grade, string Status, string Band, string Metrics, string Explanation)
{
    public string Title => Score is { } score ? $"{Grade} {score} · {Status}" : $"{Grade} · {Status}";
}
