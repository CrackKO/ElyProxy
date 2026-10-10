using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using SevenLive.Models;

namespace SevenLive.Data;

public sealed partial class SnapshotStore
{
    public sealed record MessageSaveResult(int Inserted, int Updated, int Total);

    public void CreateJournalSchema()
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS applications (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL,
                process_name TEXT NOT NULL,
                first_seen_utc TEXT NOT NULL,
                last_seen_utc TEXT NOT NULL
            );

            CREATE UNIQUE INDEX IF NOT EXISTS ux_applications_name_process ON applications(name, process_name);

            CREATE TABLE IF NOT EXISTS conversations (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                application_id INTEGER NOT NULL REFERENCES applications(id) ON DELETE CASCADE,
                window_id INTEGER NULL REFERENCES windows(id) ON DELETE SET NULL,
                server TEXT NULL,
                channel TEXT NULL,
                name TEXT NOT NULL,
                window_title TEXT NOT NULL,
                fingerprint TEXT NOT NULL,
                first_seen_utc TEXT NOT NULL,
                last_seen_utc TEXT NOT NULL
            );

            CREATE UNIQUE INDEX IF NOT EXISTS ux_conversations_fingerprint ON conversations(fingerprint);
            CREATE INDEX IF NOT EXISTS ix_conversations_app ON conversations(application_id, id);

            CREATE TABLE IF NOT EXISTS participants (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                conversation_id INTEGER NOT NULL REFERENCES conversations(id) ON DELETE CASCADE,
                display_name TEXT NOT NULL,
                fingerprint TEXT NOT NULL,
                first_seen_utc TEXT NOT NULL,
                last_seen_utc TEXT NOT NULL
            );

            CREATE UNIQUE INDEX IF NOT EXISTS ux_participants_conversation_fp ON participants(conversation_id, fingerprint);

            CREATE TABLE IF NOT EXISTS messages (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                conversation_id INTEGER NOT NULL REFERENCES conversations(id) ON DELETE CASCADE,
                participant_id INTEGER NULL REFERENCES participants(id) ON DELETE SET NULL,
                sender TEXT NOT NULL,
                text TEXT NOT NULL,
                timestamp_text TEXT NULL,
                timestamp_utc TEXT NULL,
                direction INTEGER NOT NULL,
                reply_to TEXT NULL,
                edited INTEGER NOT NULL,
                url TEXT NULL,
                snapshot_id INTEGER NULL REFERENCES snapshots(id) ON DELETE SET NULL,
                source_adapter TEXT NOT NULL,
                external_id TEXT NULL,
                fingerprint TEXT NOT NULL,
                position INTEGER NOT NULL,
                first_seen_utc TEXT NOT NULL,
                last_seen_utc TEXT NOT NULL
            );

            CREATE UNIQUE INDEX IF NOT EXISTS ux_messages_conversation_fp ON messages(conversation_id, fingerprint);
            CREATE INDEX IF NOT EXISTS ix_messages_conversation_time ON messages(conversation_id, timestamp_utc, position);

            CREATE TABLE IF NOT EXISTS attachments (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                message_id INTEGER NOT NULL REFERENCES messages(id) ON DELETE CASCADE,
                kind TEXT NOT NULL,
                name TEXT NULL,
                url TEXT NULL,
                fingerprint TEXT NOT NULL
            );

            CREATE UNIQUE INDEX IF NOT EXISTS ux_attachments_message_fp ON attachments(message_id, fingerprint);

            CREATE TABLE IF NOT EXISTS reactions (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                message_id INTEGER NOT NULL REFERENCES messages(id) ON DELETE CASCADE,
                emoji TEXT NOT NULL,
                count INTEGER NOT NULL,
                fingerprint TEXT NOT NULL
            );

            CREATE UNIQUE INDEX IF NOT EXISTS ux_reactions_message_fp ON reactions(message_id, fingerprint);

            CREATE TABLE IF NOT EXISTS adapter_runs (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                captured_utc TEXT NOT NULL,
                adapter TEXT NOT NULL,
                process_name TEXT NOT NULL,
                window_title TEXT NOT NULL,
                raw_elements INTEGER NOT NULL,
                messages INTEGER NOT NULL,
                useful_texts INTEGER NOT NULL,
                method TEXT NOT NULL,
                conversation_id INTEGER NULL
            );

            CREATE INDEX IF NOT EXISTS ix_adapter_runs_time ON adapter_runs(captured_utc);
            """;
        command.ExecuteNonQuery();
    }

    public long GetOrCreateApplication(string name, string processName, DateTime utcNow)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            INSERT INTO applications (name, process_name, first_seen_utc, last_seen_utc)
            VALUES ($name, $processName, $now, $now)
            ON CONFLICT(name, process_name) DO UPDATE SET last_seen_utc = excluded.last_seen_utc
            RETURNING id;
            """;
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$processName", processName);
        command.Parameters.AddWithValue("$now", FormatUtc(utcNow));
        return Convert.ToInt64(command.ExecuteScalar() ?? 0L, CultureInfo.InvariantCulture);
    }

    public long GetOrCreateConversation(long applicationId, WindowInfo window, long? windowId, ConversationDraft draft, DateTime utcNow)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            INSERT INTO conversations (application_id, window_id, server, channel, name, window_title, fingerprint, first_seen_utc, last_seen_utc)
            VALUES ($applicationId, $windowId, $server, $channel, $name, $windowTitle, $fingerprint, $now, $now)
            ON CONFLICT(fingerprint) DO UPDATE SET
                application_id = excluded.application_id,
                window_id = COALESCE(excluded.window_id, conversations.window_id),
                server = COALESCE(excluded.server, conversations.server),
                channel = COALESCE(excluded.channel, conversations.channel),
                name = excluded.name,
                window_title = excluded.window_title,
                last_seen_utc = excluded.last_seen_utc
            RETURNING id;
            """;
        command.Parameters.AddWithValue("$applicationId", applicationId);
        command.Parameters.AddWithValue("$windowId", (object?)windowId ?? DBNull.Value);
        command.Parameters.AddWithValue("$server", ToDatabaseValue(draft.Server));
        command.Parameters.AddWithValue("$channel", ToDatabaseValue(draft.Channel));
        command.Parameters.AddWithValue("$name", draft.Name);
        command.Parameters.AddWithValue("$windowTitle", window.Title);
        command.Parameters.AddWithValue("$fingerprint", draft.Fingerprint);
        command.Parameters.AddWithValue("$now", FormatUtc(utcNow));
        return Convert.ToInt64(command.ExecuteScalar() ?? 0L, CultureInfo.InvariantCulture);
    }

    public MessageSaveResult SaveMessages(
        long conversationId,
        long? snapshotId,
        string adapter,
        IReadOnlyList<MessageDraft> messages,
        DateTime utcNow)
    {
        using var transaction = _connection.BeginTransaction();
        var now = FormatUtc(utcNow);
        var inserted = 0;
        var updated = 0;

        using var messageCommand = _connection.CreateCommand();
        messageCommand.Transaction = transaction;
        messageCommand.CommandText = """
            INSERT INTO messages (
                conversation_id, participant_id, sender, text, timestamp_text, timestamp_utc, direction,
                reply_to, edited, url, snapshot_id, source_adapter, external_id, fingerprint, position,
                first_seen_utc, last_seen_utc)
            VALUES (
                $conversationId, $participantId, $sender, $text, $timestampText, $timestampUtc, $direction,
                $replyTo, $edited, $url, $snapshotId, $adapter, $externalId, $fingerprint, $position,
                $now, $now)
            ON CONFLICT(conversation_id, fingerprint) DO UPDATE SET
                participant_id = excluded.participant_id,
                text = excluded.text,
                timestamp_text = excluded.timestamp_text,
                timestamp_utc = excluded.timestamp_utc,
                edited = excluded.edited,
                url = COALESCE(excluded.url, messages.url),
                snapshot_id = excluded.snapshot_id,
                position = excluded.position,
                last_seen_utc = excluded.last_seen_utc
            RETURNING id, first_seen_utc;
            """;
        var conversationParameter = messageCommand.Parameters.Add("$conversationId", SqliteType.Integer);
        var participantParameter = messageCommand.Parameters.Add("$participantId", SqliteType.Integer);
        var senderParameter = messageCommand.Parameters.Add("$sender", SqliteType.Text);
        var textParameter = messageCommand.Parameters.Add("$text", SqliteType.Text);
        var timestampTextParameter = messageCommand.Parameters.Add("$timestampText", SqliteType.Text);
        var timestampUtcParameter = messageCommand.Parameters.Add("$timestampUtc", SqliteType.Text);
        var directionParameter = messageCommand.Parameters.Add("$direction", SqliteType.Integer);
        var replyParameter = messageCommand.Parameters.Add("$replyTo", SqliteType.Text);
        var editedParameter = messageCommand.Parameters.Add("$edited", SqliteType.Integer);
        var urlParameter = messageCommand.Parameters.Add("$url", SqliteType.Text);
        var snapshotParameter = messageCommand.Parameters.Add("$snapshotId", SqliteType.Integer);
        var adapterParameter = messageCommand.Parameters.Add("$adapter", SqliteType.Text);
        var externalIdParameter = messageCommand.Parameters.Add("$externalId", SqliteType.Text);
        var fingerprintParameter = messageCommand.Parameters.Add("$fingerprint", SqliteType.Text);
        var positionParameter = messageCommand.Parameters.Add("$position", SqliteType.Integer);
        var nowParameter = messageCommand.Parameters.Add("$now", SqliteType.Text);

        using var attachmentCommand = _connection.CreateCommand();
        attachmentCommand.Transaction = transaction;
        attachmentCommand.CommandText = """
            INSERT INTO attachments (message_id, kind, name, url, fingerprint)
            VALUES ($messageId, $kind, $name, $url, $fingerprint)
            ON CONFLICT(message_id, fingerprint) DO NOTHING;
            """;
        var attachmentMessageParameter = attachmentCommand.Parameters.Add("$messageId", SqliteType.Integer);
        var attachmentKindParameter = attachmentCommand.Parameters.Add("$kind", SqliteType.Text);
        var attachmentNameParameter = attachmentCommand.Parameters.Add("$name", SqliteType.Text);
        var attachmentUrlParameter = attachmentCommand.Parameters.Add("$url", SqliteType.Text);
        var attachmentFingerprintParameter = attachmentCommand.Parameters.Add("$fingerprint", SqliteType.Text);

        using var reactionCommand = _connection.CreateCommand();
        reactionCommand.Transaction = transaction;
        reactionCommand.CommandText = """
            INSERT INTO reactions (message_id, emoji, count, fingerprint)
            VALUES ($messageId, $emoji, $count, $fingerprint)
            ON CONFLICT(message_id, fingerprint) DO UPDATE SET count = excluded.count;
            """;
        var reactionMessageParameter = reactionCommand.Parameters.Add("$messageId", SqliteType.Integer);
        var reactionEmojiParameter = reactionCommand.Parameters.Add("$emoji", SqliteType.Text);
        var reactionCountParameter = reactionCommand.Parameters.Add("$count", SqliteType.Integer);
        var reactionFingerprintParameter = reactionCommand.Parameters.Add("$fingerprint", SqliteType.Text);

        conversationParameter.Value = conversationId;
        snapshotParameter.Value = (object?)snapshotId ?? DBNull.Value;
        adapterParameter.Value = adapter;
        nowParameter.Value = now;

        foreach (var message in messages)
        {
            var fingerprint = ComputeMessageFingerprint(message);
            participantParameter.Value = message.Sender.Length > 0
                ? GetOrCreateParticipant(conversationId, message.Sender, utcNow, transaction)
                : DBNull.Value;
            senderParameter.Value = message.Sender;
            textParameter.Value = message.Text;
            timestampTextParameter.Value = ToDatabaseValue(message.TimestampText);
            timestampUtcParameter.Value = message.TimestampUtc.HasValue ? FormatUtc(message.TimestampUtc.Value) : DBNull.Value;
            directionParameter.Value = (int)message.Direction;
            replyParameter.Value = ToDatabaseValue(message.ReplyTo);
            editedParameter.Value = message.Edited ? 1 : 0;
            urlParameter.Value = ToDatabaseValue(message.Url);
            externalIdParameter.Value = ToDatabaseValue(message.ExternalId);
            fingerprintParameter.Value = fingerprint;
            positionParameter.Value = message.Position;

            long messageId;
            string? firstSeen;
            using (var reader = messageCommand.ExecuteReader())
            {
                if (!reader.Read())
                {
                    continue;
                }

                messageId = reader.GetInt64(0);
                firstSeen = reader.IsDBNull(1) ? null : reader.GetString(1);
            }

            if (firstSeen == now)
            {
                inserted++;
            }
            else
            {
                updated++;
            }

            attachmentMessageParameter.Value = messageId;
            foreach (var attachment in message.Attachments)
            {
                attachmentKindParameter.Value = attachment.Kind;
                attachmentNameParameter.Value = ToDatabaseValue(attachment.Name);
                attachmentUrlParameter.Value = ToDatabaseValue(attachment.Url);
                attachmentFingerprintParameter.Value = Hash($"{attachment.Kind}|{attachment.Name}|{attachment.Url}");
                attachmentCommand.ExecuteNonQuery();
            }

            reactionMessageParameter.Value = messageId;
            foreach (var reaction in message.Reactions)
            {
                reactionEmojiParameter.Value = reaction.Emoji;
                reactionCountParameter.Value = reaction.Count;
                reactionFingerprintParameter.Value = Hash(reaction.Emoji);
                reactionCommand.ExecuteNonQuery();
            }
        }

        transaction.Commit();
        return new MessageSaveResult(inserted, updated, messages.Count);
    }

    public void SaveAdapterRun(AdapterDiagnostics diagnostics, WindowInfo window, long? conversationId, DateTime utcNow)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            INSERT INTO adapter_runs (captured_utc, adapter, process_name, window_title, raw_elements, messages, useful_texts, method, conversation_id)
            VALUES ($capturedUtc, $adapter, $processName, $windowTitle, $rawElements, $messages, $usefulTexts, $method, $conversationId);
            """;
        command.Parameters.AddWithValue("$capturedUtc", FormatUtc(utcNow));
        command.Parameters.AddWithValue("$adapter", diagnostics.Adapter);
        command.Parameters.AddWithValue("$processName", window.ProcessName);
        command.Parameters.AddWithValue("$windowTitle", window.Title);
        command.Parameters.AddWithValue("$rawElements", diagnostics.RawElementsFound);
        command.Parameters.AddWithValue("$messages", diagnostics.MessagesFound);
        command.Parameters.AddWithValue("$usefulTexts", diagnostics.UsefulTextsFound);
        command.Parameters.AddWithValue("$method", diagnostics.ExtractionMethod);
        command.Parameters.AddWithValue("$conversationId", (object?)conversationId ?? DBNull.Value);
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<ConversationSummary> ReadConversations()
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            SELECT c.id, a.name, c.server, c.channel, c.name, c.window_title,
                   (SELECT COUNT(*) FROM messages m WHERE m.conversation_id = c.id),
                   (SELECT MAX(COALESCE(m.timestamp_utc, m.first_seen_utc)) FROM messages m WHERE m.conversation_id = c.id),
                   c.last_seen_utc
            FROM conversations c
            JOIN applications a ON a.id = c.application_id
            ORDER BY c.last_seen_utc DESC;
            """;

        var result = new List<ConversationSummary>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new ConversationSummary(
                reader.GetInt64(0),
                reader.GetString(1),
                ReadNullableString(reader, 2),
                ReadNullableString(reader, 3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetInt64(6),
                reader.IsDBNull(7) ? null : ParseUtc(reader.GetString(7)),
                ParseUtc(reader.GetString(8))));
        }

        return result;
    }

    public ConversationSummary? ReadConversation(long conversationId)
    {
        return ReadConversations().FirstOrDefault(conversation => conversation.Id == conversationId);
    }

    public IReadOnlyList<MessageView> ReadMessages(long conversationId, int limit)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            SELECT id, sender, text, timestamp_text, timestamp_utc, direction, reply_to, edited, url, source_adapter, position
            FROM (
                SELECT id, sender, text, timestamp_text, timestamp_utc, direction, reply_to, edited, url, source_adapter, position,
                       COALESCE(timestamp_utc, first_seen_utc) AS sort_key
                FROM messages
                WHERE conversation_id = $conversationId
                ORDER BY sort_key DESC, position DESC, id DESC
                LIMIT $limit
            )
            ORDER BY sort_key ASC, position ASC, id ASC;
            """;
        command.Parameters.AddWithValue("$conversationId", conversationId);
        command.Parameters.AddWithValue("$limit", limit);

        var rows = new List<(long Id, string Sender, string Text, string? TimestampText, DateTime? TimestampUtc, MessageDirection Direction, string? ReplyTo, bool Edited, string? Url, string Adapter, int Position)>();
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                rows.Add((
                    reader.GetInt64(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    ReadNullableString(reader, 3),
                    reader.IsDBNull(4) ? null : ParseUtc(reader.GetString(4)),
                    (MessageDirection)reader.GetInt32(5),
                    ReadNullableString(reader, 6),
                    reader.GetInt32(7) != 0,
                    ReadNullableString(reader, 8),
                    reader.GetString(9),
                    reader.GetInt32(10)));
            }
        }

        var result = new List<MessageView>(rows.Count);
        foreach (var row in rows)
        {
            result.Add(new MessageView(
                row.Id,
                row.Sender,
                row.Text,
                row.TimestampText,
                row.TimestampUtc,
                row.Direction,
                row.ReplyTo,
                row.Edited,
                row.Url,
                row.Adapter,
                row.Position,
                ReadAttachments(row.Id),
                ReadReactions(row.Id)));
        }

        return result;
    }

    public IReadOnlyList<AdapterRunSummary> ReadRecentAdapterRuns(int limit)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            SELECT id, captured_utc, adapter, process_name, window_title, raw_elements, messages, useful_texts, method, conversation_id
            FROM adapter_runs
            ORDER BY id DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", limit);

        var result = new List<AdapterRunSummary>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new AdapterRunSummary(
                reader.GetInt64(0),
                ParseUtc(reader.GetString(1)),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetInt32(5),
                reader.GetInt32(6),
                reader.GetInt32(7),
                reader.GetString(8),
                reader.IsDBNull(9) ? null : reader.GetInt64(9)));
        }

        return result;
    }

    private long GetOrCreateParticipant(long conversationId, string displayName, DateTime utcNow, SqliteTransaction transaction)
    {
        using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO participants (conversation_id, display_name, fingerprint, first_seen_utc, last_seen_utc)
            VALUES ($conversationId, $displayName, $fingerprint, $now, $now)
            ON CONFLICT(conversation_id, fingerprint) DO UPDATE SET
                display_name = excluded.display_name,
                last_seen_utc = excluded.last_seen_utc
            RETURNING id;
            """;
        command.Parameters.AddWithValue("$conversationId", conversationId);
        command.Parameters.AddWithValue("$displayName", displayName);
        command.Parameters.AddWithValue("$fingerprint", displayName.Trim().ToLowerInvariant());
        command.Parameters.AddWithValue("$now", FormatUtc(utcNow));
        return Convert.ToInt64(command.ExecuteScalar() ?? 0L, CultureInfo.InvariantCulture);
    }

    private IReadOnlyList<Attachment> ReadAttachments(long messageId)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT id, message_id, kind, name, url, fingerprint FROM attachments WHERE message_id = $messageId ORDER BY id;";
        command.Parameters.AddWithValue("$messageId", messageId);

        var result = new List<Attachment>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new Attachment(
                reader.GetInt64(0),
                reader.GetInt64(1),
                reader.GetString(2),
                ReadNullableString(reader, 3),
                ReadNullableString(reader, 4),
                reader.GetString(5)));
        }

        return result;
    }

    private IReadOnlyList<Reaction> ReadReactions(long messageId)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT id, message_id, emoji, count, fingerprint FROM reactions WHERE message_id = $messageId ORDER BY id;";
        command.Parameters.AddWithValue("$messageId", messageId);

        var result = new List<Reaction>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new Reaction(
                reader.GetInt64(0),
                reader.GetInt64(1),
                reader.GetString(2),
                reader.GetInt32(3),
                reader.GetString(4)));
        }

        return result;
    }

    private static string ComputeMessageFingerprint(MessageDraft message)
    {
        if (!string.IsNullOrWhiteSpace(message.ExternalId))
        {
            return "id:" + message.ExternalId;
        }

        var builder = new StringBuilder();
        builder.Append(message.Sender).Append('\u001F')
            .Append(message.Text).Append('\u001F')
            .Append(message.TimestampText).Append('\u001F')
            .Append((int)message.Direction).Append('\u001F')
            .Append(message.ReplyTo);
        return "h:" + Hash(builder.ToString());
    }

    private static string Hash(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }

    private static string FormatUtc(DateTime value)
    {
        return value.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
    }
}
