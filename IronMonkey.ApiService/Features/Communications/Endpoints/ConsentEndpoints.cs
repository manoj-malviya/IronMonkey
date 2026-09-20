using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.ApiService.Common.Results;
using IronMonkey.Common.Auth;
using IronMonkey.Data;
using IronMonkey.Data.Communications;

namespace IronMonkey.ApiService.Features.Communications.Endpoints;

public sealed record ConsentItem(Guid Id, string Channel, string Address, bool IsOptedOut, string Source, DateTime UpdatedAtUtc);

public sealed record UpdateConsentRequest(string Channel, string Address, bool IsOptedOut);

/// <summary>
/// The tenant's suppression list.
///
/// Readable so an Admin can answer "why did this customer not receive anything", which is
/// otherwise invisible — a suppressed send looks identical to one that was never attempted.
/// </summary>
public class ListConsentEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapGet("/api/message-consent", Handle)
        .WithSummary("List channel opt-outs for the current tenant")
        .WithTags("Communications")
        .RequireAuthorization(PermissionConstants.MessagesRead);

    internal static async Task<Ok<List<ConsentItem>>> Handle(
        string? channel,
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        CancellationToken cancellationToken)
    {
        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);
        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        var query = db.MessageConsents.AsNoTracking().Where(c => c.IsOptedOut);

        if (MessageMapping.TryParseChannel(channel, out var parsed))
            query = query.Where(c => c.Channel == parsed);

        var rows = await query
            .OrderByDescending(c => c.UpdatedAtUtc)
            .ThenBy(c => c.Id)
            .Take(500)
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(rows
            .Select(c => new ConsentItem(c.Id, c.Channel.ToString(), c.Address, c.IsOptedOut, c.Source, c.UpdatedAtUtc))
            .ToList());
    }
}

/// <summary>
/// Records an opt-out or opt-in on behalf of a customer who asked by another route — a phone
/// call, or a reply to a human.
/// </summary>
public class UpdateConsentEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapPut("/api/message-consent", Handle)
        .WithSummary("Record a channel opt-out or opt-in")
        .WithTags("Communications")
        .RequireAuthorization(PermissionConstants.MessagesSend);

    internal static async Task<Results<Ok, ValidationError>> Handle(
        UpdateConsentRequest request,
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        ISuppressionService suppression,
        CancellationToken cancellationToken)
    {
        if (!MessageMapping.TryParseChannel(request.Channel, out var channel))
            return new ValidationError($"\"{request.Channel}\" is not a supported channel.");

        if (string.IsNullOrWhiteSpace(request.Address))
            return new ValidationError("An address is required.");

        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);
        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        if (request.IsOptedOut)
            await suppression.OptOutAsync(db, tenantId, channel, request.Address, "Manual", cancellationToken);
        else
            await suppression.OptInAsync(db, tenantId, channel, request.Address, "Manual", cancellationToken);

        return TypedResults.Ok();
    }
}
