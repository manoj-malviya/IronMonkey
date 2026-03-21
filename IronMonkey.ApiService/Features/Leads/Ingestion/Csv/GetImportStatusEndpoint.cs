using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.Data;

namespace IronMonkey.ApiService.Features.Leads.Ingestion.Csv;

public class GetImportStatusEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapGet("/api/leads/import/{batchId:guid}/status", Handle)
        .WithSummary("Poll import batch progress")
        .WithTags("Lead Import")
        .RequireAuthorization();

    public record Response(
        Guid BatchId,
        string Status,
        int TotalRows,
        int ImportedRows,
        int SkippedRows,
        bool HasErrors);

    private static async Task<Results<Ok<Response>, NotFound>> Handle(
        Guid batchId,
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        CancellationToken cancellationToken)
    {
        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);
        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        var batch = await db.ImportBatches
            .FirstOrDefaultAsync(b => b.Id == batchId, cancellationToken);

        if (batch == null)
            return TypedResults.NotFound();

        return TypedResults.Ok(new Response(
            batch.Id,
            batch.Status.ToString(),
            batch.TotalRows,
            batch.ImportedRows,
            batch.SkippedRows,
            batch.ErrorDetailsJson != null));
    }
}
