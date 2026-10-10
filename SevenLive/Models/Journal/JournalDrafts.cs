namespace SevenLive.Models;

public sealed record ConversationDraft(
    string Name,
    string? Server,
    string? Channel,
    string Fingerprint);

public sealed record AttachmentDraft(
    string Kind,
    string? Name,
    string? Url);

public sealed record ReactionDraft(
    string Emoji,
    int Count);

public sealed record MessageDraft(
    string Sender,
    string Text,
    string? TimestampText,
    DateTime? TimestampUtc,
    MessageDirection Direction,
    string? ReplyTo,
    bool Edited,
    string? Url,
    string? ExternalId,
    int Position,
    IReadOnlyList<AttachmentDraft> Attachments,
    IReadOnlyList<ReactionDraft> Reactions,
    string Method);

public sealed record AdapterDiagnostics(
    string Adapter,
    int RawElementsFound,
    int MessagesFound,
    int UsefulTextsFound,
    string ExtractionMethod);

public sealed record AdapterResult(
    ConversationDraft Conversation,
    IReadOnlyList<MessageDraft> Messages,
    AdapterDiagnostics Diagnostics);
