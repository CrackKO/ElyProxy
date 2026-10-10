namespace SevenLive.Cli;

public sealed record AppOptions
{
    public const double DefaultIntervalSeconds = 3.0;
    public const int DefaultMaxDepth = 32;
    public const int DefaultMaxElements = 1500;
    public const int DefaultDebugMaxDepth = 32;
    public const int DefaultDebugMaxElements = 4000;

    public double IntervalSeconds { get; init; } = DefaultIntervalSeconds;
    public int? MaxDepth { get; init; }
    public int? MaxElements { get; init; }
    public string? WindowSelector { get; init; }
    public bool UseRawView { get; init; } = true;
    public string DatabasePath { get; init; } = DefaultDatabasePath();

    public int? Limit { get; init; }
    public int? KeepDays { get; init; }
    public bool Vacuum { get; init; }
    public bool IncludeAll { get; init; }
    public bool ListConversations { get; init; }
    public string? ExportFormat { get; init; }
    public string? OutputPath { get; init; }

    public int MaxDepthValue => MaxDepth ?? DefaultMaxDepth;

    public int MaxElementsValue => MaxElements ?? DefaultMaxElements;

    public int DebugMaxDepth => MaxDepth ?? DefaultDebugMaxDepth;

    public int DebugMaxElements => MaxElements ?? DefaultDebugMaxElements;

    public int KeepDaysValue => KeepDays ?? 30;

    public static string DefaultDatabasePath()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SevenLive");
        return Path.Combine(directory, "sevenlive.db");
    }
}
