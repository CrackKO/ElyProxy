using SevenLive.Models;

namespace SevenLive.Services.Adapters;

public sealed class ElementTree
{
    private readonly Dictionary<int, UiElementInfo> _byId = new();
    private readonly Dictionary<int, List<UiElementInfo>> _children = new();

    public ElementTree(IReadOnlyList<UiElementInfo> elements)
    {
        foreach (var element in elements)
        {
            _byId[element.ElementId] = element;
        }

        foreach (var element in elements)
        {
            if (element.ParentElementId is int parentId && _byId.ContainsKey(parentId))
            {
                if (!_children.TryGetValue(parentId, out var list))
                {
                    list = new List<UiElementInfo>();
                    _children[parentId] = list;
                }

                list.Add(element);
            }
        }
    }

    public IReadOnlyList<UiElementInfo> ChildrenOf(UiElementInfo element)
    {
        return _children.TryGetValue(element.ElementId, out var list) ? list : Array.Empty<UiElementInfo>();
    }

    public UiElementInfo? ParentOf(UiElementInfo element)
    {
        return element.ParentElementId is int parentId && _byId.TryGetValue(parentId, out var parent) ? parent : null;
    }

    public bool IsDescendantOf(UiElementInfo element, UiElementInfo ancestor)
    {
        var current = ParentOf(element);
        while (current != null)
        {
            if (current.ElementId == ancestor.ElementId)
            {
                return true;
            }

            current = ParentOf(current);
        }

        return false;
    }

    public IEnumerable<UiElementInfo> Descendants(UiElementInfo element)
    {
        foreach (var child in ChildrenOf(element))
        {
            yield return child;
            foreach (var descendant in Descendants(child))
            {
                yield return descendant;
            }
        }
    }

    public IEnumerable<UiElementInfo> DescendantsAndSelf(UiElementInfo element)
    {
        yield return element;
        foreach (var descendant in Descendants(element))
        {
            yield return descendant;
        }
    }

    public static bool HasClass(UiElementInfo element, string fragment)
    {
        return !string.IsNullOrEmpty(element.ClassName)
            && element.ClassName.Contains(fragment, StringComparison.OrdinalIgnoreCase);
    }

    public static bool HasIdPrefix(UiElementInfo element, string prefix)
    {
        return !string.IsNullOrEmpty(element.AutomationId)
            && element.AutomationId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    public static string DisplayText(UiElementInfo element)
    {
        return string.IsNullOrWhiteSpace(element.Value) ? element.Name : element.Value;
    }
}
