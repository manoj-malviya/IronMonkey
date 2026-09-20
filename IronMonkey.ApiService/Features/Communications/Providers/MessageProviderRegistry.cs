using IronMonkey.Data.Communications;

namespace IronMonkey.ApiService.Features.Communications.Providers;

public interface IMessageProviderRegistry
{
    /// <summary>
    /// The provider serving a channel, or null when none is configured. Null is a normal
    /// state, not an error — it means the tenant cannot use that channel, which callers
    /// report as ChannelNotConfigured rather than throwing.
    /// </summary>
    IMessageProvider? For(MessageChannel channel);

    /// <summary>Channels that can actually send right now, for the UI to enable or hide.</summary>
    IReadOnlyList<MessageChannel> AvailableChannels();
}

public sealed class MessageProviderRegistry(IEnumerable<IMessageProvider> providers) : IMessageProviderRegistry
{
    /// <summary>
    /// Only configured providers are indexed, so an unconfigured one is indistinguishable
    /// from an absent one at the call site — a channel either works or it does not, and the
    /// caller never has to remember to re-check IsConfigured.
    ///
    /// Last registration wins for a channel, which is what lets the no-op provider override
    /// a real one when UseNoopProviders is set.
    /// </summary>
    private readonly Dictionary<MessageChannel, IMessageProvider> _byChannel = providers
        .Where(provider => provider.IsConfigured)
        .GroupBy(provider => provider.Channel)
        .ToDictionary(group => group.Key, group => group.Last());

    public IMessageProvider? For(MessageChannel channel) =>
        _byChannel.TryGetValue(channel, out var provider) ? provider : null;

    public IReadOnlyList<MessageChannel> AvailableChannels() => [.. _byChannel.Keys.Order()];
}
