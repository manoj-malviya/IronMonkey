using System.Text.Json;
using Hangfire;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.Common.Auth;
using IronMonkey.Data;
using IronMonkey.Data.Entities;

namespace IronMonkey.ApiService.Features.Insights;

public sealed record SaveReportRequest(string Name, string RecordType, ReportSpec Spec, bool IsShared, string? Schedule);

public sealed record ReportDefinitionResponse(Guid Id, string Name, string RecordType, ReportSpec Spec, bool IsShared, bool IsOwner,
    string Schedule, DateTime? LastRunAt, List<string> Problems);

public sealed record ReportRunResponse(Guid Id, DateTime RanAt, string? Error, JsonElement? Result);

public sealed record ExportJobResponse(Guid Id, string RecordType, string Source, string Description, string Status,
    int? RowCount, bool WasTruncated, string? Error, DateTime CreatedAt, DateTime? CompletedAt, DateTime ExpiresAt, bool CanDownload);

/// <summary>
/// The report builder. Previewing and running need the record type's read permission; a shared
/// report runs under each viewer's own visibility; only the owner edits, deletes or schedules.
/// The four fixed dashboard reports are untouched and keep their own endpoints.
/// </summary>
public class ReportBuilderEndpoints : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/reports/run", Run).WithTags("Report Builder").WithSummary("Run an unsaved report definition").RequireAuthorization();
        app.MapGet("/api/reports/definitions", List).WithTags("Report Builder").RequireAuthorization();
        app.MapPost("/api/reports/definitions", Create).WithTags("Report Builder").RequireAuthorization(PermissionConstants.ReportsRead);
        app.MapPut("/api/reports/definitions/{id:guid}", Update).WithTags("Report Builder").RequireAuthorization(PermissionConstants.ReportsRead);
        app.MapDelete("/api/reports/definitions/{id:guid}", Delete).WithTags("Report Builder").RequireAuthorization();
        app.MapGet("/api/reports/definitions/{id:guid}/run", RunSaved).WithTags("Report Builder").RequireAuthorization();
        app.MapGet("/api/reports/definitions/{id:guid}/runs", Runs).WithTags("Report Builder").RequireAuthorization();
    }

    internal static async Task<IResult> Run(ReportRunRequest request, ITenantService tenantService, ITenantDbContextFactory factory,
        IUserContext user, InsightService insights, CancellationToken ct)
    {
        if (!Insights.TryType(request.RecordType, out var type)) return TypedResults.BadRequest("Unknown record type.");
        var (db, tenantId) = await Insights.OpenAsync(tenantService, factory, ct);
        await using (db) return await ExecuteAsync(db, tenantId, user.UserId, type, request.Spec, insights, ct);
    }

    internal static async Task<IResult> RunSaved(Guid id, ITenantService tenantService, ITenantDbContextFactory factory,
        IUserContext user, InsightService insights, CancellationToken ct)
    {
        var (db, tenantId) = await Insights.OpenAsync(tenantService, factory, ct);
        await using (db)
        {
            var report = await Visible(db, user.UserId).SingleOrDefaultAsync(r => r.Id == id, ct);
            if (report is null) return TypedResults.NotFound();
            var spec = JsonSerializer.Deserialize<ReportSpec>(report.DefinitionJson, Insights.Json) ?? new ReportSpec();
            return await ExecuteAsync(db, tenantId, user.UserId, report.RecordType, spec, insights, ct);
        }
    }

    private static async Task<IResult> ExecuteAsync(TenantDbContext db, Guid tenantId, Guid userId, InsightRecordType type,
        ReportSpec spec, InsightService insights, CancellationToken ct)
    {
        var record = await RecordCatalog.LoadAsync(db, type, ct);
        if (!await InsightService.CanReadAsync(db, userId, record, ct)) return TypedResults.Forbid();
        try
        {
            var context = await insights.ContextAsync(db, tenantId, userId, ct);
            return TypedResults.Ok(await insights.RunReportAsync(db, record, context, spec, ct));
        }
        catch (QueryValidationException ex) { return Insights.Problems(ex); }
        catch (QueryTimeoutException ex) { return Insights.Timeout(ex); }
    }

    internal static async Task<IResult> List(ITenantService tenantService, ITenantDbContextFactory factory, IUserContext user, CancellationToken ct)
    {
        var (db, tenantId) = await Insights.OpenAsync(tenantService, factory, ct);
        await using (db)
        {
            var held = await RoleManagement.PrivilegeGuard.ActorPermissionsAsync(db, user.UserId, ct);
            var reports = await Visible(db, user.UserId).AsNoTracking().OrderBy(r => r.Name).ThenBy(r => r.Id).ToListAsync(ct);
            var result = new List<ReportDefinitionResponse>();
            foreach (var r in reports)
            {
                var record = await RecordCatalog.LoadAsync(db, r.RecordType, ct);
                // A shared report over a type the viewer cannot read is not listed at all.
                if (!held.Contains(record.ReadPermission)) continue;
                result.Add(ToResponse(r, record, user.UserId, tenantId));
            }
            return TypedResults.Ok(result);
        }
    }

    internal static async Task<IResult> Create(SaveReportRequest request, ITenantService tenantService, ITenantDbContextFactory factory,
        IUserContext user, InsightService insights, IRecurringJobManager recurring, CancellationToken ct)
    {
        if (!Insights.TryType(request.RecordType, out var type)) return TypedResults.BadRequest("Unknown record type.");
        if (!TryParseSchedule(request.Schedule, out var schedule)) return TypedResults.BadRequest("Schedule must be None, Daily, Weekly or Monthly.");
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 100) return TypedResults.BadRequest("A report needs a name of up to 100 characters.");

        var (db, tenantId) = await Insights.OpenAsync(tenantService, factory, ct);
        await using (db)
        {
            var record = await RecordCatalog.LoadAsync(db, type, ct);
            if (!await InsightService.CanReadAsync(db, user.UserId, record, ct)) return TypedResults.Forbid();
            if (await ValidateAsync(db, record, tenantId, user.UserId, request.Spec, insights, ct) is { } problems) return problems;

            var report = ReportDefinition.Create(tenantId, user.UserId, request.Name, type,
                JsonSerializer.Serialize(request.Spec, Insights.Json), request.IsShared, schedule);
            db.ReportDefinitions.Add(report);
            await db.SaveChangesAsync(ct);
            ApplySchedule(recurring, tenantId, report);
            return TypedResults.Created($"/api/reports/definitions/{report.Id}", new { report.Id });
        }
    }

    internal static async Task<IResult> Update(Guid id, SaveReportRequest request, ITenantService tenantService, ITenantDbContextFactory factory,
        IUserContext user, InsightService insights, IRecurringJobManager recurring, CancellationToken ct)
    {
        if (!TryParseSchedule(request.Schedule, out var schedule)) return TypedResults.BadRequest("Schedule must be None, Daily, Weekly or Monthly.");
        var (db, tenantId) = await Insights.OpenAsync(tenantService, factory, ct);
        await using (db)
        {
            var report = await db.ReportDefinitions.SingleOrDefaultAsync(r => r.Id == id, ct);
            if (report is null || report.OwnerUserId != user.UserId) return TypedResults.NotFound();

            var record = await RecordCatalog.LoadAsync(db, report.RecordType, ct);
            if (await ValidateAsync(db, record, tenantId, user.UserId, request.Spec, insights, ct) is { } problems) return problems;

            report.Update(request.Name, JsonSerializer.Serialize(request.Spec, Insights.Json), request.IsShared, schedule);
            report.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            ApplySchedule(recurring, tenantId, report);
            return TypedResults.NoContent();
        }
    }

    internal static async Task<IResult> Delete(Guid id, ITenantService tenantService, ITenantDbContextFactory factory,
        IUserContext user, IRecurringJobManager recurring, CancellationToken ct)
    {
        var (db, tenantId) = await Insights.OpenAsync(tenantService, factory, ct);
        await using (db)
        {
            var report = await db.ReportDefinitions.SingleOrDefaultAsync(r => r.Id == id, ct);
            if (report is null || report.OwnerUserId != user.UserId) return TypedResults.NotFound();
            report.IsDeleted = true;
            report.DeletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            recurring.RemoveIfExists(ScheduledReportJob.JobId(tenantId, id));
            return TypedResults.NoContent();
        }
    }

    internal static async Task<IResult> Runs(Guid id, ITenantService tenantService, ITenantDbContextFactory factory,
        IUserContext user, CancellationToken ct)
    {
        var (db, _) = await Insights.OpenAsync(tenantService, factory, ct);
        await using (db)
        {
            // Stored runs were computed with the OWNER's visibility, so only the owner reads them.
            var report = await db.ReportDefinitions.SingleOrDefaultAsync(r => r.Id == id && r.OwnerUserId == user.UserId, ct);
            if (report is null) return TypedResults.NotFound();
            var runs = await db.ReportRuns.AsNoTracking().Where(r => r.ReportDefinitionId == id)
                .OrderByDescending(r => r.RanAt).Take(20).ToListAsync(ct);
            return TypedResults.Ok(runs.Select(r => new ReportRunResponse(r.Id, r.RanAt, r.Error,
                r.Error is null ? JsonSerializer.Deserialize<JsonElement>(r.ResultJson) : null)).ToList());
        }
    }

    private static IQueryable<ReportDefinition> Visible(TenantDbContext db, Guid userId) =>
        db.ReportDefinitions.Where(r => r.OwnerUserId == userId || r.IsShared);

    private static async Task<IResult?> ValidateAsync(TenantDbContext db, RecordDef record, Guid tenantId, Guid userId, ReportSpec spec,
        InsightService insights, CancellationToken ct)
    {
        try
        {
            new QueryCompiler(record, await insights.ContextAsync(db, tenantId, userId, ct))
                .CompileReport(spec, InsightService.ReportGroupCap);
            return null;
        }
        catch (QueryValidationException ex) { return Insights.Problems(ex); }
    }

    private static void ApplySchedule(IRecurringJobManager recurring, Guid tenantId, ReportDefinition report)
    {
        var jobId = ScheduledReportJob.JobId(tenantId, report.Id);
        if (ScheduledReportJob.Cron(report.Schedule) is { } cron)
            recurring.AddOrUpdate<ScheduledReportJob>(jobId, "tenant", j => j.RunAsync(tenantId, report.Id, CancellationToken.None), cron);
        else
            recurring.RemoveIfExists(jobId);
    }

    private static bool TryParseSchedule(string? value, out ReportSchedule schedule)
    {
        schedule = ReportSchedule.None;
        return string.IsNullOrWhiteSpace(value) || (Enum.TryParse(value, true, out schedule) && Enum.IsDefined(schedule));
    }

    private static ReportDefinitionResponse ToResponse(ReportDefinition r, RecordDef record, Guid userId, Guid tenantId)
    {
        var spec = JsonSerializer.Deserialize<ReportSpec>(r.DefinitionJson, Insights.Json) ?? new ReportSpec();
        var problems = new List<string>();
        try
        {
            new QueryCompiler(record, new QueryContext(tenantId, userId, Data.Visibility.RecordVisibility.Unrestricted, TimeZoneInfo.Utc, DateTime.UtcNow))
                .CompileReport(spec, InsightService.ReportGroupCap);
        }
        catch (QueryValidationException ex) { problems.AddRange(ex.Problems); }

        return new ReportDefinitionResponse(r.Id, r.Name, r.RecordType.ToString(), spec, r.IsShared, r.OwnerUserId == userId,
            r.Schedule.ToString(), r.LastRunAt, problems);
    }
}

/// <summary>
/// Exports. Queuing needs <c>data:export</c> and the record type's read permission; the file is
/// built by <see cref="DataExportJob"/> on the tenant queue under the requester's visibility and
/// is downloadable only by the requester, until it expires. Every request leaves an audit row.
/// </summary>
public class ExportEndpoints : IEndpoint
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);

    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/exports", Queue).WithTags("Exports").WithSummary("Queue a CSV export of a view or report")
            .RequireAuthorization(PermissionConstants.DataExport);
        app.MapGet("/api/exports", List).WithTags("Exports").RequireAuthorization();
        app.MapGet("/api/exports/{id:guid}/download", Download).WithTags("Exports").RequireAuthorization(PermissionConstants.DataExport);
    }

    internal static async Task<IResult> Queue(ExportRequest request, ITenantService tenantService, ITenantDbContextFactory factory,
        IUserContext user, InsightService insights, IBackgroundJobClient jobs, TimeProvider clock, CancellationToken ct)
    {
        if (!Insights.TryType(request.RecordType, out var type)) return TypedResults.BadRequest("Unknown record type.");
        var source = request.Source?.Trim().ToLowerInvariant();
        if (source is not ("view" or "report")) return TypedResults.BadRequest("Source must be view or report.");

        var (db, tenantId) = await Insights.OpenAsync(tenantService, factory, ct);
        await using (db)
        {
            var record = await RecordCatalog.LoadAsync(db, type, ct);
            if (!await InsightService.CanReadAsync(db, user.UserId, record, ct)) return TypedResults.Forbid();
            var context = await insights.ContextAsync(db, tenantId, user.UserId, ct);

            string definitionJson, description;
            try
            {
                if (source == "view")
                {
                    var view = request.Definition ?? new ViewDefinition();
                    var problems = new QueryCompiler(record, context).Validate(view);
                    if (problems.Count > 0) throw new QueryValidationException(problems);
                    definitionJson = JsonSerializer.Serialize(view, Insights.Json);
                    description = $"{type} list";
                }
                else
                {
                    ReportSpec spec;
                    if (request.ReportId is { } reportId)
                    {
                        var report = await db.ReportDefinitions.SingleOrDefaultAsync(r => r.Id == reportId && (r.OwnerUserId == user.UserId || r.IsShared), ct);
                        if (report is null) return TypedResults.NotFound();
                        spec = JsonSerializer.Deserialize<ReportSpec>(report.DefinitionJson, Insights.Json) ?? new ReportSpec();
                        description = $"Report: {report.Name}";
                    }
                    else
                    {
                        spec = request.ReportSpec ?? new ReportSpec();
                        description = $"{type} report";
                    }
                    new QueryCompiler(record, context).CompileReport(spec, InsightService.ReportGroupCap);
                    definitionJson = JsonSerializer.Serialize(spec, Insights.Json);
                }
            }
            catch (QueryValidationException ex) { return Insights.Problems(ex); }

            var job = ExportJob.Queue(tenantId, user.UserId, type, source == "view" ? "View" : "Report", description,
                definitionJson, clock.GetUtcNow().UtcDateTime.Add(Lifetime));
            db.ExportJobs.Add(job);
            await db.SaveChangesAsync(ct);

            jobs.Enqueue<DataExportJob>(j => j.RunAsync(tenantId, job.Id, CancellationToken.None));
            return TypedResults.Accepted($"/api/exports/{job.Id}", ToResponse(job, clock.GetUtcNow().UtcDateTime));
        }
    }

    internal static async Task<IResult> List(ITenantService tenantService, ITenantDbContextFactory factory, IUserContext user,
        TimeProvider clock, CancellationToken ct)
    {
        var (db, _) = await Insights.OpenAsync(tenantService, factory, ct);
        await using (db)
        {
            var jobs = await db.ExportJobs.AsNoTracking().Where(e => e.RequestedByUserId == user.UserId)
                .OrderByDescending(e => e.CreatedAt).Take(50)
                .Select(e => new { e.Id, e.RecordType, e.Source, e.Description, e.Status, e.RowCount, e.WasTruncated, e.Error, e.CreatedAt, e.CompletedAt, e.ExpiresAt })
                .ToListAsync(ct);
            var now = clock.GetUtcNow().UtcDateTime;
            return TypedResults.Ok(jobs.Select(e => new ExportJobResponse(e.Id, e.RecordType.ToString(), e.Source, e.Description,
                e.Status.ToString(), e.RowCount, e.WasTruncated, e.Error, e.CreatedAt, e.CompletedAt, e.ExpiresAt,
                e.Status == ExportStatus.Completed && e.ExpiresAt > now)).ToList());
        }
    }

    internal static async Task<IResult> Download(Guid id, ITenantService tenantService, ITenantDbContextFactory factory,
        IUserContext user, TimeProvider clock, CancellationToken ct)
    {
        var (db, tenantId) = await Insights.OpenAsync(tenantService, factory, ct);
        await using (db)
        {
            // Only the requester: an export was produced under their visibility, and handing it
            // to anyone else would show that person records outside their own scope.
            var job = await db.ExportJobs.SingleOrDefaultAsync(e => e.Id == id && e.RequestedByUserId == user.UserId, ct);
            if (job is null || job.Status != ExportStatus.Completed || job.Content is null) return TypedResults.NotFound();
            if (job.ExpiresAt <= clock.GetUtcNow().UtcDateTime)
            {
                job.PurgeContent();
                await db.SaveChangesAsync(ct);
                return TypedResults.NotFound();
            }

            db.ActivityLogs.Add(ActivityLog.CreateFor(tenantId, "Export", job.Id, user.UserId, "ExportDownloaded", "ExportJob", job.Id.ToString()));
            await db.SaveChangesAsync(ct);
            return TypedResults.File(job.Content, "text/csv; charset=utf-8", job.FileName);
        }
    }

    private static ExportJobResponse ToResponse(ExportJob e, DateTime now) => new(e.Id, e.RecordType.ToString(), e.Source, e.Description,
        e.Status.ToString(), e.RowCount, e.WasTruncated, e.Error, e.CreatedAt, e.CompletedAt, e.ExpiresAt,
        e.Status == ExportStatus.Completed && e.ExpiresAt > now);
}
