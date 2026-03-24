using System.Text.Json;
using IronMonkey.Data.Abstractions;

namespace IronMonkey.Data.Entities;

public enum ImportBatchStatus { Pending = 0, Processing = 1, Complete = 2, Failed = 3 }

/// <summary>
/// Tracks the state of a single CSV import job.
/// Lives in the tenant DB (per-tenant isolation).
/// Progress is updated during Hangfire job execution; UI polls this record.
/// </summary>
public sealed class ImportBatch : BaseTenantEntity
{
    private ImportBatch(Guid id, Guid tenantId, string originalFileName, string tempFilePath,
        Guid? defaultPipelineStageId)
        : base(id, tenantId)
    {
        OriginalFileName = originalFileName;
        TempFilePath = tempFilePath;
        DefaultPipelineStageId = defaultPipelineStageId;
        Status = ImportBatchStatus.Pending;
        TotalRows = 0;
        ImportedRows = 0;
        SkippedRows = 0;
    }

    private ImportBatch() { }

    public string OriginalFileName { get; private set; } = string.Empty;
    public string TempFilePath { get; private set; } = string.Empty;
    public Guid? DefaultPipelineStageId { get; private set; }
    public ImportBatchStatus Status { get; private set; }
    public int TotalRows { get; private set; }
    public int ImportedRows { get; private set; }
    public int SkippedRows { get; private set; }

    /// <summary>JSON array of ImportRowError objects. Stored as text for portability.</summary>
    public string? ErrorDetailsJson { get; private set; }

    public string? FailureReason { get; private set; }

    public static ImportBatch Create(Guid tenantId, string originalFileName, string tempFilePath,
        Guid? defaultPipelineStageId = null)
        => new ImportBatch(Guid.NewGuid(), tenantId, originalFileName, tempFilePath, defaultPipelineStageId);

    public void MarkStarted()
    {
        Status = ImportBatchStatus.Processing;
    }

    public void UpdateProgress(int imported, int skipped)
    {
        ImportedRows = imported;
        SkippedRows = skipped;
        TotalRows = imported + skipped;
    }

    public void MarkComplete(int imported, int skipped, List<ImportRowError>? errors = null)
    {
        Status = ImportBatchStatus.Complete;
        ImportedRows = imported;
        SkippedRows = skipped;
        TotalRows = imported + skipped;
        if (errors?.Any() == true)
            ErrorDetailsJson = JsonSerializer.Serialize(errors);
    }

    public void MarkFailed(string reason)
    {
        Status = ImportBatchStatus.Failed;
        FailureReason = reason;
    }
}

public record ImportRowError(int RowNumber, string ErrorMessage);
