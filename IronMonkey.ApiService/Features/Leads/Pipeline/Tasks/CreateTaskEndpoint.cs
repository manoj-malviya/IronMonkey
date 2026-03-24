using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.Data;
using IronMonkey.Data.Entities;

namespace IronMonkey.ApiService.Features.Leads.Pipeline.Tasks;

public class CreateTaskEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapPost("/api/leads/{leadId:guid}/tasks", Handle)
        .WithSummary("Create a task linked to a lead")
        .WithTags("Tasks")
        .RequireAuthorization();

    public record Request(
        string Title,
        string? Description,
        DateTime? DueDate,
        string Priority,
        Guid? AssignedToUserId);

    public record Response(Guid Id, string Title, string Priority, string Status, DateTime? DueDate, Guid? AssignedToUserId);

    private static async Task<Results<Created<Response>, BadRequest<string>, NotFound>> Handle(
        Guid leadId,
        Request request,
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Length > 200)
            return TypedResults.BadRequest("Title must be between 1 and 200 characters.");

        if (!Enum.TryParse<TaskPriority>(request.Priority, ignoreCase: true, out var priority))
            return TypedResults.BadRequest($"Invalid priority. Valid values: {string.Join(", ", Enum.GetNames<TaskPriority>())}");

        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);
        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        var leadExists = await db.Leads.AnyAsync(l => l.Id == leadId, cancellationToken);
        if (!leadExists) return TypedResults.NotFound();

        var task = LeadTask.Create(tenantId, leadId, request.Title, request.DueDate, priority, request.AssignedToUserId);
        if (request.Description != null)
            task.Update(task.Title, request.Description, task.DueDate, task.Priority, task.Status, task.AssignedToUserId);
        db.LeadTasks.Add(task);
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Created(
            $"/api/tasks/{task.Id}",
            new Response(task.Id, task.Title, task.Priority.ToString(), task.Status.ToString(), task.DueDate, task.AssignedToUserId));
    }
}
