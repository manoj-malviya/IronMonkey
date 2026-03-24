using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.BackgroundJobs;
using IronMonkey.ApiService.Features.Leads.Duplicates;
using IronMonkey.Data;
using IronMonkey.Data.Entities;

namespace IronMonkey.ApiService.Features.Leads.Ingestion.Csv;

public class CsvImportJob
{
    private readonly ITenantRegistry _tenantRegistry;
    private readonly ITenantDbContextFactory _dbContextFactory;
    private readonly IDuplicateDetectionService _duplicateDetection;
    private readonly CsvImportService _importService;
    private readonly ILogger<CsvImportJob> _logger;

    public CsvImportJob(
        ITenantRegistry tenantRegistry,
        ITenantDbContextFactory dbContextFactory,
        IDuplicateDetectionService duplicateDetection,
        CsvImportService importService,
        ILogger<CsvImportJob> logger)
    {
        _tenantRegistry = tenantRegistry;
        _dbContextFactory = dbContextFactory;
        _duplicateDetection = duplicateDetection;
        _importService = importService;
        _logger = logger;
    }

    // TenantId MUST be first parameter — explicit tenant context, survives process restarts
    [AutomaticRetry(Attempts = 2, DelaysInSeconds = [60, 300])]
    [Queue("tenant")]
    public async Task ProcessImportAsync(Guid tenantId, Guid batchId, string filePath,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting CSV import for tenant {TenantId} batch {BatchId}", tenantId, batchId);

        var connectionString = await _tenantRegistry.GetConnectionStringAsync(tenantId, cancellationToken);
        await using var db = _dbContextFactory.CreateForTenant(connectionString, tenantId);

        var batch = await db.ImportBatches.FindAsync(new object[] { batchId }, cancellationToken: cancellationToken);
        if (batch == null)
        {
            _logger.LogWarning("ImportBatch {BatchId} not found for tenant {TenantId}", batchId, tenantId);
            return;
        }

        batch.MarkStarted();
        await db.SaveChangesAsync(cancellationToken);

        var successCount = 0;
        var failureCount = 0;
        var errors = new List<ImportRowError>();

        try
        {
            var config = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = true,
                TrimOptions = TrimOptions.Trim,
                MissingFieldFound = null,  // Allow optional columns to be absent
                HeaderValidated = null     // We validate headers before enqueueing job
            };

            using var reader = new StreamReader(filePath);
            using var csv = new CsvReader(reader, config);

            // Get default pipeline stage for rows that don't specify one
            var defaultStageId = batch.DefaultPipelineStageId ?? await db.PipelineStages
                .Where(p => p.IsActive)
                .OrderBy(p => p.Order)
                .Select(p => p.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (defaultStageId == Guid.Empty)
            {
                batch.MarkFailed("No pipeline stage found for tenant. Create a pipeline stage first.");
                await db.SaveChangesAsync(cancellationToken);
                return;
            }

            var rowIndex = 1;  // 1-based for user-facing error messages (row 1 = first data row)

            await foreach (var record in csv.GetRecordsAsync<dynamic>())
            {
                try
                {
                    var row = (IDictionary<string, object>)record;
                    var lead = _importService.ParseRow(row, tenantId, defaultStageId);

                    // Check duplicates per D-14 — create anyway, flag if duplicate found
                    var duplicates = await _duplicateDetection.FindCandidatesAsync(
                        tenantId, lead.Email, lead.Mobile,
                        $"{lead.FirstName} {lead.LastName}", cancellationToken);

                    if (duplicates.Any())
                        lead.MarkAsPotentialDuplicate(duplicates.First().LeadId);

                    db.Leads.Add(lead);
                    successCount++;
                }
                catch (Exception ex)
                {
                    failureCount++;
                    errors.Add(new ImportRowError(rowIndex, ex.Message));
                    _logger.LogDebug("CSV import row {Row} failed: {Error}", rowIndex, ex.Message);
                }

                rowIndex++;

                // Save in batches of 100 to avoid holding large transactions
                if ((successCount + failureCount) % 100 == 0)
                {
                    await db.SaveChangesAsync(cancellationToken);
                    batch.UpdateProgress(successCount, failureCount);
                    await db.SaveChangesAsync(cancellationToken);
                }
            }

            // Save any remaining leads
            await db.SaveChangesAsync(cancellationToken);

            batch.MarkComplete(successCount, failureCount, errors.Any() ? errors : null);
            await db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "CSV import complete for tenant {TenantId} batch {BatchId}: {Success} imported, {Failed} skipped",
                tenantId, batchId, successCount, failureCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CSV import job failed for tenant {TenantId} batch {BatchId}", tenantId, batchId);
            batch.MarkFailed(ex.Message);
            await db.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            // Clean up temp file regardless of success/failure
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                _logger.LogDebug("Deleted temp CSV file {FilePath}", filePath);
            }
        }
    }
}
