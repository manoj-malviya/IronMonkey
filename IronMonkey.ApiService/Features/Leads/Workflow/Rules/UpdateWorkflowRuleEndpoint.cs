using Microsoft.AspNetCore.Http.HttpResults;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.Data;
using IronMonkey.Data.Entities;

namespace IronMonkey.ApiService.Features.Leads.Workflow.Rules;

public class UpdateWorkflowRuleEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app)
    {
        // Update rule definition
        app.MapPut("/api/workflow-rules/{ruleId:guid}", Handle)
            .WithSummary("Update a workflow rule's name, trigger, condition, or action")
            .WithTags("Workflow")
            .RequireAuthorization();

        // Toggle active/inactive (D-12)
        app.MapPost("/api/workflow-rules/{ruleId:guid}/toggle", Toggle)
            .WithSummary("Toggle workflow rule active/inactive state (preserves rule, per D-12)")
            .WithTags("Workflow")
            .RequireAuthorization();
    }

    public record UpdateRequest(string Name, string Trigger, string ConditionJson, string ActionJson);
    public record Response(Guid Id, string Name, string Trigger, bool IsActive);

    private static async Task<Results<Ok<Response>, NotFound, BadRequest<string>>> Handle(
        Guid ruleId,
        UpdateRequest request,
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<WorkflowTrigger>(request.Trigger, ignoreCase: true, out var trigger))
            return TypedResults.BadRequest($"Invalid trigger. Valid: {string.Join(", ", Enum.GetNames<WorkflowTrigger>())}");

        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);
        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        var rule = await db.WorkflowRules.FindAsync(new object[] { ruleId }, cancellationToken);
        if (rule == null) return TypedResults.NotFound();

        rule.Update(request.Name, trigger, request.ConditionJson, request.ActionJson);
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(new Response(rule.Id, rule.Name, rule.Trigger.ToString(), rule.IsActive));
    }

    private static async Task<Results<Ok<Response>, NotFound>> Toggle(
        Guid ruleId,
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        CancellationToken cancellationToken)
    {
        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);
        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        var rule = await db.WorkflowRules.FindAsync(new object[] { ruleId }, cancellationToken);
        if (rule == null) return TypedResults.NotFound();

        rule.SetActive(!rule.IsActive);
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(new Response(rule.Id, rule.Name, rule.Trigger.ToString(), rule.IsActive));
    }
}
