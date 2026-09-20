using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.Common.Auth;
using IronMonkey.Data;
using IronMonkey.Data.Entities;

namespace IronMonkey.ApiService.Features.Communications.Endpoints;

/// <summary>
/// The conversation attached to one lead or contact, or the unmatched inbound queue.
///
/// Tenancy is not a parameter — it comes from the authenticated context and is additionally
/// enforced by the tenant context's global query filter, so a caller cannot widen the scope by
/// asking for another tenant's messages.
/// </summary>
public class ListMessagesEndpoint : IEndpoint
{
    private const int DefaultPageSize = 50;
    private const int MaxPageSize = 200;

    public static void Map(IEndpointRouteBuilder app) => app
        .MapGet("/api/messages", Handle)
        .WithSummary("List messages for a lead or contact, or the unmatched inbound queue")
        .WithTags("Communications")
        .RequireAuthorization(PermissionConstants.MessagesRead);

    public record MessagePage(List<MessageItem> Items, int TotalCount, int Page, int PageSize, int TotalPages);

    internal static async Task<Ok<MessagePage>> Handle(
        Guid? leadId,
        Guid? contactId,
        /// <summary>True returns inbound messages matched to no record, for review.</summary>
        bool? unmatched,
        int? page,
        int? pageSize,
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        CancellationToken cancellationToken)
    {
        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);
        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        var query = db.Messages.AsNoTracking().AsQueryable();

        if (unmatched == true)
        {
            // IsUnmatched is a computed property and cannot be translated, so the predicate is
            // written out here against real columns.
            query = query.Where(m => m.Direction == Data.Communications.MessageDirection.Inbound
                                     && m.LeadId == null && m.ContactId == null);
        }
        else if (leadId.HasValue)
        {
            query = query.Where(m => m.LeadId == leadId.Value);
        }
        else if (contactId.HasValue)
        {
            query = query.Where(m => m.ContactId == contactId.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var size = Math.Clamp(pageSize ?? DefaultPageSize, 1, MaxPageSize);
        var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)size);

        var requested = Math.Max(page ?? 1, 1);
        var current = totalPages == 0 ? 1 : Math.Min(requested, totalPages);

        var rows = await query
            // Tiebreak on Id, so two messages sharing a timestamp have a defined order and
            // cannot appear on two pages or on none.
            .OrderByDescending(m => m.QueuedAt)
            .ThenByDescending(m => m.Id)
            .Skip((current - 1) * size)
            .Take(size)
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(new MessagePage(
            rows.Select(MessageMapping.ToItem).ToList(), totalCount, current, size, totalPages));
    }
}
