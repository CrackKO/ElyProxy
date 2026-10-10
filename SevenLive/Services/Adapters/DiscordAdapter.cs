using System.Globalization;
using System.Text.RegularExpressions;
using SevenLive.Models;

namespace SevenLive.Services.Adapters;

public sealed class DiscordAdapter : IConversationAdapter
{
    private static readonly string[] ChannelPrefixes = { "Сообщения на ", "Сообщения в ", "Messages in ", "Messages on " };
    private static readonly string[] EditedMarkers = { "изменено", "(edited)", "edited" };
    private static readonly string[] EditButtonMarkers = { "Изменить", "Edit" };

    private static readonly Regex FirstNumberRegex = new(@"\d+", RegexOptions.Compiled);

    public string Name => "discord";

    public bool CanHandle(WindowInfo window)
    {
        return window.ProcessName.Contains("discord", StringComparison.OrdinalIgnoreCase);
    }

    public AdapterResult? Extract(AdapterContext context)
    {
        var tree = new ElementTree(context.Elements);
        var items = context.Elements
            .Where(element => element.ControlType == "ListItem" && ElementTree.HasIdPrefix(element, "chat-messages-"))
            .ToList();
        if (items.Count == 0)
        {
            return null;
        }

        var (titleChannel, titleServer) = SplitWindowTitle(context.Window.Title);
        var list = tree.ParentOf(items[0]);
        var channel = ExtractChannelName(list?.Name) ?? titleChannel;
        var name = channel ?? titleServer ?? context.Window.Title;

        var messages = new List<MessageDraft>();
        var usefulTexts = 0;
        var methods = new HashSet<string>(StringComparer.Ordinal);

        foreach (var item in items)
        {
            var draft = ExtractMessage(tree, item, messages.Count, ref usefulTexts, methods);
            if (draft != null)
            {
                messages.Add(draft);
            }
        }

        if (messages.Count == 0)
        {
            return null;
        }

        var fingerprint = $"discord|{Normalize(titleServer)}|{Normalize(channel ?? name)}";
        var conversation = new ConversationDraft(name, titleServer, channel, fingerprint);
        var method = methods.Count > 0 ? string.Join("+", methods.OrderBy(value => value)) : "none";
        var diagnostics = new AdapterDiagnostics(Name, context.Elements.Count, messages.Count, usefulTexts, method);
        return new AdapterResult(conversation, messages, diagnostics);
    }

    private static MessageDraft? ExtractMessage(
        ElementTree tree,
        UiElementInfo item,
        int position,
        ref int usefulTexts,
        HashSet<string> methods)
    {
        var descendants = tree.Descendants(item).ToList();

        var authorGroup = descendants.FirstOrDefault(element => ElementTree.HasIdPrefix(element, "message-username-"));
        var author = authorGroup != null ? FirstText(tree, authorGroup, "Button") : null;
        if (string.IsNullOrEmpty(author))
        {
            author = descendants.FirstOrDefault(element => element.ControlType == "Button" && ElementTree.HasClass(element, "username"))?.Name;
        }

        var timestampElement = descendants.FirstOrDefault(element => ElementTree.HasIdPrefix(element, "message-timestamp-"));
        var timestampText = timestampElement != null ? FirstText(tree, timestampElement, "Text") : null;
        if (string.IsNullOrEmpty(timestampText))
        {
            var hidden = descendants.FirstOrDefault(element => ElementTree.HasClass(element, "hiddenVisually"));
            var candidate = hidden != null ? FirstText(tree, hidden, "Text") : null;
            if (!string.IsNullOrWhiteSpace(candidate) && TimeTextParser.ParseDateTime(candidate) != null)
            {
                timestampText = candidate;
            }
        }

        var contentGroup = descendants.FirstOrDefault(element =>
            ElementTree.HasIdPrefix(element, "message-content-") && !ElementTree.HasClass(element, "repliedTextContent"));
        var content = ExtractText(tree, contentGroup, out var url);
        var fromContent = content.Length > 0;

        var replyGroup = descendants.FirstOrDefault(element => ElementTree.HasIdPrefix(element, "message-reply-context-"));
        var replyTo = ExtractReply(tree, replyGroup);

        var attachments = ExtractAttachments(tree, descendants, replyGroup);
        var reactions = ExtractReactions(descendants);

        var edited = EditedMarkers.Any(marker => content.Contains(marker, StringComparison.OrdinalIgnoreCase))
            || descendants.Any(element => element.ControlType == "Text"
                && EditedMarkers.Any(marker => (element.Name ?? string.Empty).Contains(marker, StringComparison.OrdinalIgnoreCase)));
        if (edited)
        {
            content = StripEditedMarkers(content);
        }

        if (content.Length == 0 && attachments.Count > 0)
        {
            content = $"[{attachments[0].Kind}]";
        }

        var fromAttachments = content.Length > 0 && !fromContent;
        if (content.Length == 0)
        {
            content = FallbackText(tree, descendants, item);
        }

        if (content.Length == 0 && attachments.Count == 0)
        {
            return null;
        }

        var method = fromContent ? "content" : fromAttachments ? "accessories" : "name";
        methods.Add(method);

        if (string.IsNullOrEmpty(timestampText))
        {
            var messageGroup = descendants.FirstOrDefault(element => ElementTree.HasClass(element, "message__"));
            var parts = messageGroup?.Name?.Split(" , ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var candidate = parts is { Length: > 0 } ? parts[^1] : null;
            if (!string.IsNullOrWhiteSpace(candidate) && TimeTextParser.ParseDateTime(candidate) != null)
            {
                timestampText = candidate;
            }
        }

        var timestampUtc = TimeTextParser.ParseDateTime(timestampText);
        var sender = author ?? string.Empty;

        if (sender.Length > 0)
        {
            usefulTexts++;
        }

        if (content.Length > 0)
        {
            usefulTexts++;
        }

        if (!string.IsNullOrEmpty(timestampText))
        {
            usefulTexts++;
        }

        if (!string.IsNullOrEmpty(replyTo))
        {
            usefulTexts++;
        }

        return new MessageDraft(
            sender,
            content,
            string.IsNullOrWhiteSpace(timestampText) ? null : timestampText.Trim(),
            timestampUtc,
            MessageDirection.Unknown,
            replyTo,
            edited,
            url,
            string.IsNullOrWhiteSpace(item.AutomationId) ? null : item.AutomationId,
            position,
            attachments,
            reactions,
            method);
    }

    private static string? ExtractReply(ElementTree tree, UiElementInfo? replyGroup)
    {
        if (replyGroup == null)
        {
            return null;
        }

        var replyAuthor = tree.Descendants(replyGroup)
            .FirstOrDefault(element => element.ControlType == "Button" && ElementTree.HasClass(element, "username"))?.Name;
        var previewButton = tree.Descendants(replyGroup)
            .FirstOrDefault(element => element.ControlType == "Button" && ElementTree.HasClass(element, "repliedTextPreview"));
        var preview = previewButton?.Name;
        if (string.IsNullOrWhiteSpace(preview))
        {
            var contentGroup = tree.Descendants(replyGroup)
                .FirstOrDefault(element => ElementTree.HasClass(element, "repliedTextContent"));
            preview = ExtractText(tree, contentGroup, out _);
        }

        var parts = new[] { replyAuthor, preview }
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Select(part => part!.Trim());
        var result = string.Join(": ", parts);
        return result.Length > 0 ? result : null;
    }

    private static List<AttachmentDraft> ExtractAttachments(ElementTree tree, List<UiElementInfo> descendants, UiElementInfo? replyGroup)
    {
        var result = new List<AttachmentDraft>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        var accessories = descendants.FirstOrDefault(element => ElementTree.HasIdPrefix(element, "message-accessories-"));
        var scope = accessories != null ? tree.DescendantsAndSelf(accessories).ToList() : descendants;

        foreach (var element in scope)
        {
            if (replyGroup != null
                && (element.ElementId == replyGroup.ElementId || tree.IsDescendantOf(element, replyGroup)))
            {
                continue;
            }

            if (IsNoiseElement(element))
            {
                continue;
            }

            var name = string.IsNullOrWhiteSpace(element.Name) ? null : CollapseWhitespace(element.Name);
            if (name != null && EditButtonMarkers.Any(marker => name.StartsWith(marker, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            if (name != null && name.StartsWith("Удалить", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var url = LooksLikeUrl(element.Value) ? element.Value : null;

            string? kind = null;
            if (element.ControlType == "Image" && name != null)
            {
                kind = "image";
            }
            else if (ElementTree.HasClass(element, "attachment") || ElementTree.HasClass(element, "embed"))
            {
                kind = "attachment";
            }
            else if (ElementTree.HasClass(element, "imageWrapper") || ElementTree.HasClass(element, "lazyImg"))
            {
                kind = "image";
            }
            else if (element.ControlType == "Hyperlink" && (url != null || name != null))
            {
                kind = "link";
            }

            if (kind == null || (name == null && url == null))
            {
                continue;
            }

            var key = $"{kind}|{name}|{url}";
            if (!seen.Add(key))
            {
                continue;
            }

            result.Add(new AttachmentDraft(kind, name, url));
        }

        return result;
    }

    private static bool IsNoiseElement(UiElementInfo element)
    {
        return ElementTree.HasClass(element, "hoverButton")
            || ElementTree.HasClass(element, "hiddenVisually")
            || ElementTree.HasClass(element, "removeMosaic");
    }

    private static string FallbackText(ElementTree tree, List<UiElementInfo> descendants, UiElementInfo item)
    {
        var messageGroup = descendants.FirstOrDefault(element => ElementTree.HasClass(element, "message__"));
        if (!string.IsNullOrWhiteSpace(messageGroup?.Name))
        {
            var parts = messageGroup!.Name!
                .Split(" , ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length >= 3)
            {
                var middle = parts.Skip(1).Take(parts.Length - 2).ToList();
                var cleaned = new List<string>();
                foreach (var part in middle)
                {
                    if (cleaned.Count == 0 || cleaned[^1] != part)
                    {
                        cleaned.Add(part);
                    }
                }

                if (cleaned.Count > 0)
                {
                    return string.Join(" ", cleaned);
                }
            }

            if (parts.Length > 0)
            {
                return parts[0];
            }
        }

        return string.IsNullOrWhiteSpace(item.Name) ? string.Empty : CollapseWhitespace(item.Name);
    }

    private static List<ReactionDraft> ExtractReactions(List<UiElementInfo> descendants)
    {
        var result = new List<ReactionDraft>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var element in descendants)
        {
            if (!ElementTree.HasClass(element, "reaction"))
            {
                continue;
            }

            var text = element.Name;
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            var tokens = text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var emoji = tokens.Length > 0 ? tokens[0] : string.Empty;
            if (emoji.Length == 0 || emoji.Length > 16)
            {
                continue;
            }

            var numberMatch = FirstNumberRegex.Match(text);
            var count = numberMatch.Success ? int.Parse(numberMatch.Value, CultureInfo.InvariantCulture) : 1;
            if (!seen.Add(emoji))
            {
                continue;
            }

            result.Add(new ReactionDraft(emoji, count));
        }

        return result;
    }

    private static string ExtractText(ElementTree tree, UiElementInfo? root, out string? url)
    {
        url = null;
        if (root == null)
        {
            return string.Empty;
        }

        var parts = new List<string>();
        foreach (var element in tree.DescendantsAndSelf(root))
        {
            if (element.ControlType == "Hyperlink")
            {
                if (url == null && LooksLikeUrl(element.Value))
                {
                    url = element.Value;
                }

                var linkText = element.Name?.Trim();
                if (!string.IsNullOrEmpty(linkText) && !parts.Contains(linkText))
                {
                    parts.Add(linkText);
                }

                continue;
            }

            if (element.ControlType != "Text")
            {
                continue;
            }

            if (tree.ChildrenOf(element).Any(child => child.ControlType is "Text" or "Hyperlink"))
            {
                continue;
            }

            var text = element.Name?.Trim();
            if (!string.IsNullOrEmpty(text) && !parts.Contains(text))
            {
                parts.Add(text);
            }
        }

        return string.Join("\n", parts);
    }

    private static string? FirstText(ElementTree tree, UiElementInfo root, string controlType)
    {
        return tree.DescendantsAndSelf(root)
            .FirstOrDefault(element => element.ControlType == controlType && !string.IsNullOrWhiteSpace(element.Name))
            ?.Name?.Trim();
    }

    private static string StripEditedMarkers(string text)
    {
        var lines = text
            .Split('\n')
            .Where(line =>
            {
                var trimmed = line.Trim().Trim('(', ')', '[', ']');
                return !trimmed.Equals("изменено", StringComparison.OrdinalIgnoreCase)
                    && !trimmed.Equals("edited", StringComparison.OrdinalIgnoreCase);
            });
        return string.Join("\n", lines).Trim();
    }

    private static (string? Channel, string? Server) SplitWindowTitle(string title)
    {
        var text = title.Trim();
        const string suffix = " - Discord";
        if (text.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            text = text[..^suffix.Length].Trim();
        }

        var separator = text.LastIndexOf(" | ", StringComparison.Ordinal);
        if (separator < 0)
        {
            return (text.Length > 0 ? text : null, null);
        }

        var channel = text[..separator].Trim();
        var server = text[(separator + 3)..].Trim();
        return (channel.Length > 0 ? channel : null, server.Length > 0 ? server : null);
    }

    private static string? ExtractChannelName(string? listName)
    {
        if (string.IsNullOrWhiteSpace(listName))
        {
            return null;
        }

        foreach (var prefix in ChannelPrefixes)
        {
            if (listName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return listName[prefix.Length..].Trim();
            }
        }

        return listName.Trim();
    }

    private static bool LooksLikeUrl(string? value)
    {
        return !string.IsNullOrWhiteSpace(value)
            && (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || value.StartsWith("https://", StringComparison.OrdinalIgnoreCase));
    }

    private static string CollapseWhitespace(string value)
    {
        return string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static string Normalize(string? value)
    {
        return value?.Trim().ToLowerInvariant() ?? string.Empty;
    }
}
