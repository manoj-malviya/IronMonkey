using Microsoft.AspNetCore.Http.HttpResults;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Features.Communications.Providers;
using IronMonkey.Common.Auth;
using IronMonkey.Data.Communications;

namespace IronMonkey.ApiService.Features.Communications.Endpoints;

/// <summary>
/// Which channels can actually send.
///
/// The UI uses this to disable a channel rather than offering a Send button that always
/// fails. It reports availability only — never the credentials, host, account id or sender
/// number behind it, so this endpoint cannot become a way to read provider configuration.
/// </summary>
public class ListChannelsEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapGet("/api/messages/channels", Handle)
        .WithSummary("List which message channels are available")
        .WithTags("Communications")
        .RequireAuthorization(PermissionConstants.MessagesRead);

    internal static Ok<List<ChannelAvailability>> Handle(IMessageProviderRegistry registry)
    {
        var available = registry.AvailableChannels().ToHashSet();

        var all = Enum.GetValues<MessageChannel>()
            .Select(channel => new ChannelAvailability(channel.ToString(), available.Contains(channel)))
            .ToList();

        return TypedResults.Ok(all);
    }
}
