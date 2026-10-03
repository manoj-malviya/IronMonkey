using IronMonkey.Data.Abstractions;

namespace IronMonkey.Data.Entities;

/// <summary>Record types the view, report and export engine can query.</summary>
public enum InsightRecordType { Lead = 0, Contact = 1, Opportunity = 2, Task = 3 }

/// <summary>
/// A named filter, column selection and sort over one record type.
///
/// <para><b>Sharing never widens visibility.</b> A shared view is a saved <i>question</i>, not
/// a saved <i>answer</i>: it is executed under the viewer's own record visibility every time,
/// so sharing it shows each colleague only the records their own scope allows.</para>
/// </summary>
public sealed class SavedView : BaseTenantEntity
{
    private SavedView() { }

    public Guid OwnerUserId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public InsightRecordType RecordType { get; private set; }

    /// <summary>Filter, columns and sort, as JSON validated by the query compiler on save and run.</summary>
    public string DefinitionJson { get; private set; } = "{}";
    public bool IsShared { get; private set; }

    public static SavedView Create(Guid tenantId, Guid ownerUserId, string name, InsightRecordType type, string definitionJson, bool isShared) => new()
    {
        Id = Guid.NewGuid(), TenantId = tenantId, OwnerUserId = ownerUserId,
        Name = name.Trim(), RecordType = type, DefinitionJson = definitionJson, IsShared = isShared
    };

    public void Update(string name, string definitionJson, bool isShared)
    {
        Name = name.Trim();
        DefinitionJson = definitionJson;
        IsShared = isShared;
    }
}

/// <summary>A user's default view for one list. One row per (user, record type).</summary>
public sealed class UserDefaultView
{
    private UserDefaultView() { }

    public Guid TenantId { get; private set; }
    public Guid UserId { get; private set; }
    public InsightRecordType RecordType { get; private set; }
    public Guid SavedViewId { get; private set; }

    public static UserDefaultView Create(Guid tenantId, Guid userId, InsightRecordType type, Guid viewId) =>
        new() { TenantId = tenantId, UserId = userId, RecordType = type, SavedViewId = viewId };

    public void Point(Guid viewId) => SavedViewId = viewId;
}

public enum ReportSchedule { None = 0, Daily = 1, Weekly = 2, Monthly = 3 }

/// <summary>
/// A tenant-defined report: record type, filter, grouping, measures and time dimension, as
/// JSON compiled to a parameterised query. Like a saved view, a shared report runs under the
/// viewer's visibility; a scheduled run executes under its owner's.
/// </summary>
public sealed class ReportDefinition : BaseTenantEntity
{
    private ReportDefinition() { }

    public Guid OwnerUserId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public InsightRecordType RecordType { get; private set; }
    public string DefinitionJson { get; private set; } = "{}";
    public bool IsShared { get; private set; }
    public ReportSchedule Schedule { get; private set; }
    public DateTime? LastRunAt { get; private set; }

    public static ReportDefinition Create(Guid tenantId, Guid ownerUserId, string name, InsightRecordType type,
        string definitionJson, bool isShared, ReportSchedule schedule) => new()
    {
        Id = Guid.NewGuid(), TenantId = tenantId, OwnerUserId = ownerUserId, Name = name.Trim(),
        RecordType = type, DefinitionJson = definitionJson, IsShared = isShared, Schedule = schedule
    };

    public void Update(string name, string definitionJson, bool isShared, ReportSchedule schedule)
    {
        Name = name.Trim();
        DefinitionJson = definitionJson;
        IsShared = isShared;
        Schedule = schedule;
    }

    public void MarkRun(DateTime atUtc) => LastRunAt = atUtc;
}

public enum ExportStatus { Queued = 0, Running = 1, Completed = 2, Failed = 3 }

/// <summary>
/// One export: who asked, for what, when, how many rows. <b>The row is the audit record and is
/// kept</b>; only the file content is dropped once it expires. Export is the highest-volume
/// disclosure path in a CRM, so every one is attributable after the fact.
/// </summary>
public sealed class ExportJob : BaseTenantEntity
{
    private ExportJob() { }

    public Guid RequestedByUserId { get; private set; }
    public InsightRecordType RecordType { get; private set; }

    /// <summary>"View", "Report" or "List".</summary>
    public string Source { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public string DefinitionJson { get; private set; } = "{}";
    public ExportStatus Status { get; private set; }
    public int? RowCount { get; private set; }
    public bool WasTruncated { get; private set; }
    public string? FileName { get; private set; }
    public byte[]? Content { get; private set; }
    public string? Error { get; private set; }
    public DateTime? CompletedAt { get; private set; }
    public DateTime ExpiresAt { get; private set; }

    public static ExportJob Queue(Guid tenantId, Guid userId, InsightRecordType type, string source, string description,
        string definitionJson, DateTime expiresAtUtc) => new()
    {
        Id = Guid.NewGuid(), TenantId = tenantId, RequestedByUserId = userId, RecordType = type,
        Source = source, Description = description, DefinitionJson = definitionJson,
        Status = ExportStatus.Queued, ExpiresAt = expiresAtUtc
    };

    public void Start() => Status = ExportStatus.Running;

    public void Complete(string fileName, byte[] content, int rowCount, bool truncated, DateTime nowUtc)
    {
        Status = ExportStatus.Completed;
        FileName = fileName;
        Content = content;
        RowCount = rowCount;
        WasTruncated = truncated;
        CompletedAt = nowUtc;
    }

    public void Fail(string error, DateTime nowUtc)
    {
        Status = ExportStatus.Failed;
        Error = error.Length > 1000 ? error[..1000] : error;
        CompletedAt = nowUtc;
    }

    public void PurgeContent() => Content = null;
}

/// <summary>The stored result of a scheduled report run.</summary>
public sealed class ReportRun : BaseTenantEntity
{
    private ReportRun() { }

    public Guid ReportDefinitionId { get; private set; }
    public DateTime RanAt { get; private set; }
    public string ResultJson { get; private set; } = "{}";
    public string? Error { get; private set; }

    public static ReportRun Record(Guid tenantId, Guid reportId, DateTime ranAt, string resultJson, string? error) => new()
    {
        Id = Guid.NewGuid(), TenantId = tenantId, ReportDefinitionId = reportId, RanAt = ranAt, ResultJson = resultJson, Error = error
    };
}
