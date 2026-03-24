using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.Data;

namespace IronMonkey.ApiService.Features.Leads.Workflow.Rules;

public class ListWorkflowRulesEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapGet("/api/workflow-rules", Handle)
        .WithSummary("List all workflow rules for the tenant")
        .WithTags("Workflow")
        .RequireAuthorization();

    public record RuleDto(Guid Id, string Name, string Trigger, string ConditionJson,
        string ActionJson, bool IsActive, DateTime CreatedAt);

    private static async Task<Ok<List<RuleDto>>> Handle(
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        CancellationToken cancellationToken)
    {
        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);
        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        var rules = await db.WorkflowRules
            .OrderBy(r => r.CreatedAt)
            .Select(r => new RuleDto(r.Id, r.Name, r.Trigger.ToString(), r.ConditionJson,
                r.ActionJson, r.IsActive, r.CreatedAt))
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(rules);
    }
}
