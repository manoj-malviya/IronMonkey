using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.Data;

namespace IronMonkey.ApiService.Features.Leads.PipelineStages;

public class ListPipelineStagesEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapGet("/api/pipeline-stages", Handle)
        .WithSummary("List all active pipeline stages for the authenticated tenant ordered by Order")
        .WithTags("Pipeline Stages")
        .RequireAuthorization();

    public record Response(Guid Id, string Name, int Order, bool IsActive);

    private static async Task<Ok<List<Response>>> Handle(
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        CancellationToken cancellationToken)
    {
        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);

        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        var stages = await db.PipelineStages
            .OrderBy(p => p.Order)
            .ToListAsync(cancellationToken);

        var response = stages
            .Select(s => new Response(s.Id, s.Name, s.Order, s.IsActive))
            .ToList();

        return TypedResults.Ok(response);
    }
}
