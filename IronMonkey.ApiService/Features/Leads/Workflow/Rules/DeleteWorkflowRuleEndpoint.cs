using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.Data;

namespace IronMonkey.ApiService.Features.Leads.Workflow.Rules;

public class DeleteWorkflowRuleEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapDelete("/api/workflow-rules/{id:guid}", Handle)
        .WithSummary("Delete a workflow rule for the authenticated tenant")
        .WithTags("Workflow")
        .RequireAuthorization();

    public record Response(bool Success, string? Message);

    private static async Task<Results<Ok<Response>, NotFound>> Handle(
        Guid id,
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        CancellationToken cancellationToken)
    {
        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);
        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        var rule = await db.WorkflowRules
            .SingleOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (rule is null) return TypedResults.NotFound();

        db.WorkflowRules.Remove(rule);
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(new Response(true, null));
    }
}
