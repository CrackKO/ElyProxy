using SevenLive.Models;

namespace SevenLive.Reporting;

public static class JournalReporter
{
    public static void PrintConversations(IReadOnlyList<ConversationSummary> conversations)
    {
        if (conversations.Count == 0)
        {
            Console.WriteLine("Нормализованных чатов пока нет.");
            Console.WriteLine("Открой чат в Telegram/AyuGram или Discord и запусти: SevenLive watch (или once)");
            return;
        }

        Console.WriteLine($"Чаты ({conversations.Count}):");
        string? currentApplication = null;
        for (var index = 0; index < conversations.Count; index++)
        {
            var conversation = conversations[index];
            if (conversation.ApplicationName != currentApplication)
            {
                currentApplication = conversation.ApplicationName;
                Console.WriteLine();
                Console.WriteLine(currentApplication);
            }

            var last = conversation.LastMessageUtc?.ToLocalTime().ToString("MM-dd HH:mm") ?? "—";
            Console.WriteLine($"  {index + 1,2}. {FormatConversationName(conversation),-40} сообщений: {conversation.MessageCount}, последнее: {last}");
        }
    }

    public static string FormatConversationName(ConversationSummary conversation)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(conversation.Server))
        {
            parts.Add(conversation.Server!);
        }

        if (!string.IsNullOrWhiteSpace(conversation.Channel) && conversation.Channel != conversation.Name)
        {
            parts.Add(conversation.Channel!);
        }

        if (!string.IsNullOrWhiteSpace(conversation.Name))
        {
            parts.Add(conversation.Name);
        }

        return parts.Count > 0 ? string.Join(" / ", parts) : conversation.WindowTitle;
    }

    public static void PrintMessages(ConversationSummary conversation, IReadOnlyList<MessageView> messages)
    {
        Console.WriteLine();
        Console.WriteLine(FormatTitle(conversation));
        Console.WriteLine(new string('-', Math.Clamp(FormatTitle(conversation).Length, 10, 72)));

        if (messages.Count == 0)
        {
            Console.WriteLine("  (сообщений нет)");
            return;
        }

        foreach (var message in messages)
        {
            var time = message.TimestampUtc?.ToLocalTime().ToString("HH:mm")
                ?? message.TimestampText
                ?? "--:--";
            var sender = string.IsNullOrWhiteSpace(message.Sender) ? "?" : message.Sender;
            var edited = message.Edited ? " (изменено)" : string.Empty;

            Console.WriteLine();
            Console.WriteLine($"[{time}] {sender}{edited}");
            if (!string.IsNullOrWhiteSpace(message.ReplyTo))
            {
                Console.WriteLine($"  ↳ {TextFormat.SingleLine(message.ReplyTo, 100)}");
            }

            Console.WriteLine(message.Text);

            if (message.Attachments.Count > 0)
            {
                var names = message.Attachments.Select(attachment =>
                    string.IsNullOrWhiteSpace(attachment.Name) ? attachment.Kind : $"{attachment.Kind}: {attachment.Name}");
                Console.WriteLine($"  вложения: {string.Join("; ", names)}");
            }

            if (message.Reactions.Count > 0)
            {
                var reactions = message.Reactions.Select(reaction =>
                    reaction.Count > 1 ? $"{reaction.Emoji}{reaction.Count}" : reaction.Emoji);
                Console.WriteLine($"  реакции: {string.Join(" ", reactions)}");
            }

            if (!string.IsNullOrWhiteSpace(message.Url))
            {
                Console.WriteLine($"  {message.Url}");
            }
        }

        Console.WriteLine();
    }

    public static void PrintAdapterRuns(IReadOnlyList<AdapterRunSummary> runs)
    {
        if (runs.Count == 0)
        {
            return;
        }

        Console.WriteLine();
        Console.WriteLine("Диагностика адаптеров (последние запуски):");
        foreach (var run in runs)
        {
            var when = run.CapturedUtc.ToLocalTime().ToString("MM-dd HH:mm:ss");
            Console.WriteLine(
                $"  {when}  {run.Adapter,-8} {run.ProcessName,-12} raw={run.RawElements,-4} messages={run.Messages,-3} usefulTexts={run.UsefulTexts,-4} метод: {run.Method}");
        }
    }

    public static string FormatTitle(ConversationSummary conversation)
    {
        var parts = new List<string> { conversation.ApplicationName };
        if (!string.IsNullOrWhiteSpace(conversation.Server))
        {
            parts.Add(conversation.Server!);
        }

        if (!string.IsNullOrWhiteSpace(conversation.Channel) && conversation.Channel != conversation.Name)
        {
            parts.Add(conversation.Channel!);
        }

        if (!string.IsNullOrWhiteSpace(conversation.Name))
        {
            parts.Add(conversation.Name);
        }

        return string.Join(" / ", parts);
    }
}
