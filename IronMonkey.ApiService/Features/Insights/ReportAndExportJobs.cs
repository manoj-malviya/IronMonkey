using System.Globalization;
using System.Text;
using System.Text.Json;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.BackgroundJobs;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.ApiService.Features.RoleManagement;
using IronMonkey.Common.Auth;
using IronMonkey.Data;
using IronMonkey.Data.Entities;
using IronMonkey.Data.Visibility;

namespace IronMonkey.ApiService.Features.Insights;

/// <summary>
/// Opens a tenant database as a specific user would see it. Background jobs have no request
/// and therefore no ambient visibility; anything they produce on a user's behalf — a scheduled
/// report, an export — must be narrowed to that user explicitly, or a job would quietly read
/// the whole tenant on behalf of someone allowed to see a slice of it.
/// </summary>
public static class ActingAs
{
    public static async Task<(TenantDbContext Db, HashSet<string> Permissions)?> OpenAsync(
        ITenantRegistry registry, ITenantDbContextFactory factory, Guid tenantId, Guid userId, CancellationToken ct)
    {
        var connectionString = await registry.GetConnectionStringAsync(tenantId, ct);

        RecordVisibility visibility;
        HashSet<string> permissions;
        await using (var system = new TenantDbContext(
            new DbContextOptionsBuilder<TenantDbContext>().UseNpgsql(connectionString).Options, tenantId, RecordVisibility.Unrestricted))
        {
            // A deactivated user's queued or scheduled work does not run.
            if (!await system.Users.AnyAsync(u => u.Id == userId, ct)) return null;
            visibility = await RecordVisibilityResolver.ResolveAsync(system, userId, ct);
            permissions = await PrivilegeGuard.ActorPermissionsAsync(system, userId, ct);
        }

        using (RecordVisibility.Enter(visibility))
            return (factory.CreateForTenant(connectionString, tenantId), permissions);
    }
}

/// <summary>Writes CSV that is safe to open in a spreadsheet.</summary>
public static class CsvWriter
{
    /// <summary>
    /// UTF-8 with a BOM, so Excel reads non-ASCII names correctly. Cells that begin with
    /// <c>= + - @</c>, tab or carriage return are prefixed with an apostrophe: a lead's name
    /// arrives from a public web form, and "=HYPERLINK(...)" in it would otherwise execute as a
    /// formula on the Admin's machine when the export is opened.
    /// </summary>
    public static byte[] Write(IEnumerable<string> header, IEnumerable<IEnumerable<string?>> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", header.Select(Cell)));
        foreach (var row in rows) sb.AppendLine(string.Join(",", row.Select(Cell)));
        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
    }

    public static string Cell(string? value)
    {
        var v = value ?? "";
        if (v.Length > 0 && v[0] is '=' or '+' or '-' or '@' or '\t' or '\r') v = "'" + v;
        return v.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
    }
}

public sealed record ExportRequest(string RecordType, string Source, ViewDefinition? Definition, Guid? ReportId, ReportSpec? ReportSpec);

/// <summary>The export job. Runs on the tenant queue, never in the request.</summary>
public class DataExportJob(ITenantRegistry registry, ITenantDbContextFactory factory, InsightService insights,
    TimeProvider timeProvider, ILogger<DataExportJob> logger)
{
    public const int MaxRows = 50_000;
    public const int TimeoutMs = 60_000;

    [Queue("tenant")]
    [AutomaticRetry(Attempts = 1)]
    public async Task RunAsync(Guid tenantId, Guid exportId, CancellationToken ct)
    {
        var connectionString = await registry.GetConnectionStringAsync(tenantId, ct);
        await using var system = new TenantDbContext(
            new DbContextOptionsBuilder<TenantDbContext>().UseNpgsql(connectionString).Options, tenantId, RecordVisibility.Unrestricted);

        var job = await system.ExportJobs.SingleOrDefaultAsync(e => e.Id == exportId, ct);
        if (job is null || job.Status is ExportStatus.Completed or ExportStatus.Failed) return;
        job.Start();
        await system.SaveChangesAsync(ct);

        try
        {
            var opened = await ActingAs.OpenAsync(registry, factory, tenantId, job.RequestedByUserId, ct);
            if (opened is null) { job.Fail("The requesting user is no longer active.", Now); await system.SaveChangesAsync(ct); return; }

            var (db, permissions) = opened.Value;
            await using (db)
            {
                var record = await RecordCatalog.LoadAsync(db, job.RecordType, ct);

                // Re-checked at run time, not just at request time: a permission revoked while the
                // job waited in the queue must stop it.
                if (!permissions.Contains(PermissionConstants.DataExport) || !permissions.Contains(record.ReadPermission))
                {
                    job.Fail("You no longer have permission to export this data.", Now);
                    await system.SaveChangesAsync(ct);
                    return;
                }

                var context = await insights.ContextAsync(db, tenantId, job.RequestedByUserId, ct);
                var (file, rows, truncated) = job.Source == "Report"
                    ? await ReportCsvAsync(db, record, context, job, ct)
                    : await ViewCsvAsync(db, record, context, job, ct);

                job.Complete($"{job.RecordType.ToString().ToLowerInvariant()}-{job.Source.ToLowerInvariant()}-{Now:yyyyMMdd-HHmm}.csv",
                    file, rows, truncated, Now);
                system.ActivityLogs.Add(ActivityLog.CreateFor(tenantId, "Export", job.Id, job.RequestedByUserId, "DataExported",
                    "ExportJob", job.Id.ToString(), null,
                    new() { ["RecordType"] = job.RecordType.ToString(), ["Source"] = job.Source, ["Rows"] = rows, ["Truncated"] = truncated }));
                await system.SaveChangesAsync(ct);
            }
        }
        catch (Exception ex) when (ex is QueryValidationException or QueryTimeoutException)
        {
            job.Fail(ex.Message, Now);
            await system.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Export {ExportId} failed for tenant {TenantId}", exportId, tenantId);
            job.Fail("The export failed unexpectedly.", Now);
            await system.SaveChangesAsync(CancellationToken.None);
        }
    }

    private DateTime Now => timeProvider.GetUtcNow().UtcDateTime;

    private static async Task<(byte[], int, bool)> ViewCsvAsync(TenantDbContext db, RecordDef record, QueryContext context,
        ExportJob job, CancellationToken ct)
    {
        var view = JsonSerializer.Deserialize<ViewDefinition>(job.DefinitionJson, Insights.Json) ?? new ViewDefinition();
        if (view.Columns.Count == 0) view.Columns = record.Fields.Values.Where(f => !f.IsCustom).Select(f => f.Key).ToList();

        // One statement capped at MaxRows + 1: the extra row only signals truncation.
        var query = new QueryCompiler(record, context).CompilePage(view, MaxRows + 1, 0);
        var raw = await InsightSql.QueryAsync(db, query, TimeoutMs, ct);
        var columns = view.Columns.Select(k => record.Fields[k]).ToList();

        var rows = raw.Take(MaxRows).Select(r => r.Skip(1).Select(v => v?.ToString())).ToList();
        return (CsvWriter.Write(columns.Select(c => c.Label), rows), rows.Count, raw.Count > MaxRows);
    }

    private async Task<(byte[], int, bool)> ReportCsvAsync(TenantDbContext db, RecordDef record, QueryContext context,
        ExportJob job, CancellationToken ct)
    {
        var spec = JsonSerializer.Deserialize<ReportSpec>(job.DefinitionJson, Insights.Json) ?? new ReportSpec();
        var result = await insights.RunReportAsync(db, record, context, spec, ct, TimeoutMs);
        var header = new List<string> { "Group", "Period" }.Concat(result.MeasureLabels);
        var rows = result.Rows.Select(r => new[] { r.GroupLabel, r.Bucket?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) }
            .Concat(r.Values.Select(v => v?.ToString(CultureInfo.InvariantCulture)))).ToList();
        return (CsvWriter.Write(header, rows), rows.Count, result.Truncated);
    }
}

/// <summary>Runs a saved report on its schedule, as its owner, and stores the result.</summary>
public class ScheduledReportJob(ITenantRegistry registry, ITenantDbContextFactory factory, InsightService insights,
    TimeProvider timeProvider, ILogger<ScheduledReportJob> logger)
{
    [Queue("tenant")]
    [AutomaticRetry(Attempts = 1)]
    public async Task RunAsync(Guid tenantId, Guid reportId, CancellationToken ct)
    {
        var connectionString = await registry.GetConnectionStringAsync(tenantId, ct);
        await using var system = new TenantDbContext(
            new DbContextOptionsBuilder<TenantDbContext>().UseNpgsql(connectionString).Options, tenantId, RecordVisibility.Unrestricted);

        var report = await system.ReportDefinitions.SingleOrDefaultAsync(r => r.Id == reportId, ct);
        if (report is null || report.Schedule == ReportSchedule.None) return;

        var now = timeProvider.GetUtcNow().UtcDateTime;
        string resultJson = "{}";
        string? error = null;

        try
        {
            var opened = await ActingAs.OpenAsync(registry, factory, tenantId, report.OwnerUserId, ct);
            if (opened is null) error = "The report owner is no longer active.";
            else
            {
                var (db, permissions) = opened.Value;
                await using (db)
                {
                    var record = await RecordCatalog.LoadAsync(db, report.RecordType, ct);
                    if (!permissions.Contains(record.ReadPermission)) error = "The report owner can no longer read this data.";
                    else
                    {
                        var spec = JsonSerializer.Deserialize<ReportSpec>(report.DefinitionJson, Insights.Json) ?? new ReportSpec();
                        var context = await insights.ContextAsync(db, tenantId, report.OwnerUserId, ct);
                        var result = await insights.RunReportAsync(db, record, context, spec, ct, DataExportJob.TimeoutMs);
                        resultJson = JsonSerializer.Serialize(result, Insights.Json);
                    }
                }
            }
        }
        catch (Exception ex) when (ex is QueryValidationException or QueryTimeoutException) { error = ex.Message; }
        catch (Exception ex)
        {
            logger.LogError(ex, "Scheduled report {ReportId} failed for tenant {TenantId}", reportId, tenantId);
            error = "The report failed unexpectedly.";
        }

        system.ReportRuns.Add(ReportRun.Record(tenantId, reportId, now, resultJson, error));
        report.MarkRun(now);
        await system.SaveChangesAsync(ct);
    }

    public static string JobId(Guid tenantId, Guid reportId) => $"report-{tenantId:N}-{reportId:N}";

    /// <summary>Cron in UTC. 06:00 so a morning report is ready at the start of a European/Asian day.</summary>
    public static string? Cron(ReportSchedule schedule) => schedule switch
    {
        ReportSchedule.Daily => "0 6 * * *",
        ReportSchedule.Weekly => "0 6 * * 1",
        ReportSchedule.Monthly => "0 6 1 * *",
        _ => null
    };
}
