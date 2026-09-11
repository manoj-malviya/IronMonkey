using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.Data;

namespace IronMonkey.ApiService.Features.Activity.Timeline;

/// <summary>
/// Timeline for any subject — lead, contact or opportunity. The lead-only
/// <see cref="GetLeadActivityTimelineEndpoint"/> stays for its existing callers.
/// </summary>
public class GetActivityTimelineEndpoint : IEndpoint
{
    private static readonly string[] AllowedSubjects = ["Lead", "Contact", "Opportunity"];

    public static void Map(IEndpointRouteBuilder app) => app
        .MapGet("/api/activity/{subjectType}/{subjectId:guid}", Handle)
        .WithSummary("Get the paginated activity timeline for a lead, contact or opportunity")
        .WithTags("Activity")
        .RequireAuthorization();

    public record ActivityEventDto(
        Guid Id,
        string EventType,
        string EntityType,
        string EntityId,
        Guid? ActorId,
        string? ActorName,
        DateTime OccurredAt,
        Dictionary<string, object?>? OldValues,
        Dictionary<string, object?>? NewValues);

    public record TimelineResponse(
        List<ActivityEventDto> Events,
        bool HasMore,
        int TotalCount,
        int Page);

    private static async Task<Results<Ok<TimelineResponse>, BadRequest<string>>> Handle(
        string subjectType,
        Guid subjectId,
        int? page,
        string? eventTypes,
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        CancellationToken cancellationToken)
    {
        var subject = AllowedSubjects.FirstOrDefault(
            s => string.Equals(s, subjectType, StringComparison.OrdinalIgnoreCase));

        if (subject is null)
            return TypedResults.BadRequest($"Invalid subject type. Valid values: {string.Join(", ", AllowedSubjects)}");

        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);
        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        var query = db.ActivityLogs.Where(a => a.SubjectType == subject && a.SubjectId == subjectId);

        var selectedTypes = string.IsNullOrWhiteSpace(eventTypes)
            ? []
            : eventTypes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (selectedTypes.Length > 0)
            query = query.Where(a => selectedTypes.Contains(a.EventType));

        var totalCount = await query.CountAsync(cancellationToken);

        var pageNumber = page.GetValueOrDefault();
        const int pageSize = 20;
        var rows = await query
            .OrderByDescending(a => a.CreatedAt)
            .Skip(pageNumber * pageSize)
            .Take(pageSize)
            .Select(a => new
            {
                a.Id, a.EventType, a.EntityType, a.EntityId, a.ActorId,
                a.CreatedAt, a.OldValues, a.NewValues
            })
            .ToListAsync(cancellationToken);

        // Actor names are looked up separately: User has a soft-delete query filter, and
        // composing that filtered principal into the projection above makes EF throw.
        // IgnoreQueryFilters so a deactivated user's past actions still show their name.
        var actorIds = rows.Where(r => r.ActorId.HasValue).Select(r => r.ActorId!.Value).Distinct().ToList();
        var actorNames = actorIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await db.Users
                .IgnoreQueryFilters()
                .Where(u => u.TenantId == tenantId && actorIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.Name, cancellationToken);

        var events = rows.Select(r => new ActivityEventDto(
            r.Id, r.EventType, r.EntityType, r.EntityId, r.ActorId,
            r.ActorId.HasValue && actorNames.TryGetValue(r.ActorId.Value, out var name) ? name : null,
            r.CreatedAt, r.OldValues, r.NewValues)).ToList();

        return TypedResults.Ok(new TimelineResponse(
            events, totalCount > (pageNumber + 1) * pageSize, totalCount, pageNumber));
    }
}
