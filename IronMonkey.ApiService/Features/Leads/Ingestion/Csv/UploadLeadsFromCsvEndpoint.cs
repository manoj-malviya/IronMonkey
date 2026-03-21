using Microsoft.AspNetCore.Http.HttpResults;
using Hangfire;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.Data;
using IronMonkey.Data.Entities;

namespace IronMonkey.ApiService.Features.Leads.Ingestion.Csv;

public class UploadLeadsFromCsvEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapPost("/api/leads/import/csv", Handle)
        .WithSummary("Upload a CSV file to bulk-import leads")
        .WithTags("Lead Import")
        .DisableAntiforgery()  // multipart/form-data upload
        .RequireAuthorization();

    public record Response(Guid ImportBatchId, string Status,
        string Message = "Import queued. Poll /api/leads/import/{id}/status for progress.");

    private static async Task<Results<Ok<Response>, BadRequest<string>>> Handle(
        IFormFile csvFile,
        Guid? pipelineStageId,
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        IBackgroundJobClient backgroundJobs,
        CsvImportService importService,
        CancellationToken cancellationToken)
    {
        if (csvFile == null || csvFile.Length == 0)
            return TypedResults.BadRequest("CSV file is required");

        if (!csvFile.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            return TypedResults.BadRequest("Only .csv files are supported");

        // Validate headers before enqueueing — fail fast on structure errors
        await using var headerStream = csvFile.OpenReadStream();
        var headerError = importService.ValidateHeaders(headerStream);
        if (headerError != null)
            return TypedResults.BadRequest(headerError);

        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);

        // Save to temp file — Hangfire job will clean up after processing
        var tempPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.csv");
        await using (var tempStream = File.Create(tempPath))
        {
            await csvFile.CopyToAsync(tempStream, cancellationToken);
        }

        // Create ImportBatch record
        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);
        var batch = ImportBatch.Create(tenantId, csvFile.FileName, tempPath, pipelineStageId);
        db.ImportBatches.Add(batch);
        await db.SaveChangesAsync(cancellationToken);

        // Enqueue Hangfire job
        backgroundJobs.Enqueue<CsvImportJob>(job =>
            job.ProcessImportAsync(tenantId, batch.Id, tempPath, CancellationToken.None));

        return TypedResults.Ok(new Response(batch.Id, "Pending"));
    }
}
