using Microsoft.AspNetCore.Http.HttpResults;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.Data;
using IronMonkey.Data.Entities;

namespace IronMonkey.ApiService.Features.Leads.Workflow.Rules;

public class CreateWorkflowRuleEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapPost("/api/workflow-rules", Handle)
        .WithSummary("Create a workflow rule with trigger, condition, and action")
        .WithTags("Workflow")
        .RequireAuthorization();

    public record Request(string Name, string Trigger, string ConditionJson, string ActionJson);
    public record Response(Guid Id, string Name, string Trigger, bool IsActive);

    private static async Task<Results<Created<Response>, BadRequest<string>>> Handle(
        Request request,
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return TypedResults.BadRequest("Name is required.");

        if (!Enum.TryParse<WorkflowTrigger>(request.Trigger, ignoreCase: true, out var trigger))
            return TypedResults.BadRequest($"Invalid trigger. Valid: {string.Join(", ", Enum.GetNames<WorkflowTrigger>())}");

        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);
        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        var rule = WorkflowRule.Create(tenantId, request.Name, trigger, request.ConditionJson, request.ActionJson);
        db.WorkflowRules.Add(rule);
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Created($"/api/workflow-rules/{rule.Id}",
            new Response(rule.Id, rule.Name, rule.Trigger.ToString(), rule.IsActive));
    }
}
