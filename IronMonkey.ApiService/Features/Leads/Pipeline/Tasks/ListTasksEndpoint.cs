using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.Data;

namespace IronMonkey.ApiService.Features.Leads.Pipeline.Tasks;

public class ListTasksEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapGet("/api/tasks", Handle)
        .WithSummary("List tasks, optionally filtered by assignee or lead")
        .WithTags("Tasks")
        .RequireAuthorization();

    public record TaskDto(Guid Id, Guid LeadId, string Title, string Priority, string Status,
        DateTime? DueDate, Guid? AssignedToUserId, DateTime CreatedAt);

    private static async Task<Ok<List<TaskDto>>> Handle(
        Guid? assignedTo,
        Guid? leadId,
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        CancellationToken cancellationToken)
    {
        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);
        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        var query = db.LeadTasks.AsQueryable();

        if (assignedTo.HasValue)
            query = query.Where(t => t.AssignedToUserId == assignedTo.Value);

        if (leadId.HasValue)
            query = query.Where(t => t.LeadId == leadId.Value);

        var tasks = await query
            .OrderBy(t => t.DueDate)
            .ThenBy(t => t.Priority)
            .Select(t => new TaskDto(t.Id, t.LeadId, t.Title, t.Priority.ToString(),
                t.Status.ToString(), t.DueDate, t.AssignedToUserId, t.CreatedAt))
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(tasks);
    }
}
