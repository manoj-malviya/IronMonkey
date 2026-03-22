using Microsoft.AspNetCore.Http.HttpResults;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.Data;
using IronMonkey.Data.Entities;

namespace IronMonkey.ApiService.Features.Leads.Pipeline.Tasks;

public class UpdateTaskEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapPut("/api/tasks/{taskId:guid}", Handle)
        .WithSummary("Update a task's status, priority, or assignee")
        .WithTags("Tasks")
        .RequireAuthorization();

    public record Request(string Title, string? Description, DateTime? DueDate,
        string Priority, string Status, Guid? AssignedToUserId);

    public record Response(Guid Id, string Title, string Priority, string Status,
        DateTime? DueDate, Guid? AssignedToUserId);

    private static async Task<Results<Ok<Response>, NotFound, BadRequest<string>>> Handle(
        Guid taskId,
        Request request,
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<TaskPriority>(request.Priority, ignoreCase: true, out var priority))
            return TypedResults.BadRequest($"Invalid priority. Valid: {string.Join(", ", Enum.GetNames<TaskPriority>())}");

        if (!Enum.TryParse<IronMonkey.Data.Entities.TaskStatus>(request.Status, ignoreCase: true, out var status))
            return TypedResults.BadRequest($"Invalid status. Valid: {string.Join(", ", Enum.GetNames<IronMonkey.Data.Entities.TaskStatus>())}");

        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);
        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        var task = await db.LeadTasks.FindAsync(new object[] { taskId }, cancellationToken);
        if (task == null) return TypedResults.NotFound();

        task.Update(request.Title, request.Description, request.DueDate, priority, status, request.AssignedToUserId);
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(new Response(task.Id, task.Title, task.Priority.ToString(),
            task.Status.ToString(), task.DueDate, task.AssignedToUserId));
    }
}
