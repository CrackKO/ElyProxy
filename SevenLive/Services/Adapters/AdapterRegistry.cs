using SevenLive.Models;

namespace SevenLive.Services.Adapters;

public sealed class AdapterRegistry
{
    private readonly IReadOnlyList<IConversationAdapter> _adapters = new IConversationAdapter[]
    {
        new DiscordAdapter(),
        new TelegramAdapter(),
    };

    public IReadOnlyList<IConversationAdapter> Adapters => _adapters;

    public AdapterResult? Extract(WindowInfo window, IReadOnlyList<UiElementInfo> elements)
    {
        foreach (var adapter in _adapters)
        {
            if (!adapter.CanHandle(window))
            {
                continue;
            }

            try
            {
                return adapter.Extract(new AdapterContext(window, elements));
            }
            catch
            {
                return null;
            }
        }

        return null;
    }
}
