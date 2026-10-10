namespace SevenLive.Models;

public sealed record ConversationSummary(
    long Id,
    string ApplicationName,
    string? Server,
    string? Channel,
    string Name,
    string WindowTitle,
    long MessageCount,
    DateTime? LastMessageUtc,
    DateTime LastSeenUtc);

public sealed record MessageView(
    long Id,
    string Sender,
    string Text,
    string? TimestampText,
    DateTime? TimestampUtc,
    MessageDirection Direction,
    string? ReplyTo,
    bool Edited,
    string? Url,
    string SourceAdapter,
    int Position,
    IReadOnlyList<Attachment> Attachments,
    IReadOnlyList<Reaction> Reactions);

public sealed record AdapterRunSummary(
    long Id,
    DateTime CapturedUtc,
    string Adapter,
    string ProcessName,
    string WindowTitle,
    int RawElements,
    int Messages,
    int UsefulTexts,
    string Method,
    long? ConversationId);
