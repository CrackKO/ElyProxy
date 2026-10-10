using SevenLive.Models;

namespace SevenLive.Services.Adapters;

public sealed record AdapterContext(WindowInfo Window, IReadOnlyList<UiElementInfo> Elements);

public interface IConversationAdapter
{
    string Name { get; }

    bool CanHandle(WindowInfo window);

    AdapterResult? Extract(AdapterContext context);
}
