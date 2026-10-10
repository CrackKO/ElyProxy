using System.Diagnostics;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Identifiers;
using FlaUI.UIA3;
using SevenLive.Models;

namespace SevenLive.Services;

public sealed class UiDebugDumper
{
    private const int MaxTextChars = 8000;
    private const int MaxStringChars = 500;
    private const int MaxPrintedSamples = 40;
    private const int MaxCollectedSamples = 200;

    private static readonly string[] ContainerMarkers =
    {
        "HistoryInner",
        "HistoryWidget",
        "MessageList",
        "Messages",
        "Сообщения",
        "Сообщении",
        "ChatList",
        "DialogList",
        "Список чатов",
    };

    private readonly UIA3Automation _automation;
    private readonly TextWriter _output;
    private readonly int _maxDepth;
    private readonly int _maxElements;
    private readonly ITreeWalker _rawWalker;

    private readonly List<string> _samples = new();

    private int _dumped;
    private bool _collectSamples;
    private int _withName;
    private int _withValue;
    private int _withText;
    private int _withLegacy;
    private int _withLegacyName;
    private int _withLegacyValue;
    private int _textChars;
    private int _offscreenCount;

    public UiDebugDumper(UIA3Automation automation, TextWriter output, int maxDepth, int maxElements)
    {
        _automation = automation;
        _output = output;
        _maxDepth = maxDepth;
        _maxElements = maxElements;
        _rawWalker = automation.TreeWalkerFactory.GetRawViewWalker();
    }

    public void Dump(WindowInfo window)
    {
        Write("=== DEBUG UI AUTOMATION ===");
        Write($"Окно: {window.ProcessName} (PID {window.ProcessId}) {window.HandleHex} \"{window.Title}\"");
        Write($"Границы окна: {window.Bounds}");
        Write($"Лимиты: глубина <= {_maxDepth}, элементов <= {_maxElements}");
        Write("");

        AutomationElement? root;
        try
        {
            root = _automation.FromHandle(window.Handle);
        }
        catch (Exception exception)
        {
            Write($"Не удалось получить окно через UIA: {exception.GetType().Name}: {exception.Message}");
            return;
        }

        if (root == null)
        {
            Write("UIA не вернул элемент окна.");
            return;
        }

        Write("--- Корень окна ---");
        _dumped = 0;
        DumpElement(root, 0);
        Write("");

        var containers = new List<AutomationElement>();
        var lists = new List<AutomationElement>();
        var visited = 0;
        Search(root, 0, containers, lists, ref visited);

        Write($"Просмотрено элементов Raw View при поиске: {visited}");
        Write("");
        PrintLists(lists);
        Write("");

        if (containers.Count == 0)
        {
            Write("Контейнеры HistoryWidget / HistoryInner / List \"Сообщения\" не найдены.");
            Write($"Показываю Raw View дерево окна (глубина <= {_maxDepth}, элементов <= {_maxElements}):");
            Write("");
            _dumped = 0;
            _samples.Clear();
            _collectSamples = true;
            DumpElement(root, 0);
            WalkRaw(root, 1);
            _collectSamples = false;
            Write("");
            PrintSummary();
            return;
        }

        Write($"Найдено контейнеров: {containers.Count}");
        foreach (var container in containers)
        {
            DumpContainer(container);
        }
    }

    private void DumpContainer(AutomationElement container)
    {
        Write("");
        Write($"=== КОНТЕЙНЕР: {DescribeShort(container)} ===");
        Write("");

        _dumped = 0;
        _samples.Clear();
        _collectSamples = true;
        ResetCounters();

        var stopwatch = Stopwatch.StartNew();
        DumpElement(container, 0);
        WalkRaw(container, 1);
        stopwatch.Stop();

        _collectSamples = false;

        Write("");
        Write($"--- Сводка контейнера ---");
        Write($"Элементов выведено: {_dumped}, время: {stopwatch.Elapsed.TotalSeconds:F1} с");
        PrintSummary();
    }

    private void PrintSummary()
    {
        Write($"Элементов: {_dumped}, из них offscreen: {_offscreenCount}");
        Write($"С непустым Name: {_withName}");
        Write($"С ValuePattern/значением: {_withValue}");
        Write($"С TextPattern: {_withText} (суммарно символов: {_textChars})");
        Write($"С LegacyIAccessiblePattern: {_withLegacy} (Name: {_withLegacyName}, Value: {_withLegacyValue})");

        if (_samples.Count == 0)
        {
            Write("Извлечённый текст: пусто.");
            return;
        }

        Write($"Примеры извлечённого текста (до {MaxPrintedSamples}):");
        foreach (var sample in _samples.Take(MaxPrintedSamples))
        {
            Write("  " + sample);
        }
    }

    private void ResetCounters()
    {
        _withName = 0;
        _withValue = 0;
        _withText = 0;
        _withLegacy = 0;
        _withLegacyName = 0;
        _withLegacyValue = 0;
        _textChars = 0;
        _offscreenCount = 0;
    }

    private void Search(AutomationElement element, int depth, List<AutomationElement> containers, List<AutomationElement> lists, ref int visited)
    {
        if (depth >= _maxDepth || visited >= _maxElements)
        {
            return;
        }

        AutomationElement? child;
        try
        {
            child = _rawWalker.GetFirstChild(element);
        }
        catch
        {
            return;
        }

        while (child != null)
        {
            if (visited >= _maxElements)
            {
                return;
            }

            visited++;

            AutomationElement? sibling;
            try
            {
                sibling = _rawWalker.GetNextSibling(child);
            }
            catch
            {
                sibling = null;
            }

            try
            {
                if (IsContainerCandidate(child))
                {
                    containers.Add(child);
                }
                else
                {
                    var controlType = SafeControlType(child);
                    if (controlType is ControlType.List or ControlType.Table or ControlType.Tree or ControlType.DataGrid)
                    {
                        lists.Add(child);
                    }

                    Search(child, depth + 1, containers, lists, ref visited);
                }
            }
            catch
            {
            }

            child = sibling;
        }
    }

    private void WalkRaw(AutomationElement element, int childDepth)
    {
        if (childDepth > _maxDepth || _dumped >= _maxElements)
        {
            return;
        }

        AutomationElement? child;
        try
        {
            child = _rawWalker.GetFirstChild(element);
        }
        catch
        {
            return;
        }

        while (child != null)
        {
            if (_dumped >= _maxElements)
            {
                return;
            }

            AutomationElement? sibling;
            try
            {
                sibling = _rawWalker.GetNextSibling(child);
            }
            catch
            {
                sibling = null;
            }

            try
            {
                DumpElement(child, childDepth);
                WalkRaw(child, childDepth + 1);
            }
            catch
            {
            }

            child = sibling;
        }
    }

    private void DumpElement(AutomationElement element, int depth)
    {
        if (_dumped >= _maxElements)
        {
            return;
        }

        _dumped++;
        if (_dumped % 500 == 0)
        {
            Write($"    ... выведено {_dumped} элементов ...");
        }

        var indent = new string(' ', Math.Min(depth, 24) * 2);

        var controlType = Prop(() => element.ControlType);
        var name = Prop(() => element.Name);
        var automationId = Prop(() => element.AutomationId);
        var className = Prop(() => element.ClassName);
        var bounds = Prop(() => element.BoundingRectangle);
        var offscreen = Prop(() => element.Properties.IsOffscreen.ValueOrDefault);
        var enabled = Prop(() => element.Properties.IsEnabled.ValueOrDefault);
        var focusable = Prop(() => element.Properties.IsKeyboardFocusable.ValueOrDefault);
        var hasFocus = Prop(() => element.Properties.HasKeyboardFocus.ValueOrDefault);
        var isControl = Prop(() => element.Properties.IsControlElement.ValueOrDefault);
        var isContent = Prop(() => element.Properties.IsContentElement.ValueOrDefault);
        var isPassword = Prop(() => element.Properties.IsPassword.ValueOrDefault);
        var framework = Prop(() => element.Properties.FrameworkId.ValueOrDefault);
        var processId = Prop(() => element.Properties.ProcessId.ValueOrDefault);
        var nativeHandle = Prop(() => element.Properties.NativeWindowHandle.ValueOrDefault);
        var localizedType = Prop(() => element.Properties.LocalizedControlType.ValueOrDefault);
        var acceleratorKey = Prop(() => element.Properties.AcceleratorKey.ValueOrDefault);
        var accessKey = Prop(() => element.Properties.AccessKey.ValueOrDefault);
        var helpText = Prop(() => element.HelpText);
        var itemStatus = Prop(() => element.ItemStatus);
        var itemType = Prop(() => element.Properties.ItemType.ValueOrDefault);
        var positionInSet = Prop(() => element.Properties.PositionInSet.ValueOrDefault);
        var sizeOfSet = Prop(() => element.Properties.SizeOfSet.ValueOrDefault);
        var level = Prop(() => element.Properties.Level.ValueOrDefault);
        var orientation = Prop(() => element.Properties.Orientation.ValueOrDefault);
        var ariaRole = Prop(() => element.Properties.AriaRole.ValueOrDefault);
        var provider = Prop(() => element.Properties.ProviderDescription.ValueOrDefault);

        var value = TryValue(element);
        var text = TryText(element);
        var legacy = TryLegacy(element, out var legacyName, out var legacyValue);

        var hasText = !string.IsNullOrWhiteSpace(name)
            || !string.IsNullOrWhiteSpace(value)
            || !string.IsNullOrWhiteSpace(text)
            || legacy != null;

        Write($"{indent}#{_dumped} [{controlType}] name={Quote(name)} id={Quote(automationId)} class={Quote(className)}");
        Write($"{indent}    rect={bounds} offscreen={offscreen} enabled={enabled} focusable={focusable} hasFocus={hasFocus} isControl={isControl} isContent={isContent} isPassword={isPassword}");
        Write($"{indent}    framework={Quote(framework)} pid={processId} nativeHwnd={nativeHandle} localizedType={Quote(localizedType)} accelKey={Quote(acceleratorKey)} accessKey={Quote(accessKey)}");
        Write($"{indent}    help={Quote(helpText)} itemStatus={Quote(itemStatus)} itemType={Quote(itemType)} positionInSet={positionInSet} sizeOfSet={sizeOfSet} level={level} orientation={orientation} ariaRole={Quote(ariaRole)} provider={Quote(provider)}");
        Write($"{indent}    {DescribePatterns(element, hasText)}");

        if (value != null)
        {
            Write($"{indent}    value={Quote(value, MaxTextChars)}");
        }

        if (text != null)
        {
            Write($"{indent}    text[{text.Length}]={Quote(text, MaxTextChars)}");
        }

        if (legacy != null)
        {
            Write($"{indent}    {legacy}");
        }

        if (!string.IsNullOrWhiteSpace(name))
        {
            _withName++;
        }

        if (offscreen == "True")
        {
            _offscreenCount++;
        }

        if (value != null)
        {
            _withValue++;
        }

        if (text != null)
        {
            _withText++;
            _textChars += text.Length;
        }

        if (legacy != null)
        {
            _withLegacy++;
        }

        if (!string.IsNullOrWhiteSpace(legacyName))
        {
            _withLegacyName++;
        }

        if (!string.IsNullOrWhiteSpace(legacyValue))
        {
            _withLegacyValue++;
        }

        AddSample("name", name);
        AddSample("value", value);
        AddSample("text", text);
        AddSample("legacy.name", legacyName);
        AddSample("legacy.value", legacyValue);
    }

    private void AddSample(string source, string? text)
    {
        if (!_collectSamples || string.IsNullOrWhiteSpace(text) || _samples.Count >= MaxCollectedSamples)
        {
            return;
        }

        _samples.Add($"[{source}] {Quote(text, 300)}");
    }

    private void PrintLists(List<AutomationElement> lists)
    {
        Write($"List/Table/Tree/DataGrid контейнеров: {lists.Count}");
        foreach (var list in lists)
        {
            Write(
                $"  [{Prop(() => list.ControlType)}] name={Quote(Prop(() => list.Name))} id={Quote(Prop(() => list.AutomationId))} " +
                $"class={Quote(Prop(() => list.ClassName))} rect={Prop(() => list.BoundingRectangle)} " +
                $"children={CountChildren(list)} offscreen={Prop(() => list.Properties.IsOffscreen.ValueOrDefault)}");
        }
    }

    private int CountChildren(AutomationElement element)
    {
        var count = 0;
        AutomationElement? child;
        try
        {
            child = _rawWalker.GetFirstChild(element);
        }
        catch
        {
            return 0;
        }

        while (child != null && count < 1000)
        {
            count++;
            try
            {
                child = _rawWalker.GetNextSibling(child);
            }
            catch
            {
                break;
            }
        }

        return count;
    }

    private static bool IsContainerCandidate(AutomationElement element)
    {
        try
        {
            var name = element.Name ?? string.Empty;
            var automationId = element.AutomationId ?? string.Empty;
            var className = element.ClassName ?? string.Empty;

            foreach (var marker in ContainerMarkers)
            {
                if (name.Contains(marker, StringComparison.OrdinalIgnoreCase)
                    || automationId.Contains(marker, StringComparison.OrdinalIgnoreCase)
                    || className.Contains(marker, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private static ControlType SafeControlType(AutomationElement element)
    {
        try
        {
            return element.ControlType;
        }
        catch
        {
            return ControlType.Unknown;
        }
    }

    private static string DescribeShort(AutomationElement element)
    {
        return $"[{Prop(() => element.ControlType)}] name={Quote(Prop(() => element.Name))} " +
               $"id={Quote(Prop(() => element.AutomationId))} class={Quote(Prop(() => element.ClassName))}";
    }

    private static string DescribePatterns(AutomationElement element, bool includeProperties)
    {
        var provider = SafePatterns(() => element.GetSupportedPatternsDirect());
        var providerNames = string.Join(", ", provider.Select(pattern => pattern.Name));

        if (!includeProperties)
        {
            return $"patterns(provider)[{provider.Length}]: {providerNames}";
        }

        var byProperties = SafePatterns(() => element.GetSupportedPatterns());
        var propertyNames = string.Join(", ", byProperties.Select(pattern => pattern.Name));
        return $"patterns(provider)[{provider.Length}]: {providerNames} | patterns(properties)[{byProperties.Length}]: {propertyNames}";
    }

    private static PatternId[] SafePatterns(Func<PatternId[]> reader)
    {
        try
        {
            return reader() ?? Array.Empty<PatternId>();
        }
        catch
        {
            return Array.Empty<PatternId>();
        }
    }

    private static string? TryValue(AutomationElement element)
    {
        try
        {
            var pattern = element.Patterns.Value.PatternOrDefault;
            return pattern?.Value.Value;
        }
        catch
        {
            return null;
        }
    }

    private static string? TryText(AutomationElement element)
    {
        try
        {
            var pattern = element.Patterns.Text.PatternOrDefault;
            return pattern?.DocumentRange.GetText(-1);
        }
        catch
        {
            return null;
        }
    }

    private static string? TryLegacy(AutomationElement element, out string legacyName, out string legacyValue)
    {
        legacyName = string.Empty;
        legacyValue = string.Empty;
        try
        {
            var pattern = element.Patterns.LegacyIAccessible.PatternOrDefault;
            if (pattern == null)
            {
                return null;
            }

            legacyName = Prop(() => pattern.Name.ValueOrDefault);
            legacyValue = Prop(() => pattern.Value.ValueOrDefault);
            var description = Prop(() => pattern.Description.ValueOrDefault);
            var role = Prop(() => pattern.Role.ValueOrDefault);
            var state = Prop(() => pattern.State.ValueOrDefault);
            var defaultAction = Prop(() => pattern.DefaultAction.ValueOrDefault);
            var shortcut = Prop(() => pattern.KeyboardShortcut.ValueOrDefault);
            var help = Prop(() => pattern.Help.ValueOrDefault);
            var childId = Prop(() => pattern.ChildId.ValueOrDefault);

            return $"legacy: name={Quote(legacyName)} value={Quote(legacyValue)} description={Quote(description)} role={role} state={state} " +
                   $"defaultAction={Quote(defaultAction)} shortcut={Quote(shortcut)} help={Quote(help)} childId={childId}";
        }
        catch
        {
            return null;
        }
    }

    private static string Prop(Func<object?> reader)
    {
        try
        {
            var value = reader();
            return value switch
            {
                null => "-",
                string text => text,
                IntPtr handle => handle == IntPtr.Zero ? "-" : "0x" + handle.ToInt64().ToString("X8"),
                _ => value.ToString() ?? "-",
            };
        }
        catch (Exception exception)
        {
            return "<" + exception.GetType().Name + ">";
        }
    }

    private static string Quote(string? value, int maxLength = MaxStringChars)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "\"\"";
        }

        var single = value.Replace("\r", string.Empty).Replace("\n", "\\n");
        return "\"" + Truncate(single, maxLength) + "\"";
    }

    private static string Truncate(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value[..maxLength] + $"…(+{value.Length - maxLength})";
    }

    private void Write(string line)
    {
        _output.WriteLine(line);
    }
}
