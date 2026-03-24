using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.Data;

namespace IronMonkey.ApiService.Features.Activity.Timeline;

public class GetLeadActivityTimelineEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapGet("/api/leads/{leadId}/activity", Handle)
        .WithSummary("Get paginated activity timeline for a lead — newest first, filterable by event type")
        .WithTags("Activity")
        .RequireAuthorization();

    public record ActivityEventDto(
        Guid Id,
        string EventType,
        string EntityType,
        string EntityId,
        Guid ActorId,
        string? ActorName,
        DateTime OccurredAt,
        Dictionary<string, object?>? OldValues,
        Dictionary<string, object?>? NewValues);

    public record TimelineResponse(
        List<ActivityEventDto> Events,
        bool HasMore,
        int TotalCount,
        int Page);

    // Valid event types for filtering (per D-05)
    // field_change, stage_transition, task, note, assignment, system
    // Maps to EventType values: Updated, StageMoved, Created(LeadTask), Note, AssignmentChanged, Created/Deleted

    private static async Task<Results<Ok<TimelineResponse>, NotFound>> Handle(
        Guid leadId,
        int page,
        string? eventTypes,  // Comma-separated: "Note,Updated,Created"
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        CancellationToken cancellationToken)
    {
        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);
        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        // Verify lead exists
        var leadExists = await db.Leads.AnyAsync(l => l.Id == leadId, cancellationToken);
        if (!leadExists) return TypedResults.NotFound();

        // Parse event type filter (per D-05)
        var selectedTypes = string.IsNullOrEmpty(eventTypes)
            ? Array.Empty<string>()
            : eventTypes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var query = db.ActivityLogs
            .Where(a => a.LeadId == leadId);

        if (selectedTypes.Length > 0)
            query = query.Where(a => selectedTypes.Contains(a.EventType));

        var totalCount = await query.CountAsync(cancellationToken);

        // Per D-04: newest first. Per D-06: 20 per page.
        const int pageSize = 20;
        var events = await query
            .OrderByDescending(a => a.CreatedAt)
            .Skip(page * pageSize)
            .Take(pageSize)
            .Select(a => new ActivityEventDto(
                a.Id,
                a.EventType,
                a.EntityType,
                a.EntityId,
                a.ActorId,
                a.Actor != null ? a.Actor.Name : null,
                a.CreatedAt,
                a.OldValues,
                a.NewValues))
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(new TimelineResponse(events, totalCount > (page + 1) * pageSize, totalCount, page));
    }
}
