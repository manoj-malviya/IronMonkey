using Microsoft.AspNetCore.Http.HttpResults;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.Data;
using Microsoft.EntityFrameworkCore;

namespace IronMonkey.ApiService.Features.Leads.Pipeline.States;

public class ListTransitionsEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapGet("/api/stage-transitions", Handle)
        .WithSummary("List all configured stage transitions for the tenant")
        .WithTags("Pipeline")
        .RequireAuthorization();

    public record TransitionDto(Guid Id, Guid FromStageId, string FromStageName, Guid ToStageId, string ToStageName);

    private static async Task<Ok<List<TransitionDto>>> Handle(
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        CancellationToken cancellationToken)
    {
        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);
        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        var transitions = await db.StageTransitions
            .Include(t => t.FromStage)
            .Include(t => t.ToStage)
            .OrderBy(t => t.FromStage.Order)
            .ThenBy(t => t.ToStage.Order)
            .Select(t => new TransitionDto(t.Id, t.FromStageId, t.FromStage.Name, t.ToStageId, t.ToStage.Name))
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(transitions);
    }
}
