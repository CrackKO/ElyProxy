using System.Globalization;
using System.Text.RegularExpressions;
using SevenLive.Models;

namespace SevenLive.Services.Adapters;

public sealed class TelegramAdapter : IConversationAdapter
{
    private static readonly string[] ProcessMarkers = { "telegram", "ayugram", "64gram", "kotatogram", "unigram" };

    private static readonly string[] SenderLabels = { "Отправитель", "От кого", "Sender", "From" };
    private static readonly string[] TextLabels = { "Сообщение", "Текст", "Message", "Text" };
    private static readonly string[] TimeLabels = { "Время", "Time" };
    private static readonly string[] DeliveryLabels = { "Получение", "Доставка", "Delivery", "Status" };
    private static readonly string[] ReplyLabels = { "Переслать", "Переслано от", "Ответ", "Reply", "Forwarded from" };
    private static readonly string[] ReactionLabels = { "Реакции", "Реакция", "Reactions", "Reaction" };
    private static readonly string[] MediaTypeLabels = { "Тип медиафайла", "Media type" };
    private static readonly string[] FileSizeLabels = { "Размер файла", "File size" };
    private static readonly string[] ImageSizeLabels = { "Размер изображения", "Image size" };
    private static readonly string[] DurationLabels = { "Длительность", "Duration" };
    private static readonly string[] DownloadLabels = { "Скачать", "Download" };
    private static readonly string[] StickerEmojiLabels = { "Эмодзи, соответствующий стикеру", "Emoji" };

    private static readonly string[] OutgoingMarkers = { "Отправлено", "Sent" };
    private static readonly string[] IncomingMarkers = { "Получено", "Received" };

    private static readonly string[] ChatListLabels = { "Тип", "Название", "Type", "Name" };
    private static readonly string[] MessageFieldLabels = { "Сообщение", "Отправитель", "Message", "Sender", "From" };

    private static readonly Regex ReactionPartRegex = new(@"^(\S+)\s*(\d+)?$", RegexOptions.Compiled);
    private static readonly Regex ChatIdRegex = new(@"\s*\((\d{3,})\)\s*$", RegexOptions.Compiled);

    public string Name => "telegram";

    public bool CanHandle(WindowInfo window)
    {
        return ProcessMarkers.Any(marker => window.ProcessName.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    public AdapterResult? Extract(AdapterContext context)
    {
        var tree = new ElementTree(context.Elements);
        var nowUtc = DateTime.UtcNow;
        var list = FindMessagesList(context.Elements);
        var items = list != null
            ? tree.Descendants(list).Where(element => element.ControlType == "ListItem").ToList()
            : context.Elements.Where(element => element.ControlType == "ListItem" && IsMessageItem(tree, element)).ToList();

        if (items.Count == 0)
        {
            return null;
        }

        var messages = new List<MessageDraft>();
        var usefulTexts = 0;
        var usedFields = false;
        var usedNameFallback = false;
        DateTime? currentDate = null;

        foreach (var element in context.Elements)
        {
            if (list != null && element.ElementId != list.ElementId && !tree.IsDescendantOf(element, list))
            {
                continue;
            }

            if (element.ControlType == "ListItem" && IsMessageItem(tree, element))
            {
                var draft = ExtractMessage(tree, element, currentDate, nowUtc, messages.Count, ref usefulTexts, ref usedFields, ref usedNameFallback);
                if (draft != null)
                {
                    messages.Add(draft);
                }

                continue;
            }

            if (IsDateSeparator(element) && TimeTextParser.ParseDate(element.Name) is { } date)
            {
                currentDate = date;
            }
        }

        if (messages.Count == 0)
        {
            return null;
        }

        var title = StripUnreadPrefix(context.Window.Title);
        var name = StripChatId(title);
        var conversation = new ConversationDraft(name, null, null, BuildConversationFingerprint(title));
        var method = usedFields && usedNameFallback ? "DataItem+name" : usedFields ? "DataItem" : "name";
        var diagnostics = new AdapterDiagnostics(Name, context.Elements.Count, messages.Count, usefulTexts, method);
        return new AdapterResult(conversation, messages, diagnostics);
    }

    private static MessageDraft? ExtractMessage(
        ElementTree tree,
        UiElementInfo item,
        DateTime? currentDate,
        DateTime nowUtc,
        int position,
        ref int usefulTexts,
        ref bool usedFields,
        ref bool usedNameFallback)
    {
        var fields = CollectFields(tree, item);
        var nameParts = ParseName(item.Name);

        var sender = FirstOf(fields, SenderLabels);
        var text = FirstOf(fields, TextLabels);
        var timeText = FirstOf(fields, TimeLabels);
        var delivery = FirstOf(fields, DeliveryLabels);
        var reply = FirstOf(fields, ReplyLabels);
        var reactions = FirstOf(fields, ReactionLabels);
        var mediaType = FirstOf(fields, MediaTypeLabels);
        var fileSize = FirstOf(fields, FileSizeLabels);
        var imageSize = FirstOf(fields, ImageSizeLabels);
        var duration = FirstOf(fields, DurationLabels);
        var download = FirstOf(fields, DownloadLabels);
        var stickerEmoji = FirstOf(fields, StickerEmojiLabels);

        if (fields.Count > 0)
        {
            usedFields = true;
        }

        var direction = ResolveDirection(delivery);
        if (direction == MessageDirection.Unknown)
        {
            direction = ResolveDirection(nameParts.Status);
        }

        if (sender.Length == 0 && nameParts.Sender.Length > 0)
        {
            sender = nameParts.Sender;
            usedNameFallback = true;
        }

        if (text.Length == 0 && nameParts.Text.Length > 0)
        {
            text = nameParts.Text;
            usedNameFallback = true;
        }

        if (timeText.Length == 0 && nameParts.Status.Length > 0)
        {
            timeText = nameParts.Status;
            usedNameFallback = true;
        }

        var attachments = new List<AttachmentDraft>();
        if (mediaType.Length > 0 || fileSize.Length > 0 || imageSize.Length > 0 || duration.Length > 0 || download.Length > 0)
        {
            var kind = mediaType.Length > 0 ? mediaType : "media";
            var details = new List<string>();
            if (stickerEmoji.Length > 0)
            {
                details.Add(stickerEmoji);
            }

            if (imageSize.Length > 0)
            {
                details.Add(imageSize);
            }

            if (fileSize.Length > 0)
            {
                details.Add(fileSize);
            }

            if (duration.Length > 0)
            {
                details.Add(duration);
            }

            if (download.Length > 0)
            {
                details.Add(download);
            }

            attachments.Add(new AttachmentDraft(kind, details.Count > 0 ? string.Join(", ", details) : null, null));
        }

        if (text.Length == 0 && attachments.Count > 0)
        {
            text = $"[{attachments[0].Kind}]";
        }

        if (text.Length == 0)
        {
            return null;
        }

        var reactionDrafts = ParseReactions(reactions);
        var edited = text.Contains("изменено", StringComparison.OrdinalIgnoreCase)
            || (item.Name?.Contains("изменено", StringComparison.OrdinalIgnoreCase) ?? false);
        if (edited)
        {
            text = text.Replace("(изменено)", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("изменено", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Trim();
        }

        var time = TimeTextParser.ParseTime(timeText) ?? TimeTextParser.ParseTime(nameParts.Status);
        var timestampUtc = TimeTextParser.CombineWithDate(time, currentDate, nowUtc);
        var displaySender = direction == MessageDirection.Outgoing ? "Я" : sender;

        if (displaySender.Length > 0)
        {
            usefulTexts++;
        }

        usefulTexts++;
        if (timeText.Length > 0)
        {
            usefulTexts++;
        }

        if (reply.Length > 0)
        {
            usefulTexts++;
        }

        return new MessageDraft(
            displaySender,
            text,
            timeText.Length > 0 ? timeText : null,
            timestampUtc,
            direction,
            reply.Length > 0 ? reply : null,
            edited,
            null,
            null,
            position,
            attachments,
            reactionDrafts,
            fields.Count > 0 ? "DataItem" : "name");
    }

    private static UiElementInfo? FindMessagesList(IReadOnlyList<UiElementInfo> elements)
    {
        return elements.FirstOrDefault(element =>
            element.ControlType == "List"
            && (element.Name is "Сообщения" or "Messages" || ElementTree.HasClass(element, "HistoryInner")));
    }

    private static bool IsMessageItem(ElementTree tree, UiElementInfo item)
    {
        var hasMessageField = false;
        foreach (var descendant in tree.Descendants(item))
        {
            if (descendant.ControlType != "DataItem")
            {
                continue;
            }

            var label = descendant.Name?.Trim();
            if (string.IsNullOrEmpty(label))
            {
                continue;
            }

            if (ChatListLabels.Contains(label, StringComparer.OrdinalIgnoreCase))
            {
                return false;
            }

            if (MessageFieldLabels.Contains(label, StringComparer.OrdinalIgnoreCase))
            {
                hasMessageField = true;
            }
        }

        return hasMessageField;
    }

    private static Dictionary<string, string> CollectFields(ElementTree tree, UiElementInfo item)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var descendant in tree.Descendants(item))
        {
            if (descendant.ControlType != "DataItem")
            {
                continue;
            }

            var label = descendant.Name?.Trim() ?? string.Empty;
            var value = descendant.Value?.Trim() ?? string.Empty;
            if (label.Length == 0 || value.Length == 0 || fields.ContainsKey(label))
            {
                continue;
            }

            fields[label] = value;
        }

        return fields;
    }

    private static string FirstOf(Dictionary<string, string> fields, string[] labels)
    {
        foreach (var label in labels)
        {
            if (fields.TryGetValue(label, out var value) && value.Length > 0)
            {
                return value;
            }
        }

        return string.Empty;
    }

    private static MessageDirection ResolveDirection(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return MessageDirection.Unknown;
        }

        if (OutgoingMarkers.Any(marker => text.Contains(marker, StringComparison.OrdinalIgnoreCase)))
        {
            return MessageDirection.Outgoing;
        }

        if (IncomingMarkers.Any(marker => text.Contains(marker, StringComparison.OrdinalIgnoreCase)))
        {
            return MessageDirection.Incoming;
        }

        return MessageDirection.Unknown;
    }

    private static (string Sender, string Text, string Status) ParseName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return (string.Empty, string.Empty, string.Empty);
        }

        var lines = name
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        if (lines.Count == 0)
        {
            return (string.Empty, string.Empty, string.Empty);
        }

        var status = string.Empty;
        var last = lines[^1];
        if (TimeTextParser.ParseTime(last) != null
            || last.Contains("Просмотрено", StringComparison.OrdinalIgnoreCase)
            || last.Contains("Отправлено", StringComparison.OrdinalIgnoreCase)
            || last.Contains("Получено", StringComparison.OrdinalIgnoreCase))
        {
            status = last;
            lines.RemoveAt(lines.Count - 1);
        }

        var sender = string.Empty;
        if (lines.Count > 1)
        {
            sender = lines[0];
            lines.RemoveAt(0);
        }

        return (sender, string.Join("\n", lines), status);
    }

    private static List<ReactionDraft> ParseReactions(string text)
    {
        var result = new List<ReactionDraft>();
        if (string.IsNullOrWhiteSpace(text))
        {
            return result;
        }

        foreach (var part in text.Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var match = ReactionPartRegex.Match(part);
            if (!match.Success)
            {
                continue;
            }

            var emoji = match.Groups[1].Value;
            var count = match.Groups[2].Success
                ? int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture)
                : 1;
            result.Add(new ReactionDraft(emoji, count));
        }

        return result;
    }

    private static bool IsDateSeparator(UiElementInfo element)
    {
        var name = element.Name;
        if (string.IsNullOrWhiteSpace(name) || name.Length > 40 || name.Contains('\n'))
        {
            return false;
        }

        return TimeTextParser.ParseDate(name) != null;
    }

    private static string StripUnreadPrefix(string title)
    {
        var trimmed = title.TrimStart();
        if (trimmed.StartsWith('('))
        {
            var closing = trimmed.IndexOf(')');
            if (closing > 0 && closing < 8)
            {
                return trimmed[(closing + 1)..].TrimStart();
            }
        }

        return title;
    }

    private static string Normalize(string value)
    {
        return value.Trim().ToLowerInvariant();
    }

    private static string StripChatId(string title)
    {
        return ChatIdRegex.Replace(title, string.Empty).Trim();
    }

    private static string BuildConversationFingerprint(string title)
    {
        var cleaned = StripChatId(title);
        var at = cleaned.LastIndexOf(" @ ", StringComparison.Ordinal);
        var key = at >= 0 ? cleaned[(at + 3)..] : cleaned;
        return $"telegram|{Normalize(key)}";
    }
}
