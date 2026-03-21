using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.Data;
using IronMonkey.Data.Entities;

namespace IronMonkey.ApiService.Features.Leads.Ingestion.Csv;

public class GetImportErrorsEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapGet("/api/leads/import/{batchId:guid}/errors", Handle)
        .WithSummary("Download CSV file listing import errors by row number")
        .WithTags("Lead Import")
        .RequireAuthorization();

    private static async Task<Results<FileContentHttpResult, NotFound>> Handle(
        Guid batchId,
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        CsvImportService importService,
        CancellationToken cancellationToken)
    {
        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);
        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        var batch = await db.ImportBatches
            .FirstOrDefaultAsync(b => b.Id == batchId, cancellationToken);

        if (batch == null || batch.ErrorDetailsJson == null)
            return TypedResults.NotFound();

        var errors = JsonSerializer.Deserialize<List<ImportRowError>>(batch.ErrorDetailsJson)
            ?? new List<ImportRowError>();
        var csvContent = importService.SerializeErrorsToCsv(errors);
        var bytes = System.Text.Encoding.UTF8.GetBytes(csvContent);

        return TypedResults.File(bytes, "text/csv", $"import-{batchId}-errors.csv");
    }
}
