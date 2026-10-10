namespace SevenLive.Models;

public enum MessageDirection
{
    Unknown = 0,
    Incoming = 1,
    Outgoing = 2,
}

public sealed record JournalApplication(
    long Id,
    string Name,
    string ProcessName,
    DateTime FirstSeenUtc,
    DateTime LastSeenUtc);

public sealed record JournalWindow(
    long Id,
    long? ApplicationId,
    nint Handle,
    int ProcessId,
    string Title,
    DateTime FirstSeenUtc,
    DateTime LastSeenUtc);

public sealed record Conversation(
    long Id,
    long ApplicationId,
    long? WindowId,
    string? Server,
    string? Channel,
    string Name,
    string WindowTitle,
    string Fingerprint,
    DateTime FirstSeenUtc,
    DateTime LastSeenUtc);

public sealed record Participant(
    long Id,
    long ConversationId,
    string DisplayName,
    string Fingerprint,
    DateTime FirstSeenUtc,
    DateTime LastSeenUtc);

public sealed record Message(
    long Id,
    long ConversationId,
    long? ParticipantId,
    string Sender,
    string Text,
    string? TimestampText,
    DateTime? TimestampUtc,
    MessageDirection Direction,
    string? ReplyTo,
    bool Edited,
    string? Url,
    long? SnapshotId,
    string SourceAdapter,
    string? ExternalId,
    string Fingerprint,
    int Position,
    DateTime FirstSeenUtc,
    DateTime LastSeenUtc);

public sealed record Attachment(
    long Id,
    long MessageId,
    string Kind,
    string? Name,
    string? Url,
    string Fingerprint);

public sealed record Reaction(
    long Id,
    long MessageId,
    string Emoji,
    int Count,
    string Fingerprint);
