using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Features.Commerce;
using IronMonkey.ApiService.Features.RoleManagement;
using IronMonkey.Data;
using IronMonkey.Data.Entities;
using Npgsql;

namespace IronMonkey.ApiService.Features.Insights;

/// <summary>A statement exceeded its time budget and was cancelled by PostgreSQL.</summary>
public sealed class QueryTimeoutException() : Exception("The query took too long and was stopped. Narrow the filter or date range.");

/// <summary>
/// Executes compiled insight statements.
///
/// <para>Every statement runs in its own <b>read-only</b> transaction with a
/// <c>statement_timeout</c>. Read-only is defence in depth: even a compiler bug could not make
/// one of these statements write. The timeout is enforced by PostgreSQL itself, so a runaway
/// query is cancelled server-side rather than merely abandoned by the client while the database
/// keeps working on it.</para>
/// </summary>
public static class InsightSql
{
    public static async Task<List<object?[]>> QueryAsync(TenantDbContext db, CompiledQuery query, int timeoutMs, CancellationToken ct)
    {
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            var connection = (NpgsqlConnection)db.Database.GetDbConnection();
            await using var tx = await connection.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, ct);

            // timeoutMs is a constant chosen in code, never caller input; formatting it as an
            // integer keeps the statement free of any text a caller supplied.
            await using (var setup = new NpgsqlCommand(
                $"SET TRANSACTION READ ONLY; SET LOCAL statement_timeout = {timeoutMs:D}", connection, tx))
                await setup.ExecuteNonQueryAsync(ct);

            await using var command = new NpgsqlCommand(query.Sql, connection, tx) { CommandTimeout = timeoutMs / 1000 + 5 };
            foreach (var p in query.Parameters) command.Parameters.Add(p.Clone());

            var rows = new List<object?[]>();
            try
            {
                await using var reader = await command.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    var row = new object?[reader.FieldCount];
                    for (var i = 0; i < row.Length; i++) row[i] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                    rows.Add(row);
                }
            }
            catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.QueryCanceled)
            {
                throw new QueryTimeoutException();
            }

            await tx.CommitAsync(ct);
            return rows;
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }
}

public sealed record FieldInfo(string Key, string Label, string Kind, bool Sortable, bool Groupable, List<string>? Options, List<string> Operators);

public sealed record ViewColumn(string Key, string Label, string Kind);
public sealed record ViewRow(Guid Id, List<string?> Values);
public sealed record ViewResult(List<ViewColumn> Columns, List<ViewRow> Rows, int TotalCount, int Page, int PageSize, int TotalPages);

public sealed record ReportRow(string? Group, string? GroupLabel, DateTime? Bucket, List<decimal?> Values);
public sealed record ReportResult(string? GroupBy, string? TimeField, string? TimeGranularity, List<string> MeasureLabels,
    List<ReportRow> Rows, bool Truncated, int GroupCap, string TimeZone, DateTime GeneratedAt);

/// <summary>Runs views and reports for a caller, with the caps that keep a tenant's query from
/// taking the database down.</summary>
public sealed class InsightService(ITenantCommerceContextResolver tenantContext, TimeProvider timeProvider)
{
    public const int MaxPageSize = 200;
    public const int ReportGroupCap = 200;
    public const int InteractiveTimeoutMs = 10_000;

    public static readonly Dictionary<FieldKind, List<string>> OperatorsByKind = new()
    {
        [FieldKind.Text] = ["eq", "neq", "contains", "not_contains", "starts_with", "in", "is_empty", "is_not_empty"],
        [FieldKind.Number] = ["eq", "neq", "gt", "gte", "lt", "lte", "between", "is_empty", "is_not_empty"],
        [FieldKind.Instant] = ["in_range", "before", "after", "is_empty", "is_not_empty"],
        [FieldKind.Date] = ["in_range", "before", "after", "is_empty", "is_not_empty"],
        [FieldKind.Bool] = ["is_true", "is_false"],
        [FieldKind.UserRef] = ["eq", "neq", "in", "is_me", "is_empty", "is_not_empty"],
        [FieldKind.MultiSelect] = ["contains", "is_empty", "is_not_empty"]
    };

    public async Task<QueryContext> ContextAsync(TenantDbContext db, Guid tenantId, Guid userId, CancellationToken ct)
    {
        var tenant = await tenantContext.ResolveAsync(tenantId, ct);
        return new QueryContext(tenantId, userId, db.Visibility, tenant.Formatting.TimeZone, timeProvider.GetUtcNow().UtcDateTime);
    }

    /// <summary>Whether the caller may read this record type at all. Visibility then decides which records.</summary>
    public static async Task<bool> CanReadAsync(TenantDbContext db, Guid userId, RecordDef record, CancellationToken ct) =>
        (await PrivilegeGuard.ActorPermissionsAsync(db, userId, ct)).Contains(record.ReadPermission);

    public static List<FieldInfo> Describe(RecordDef record) =>
        record.Fields.Values
            .OrderBy(f => f.IsCustom).ThenBy(f => f.Label, StringComparer.OrdinalIgnoreCase)
            .Select(f => new FieldInfo(f.Key, f.Label, f.Kind.ToString(), f.Sortable, f.Groupable, f.Options, OperatorsByKind[f.Kind]))
            .ToList();

    public async Task<ViewResult> RunViewAsync(TenantDbContext db, RecordDef record, QueryContext context, ViewDefinition view,
        int page, int pageSize, CancellationToken ct, int timeoutMs = InteractiveTimeoutMs)
    {
        var size = Math.Clamp(pageSize, 1, MaxPageSize);

        // Counted with the same WHERE (visibility included) before paging, so "1–25 of N" is
        // honest about what this caller may see — a total is a disclosure too.
        var count = new QueryCompiler(record, context).CompileCount(view.Filter);
        var total = Convert.ToInt32((await InsightSql.QueryAsync(db, count, timeoutMs, ct))[0][0]);

        var totalPages = total == 0 ? 0 : (int)Math.Ceiling(total / (double)size);
        var current = totalPages == 0 ? 1 : Math.Clamp(page, 1, totalPages);

        var pageQuery = new QueryCompiler(record, context).CompilePage(view, size, (current - 1) * size);
        var raw = await InsightSql.QueryAsync(db, pageQuery, timeoutMs, ct);

        var columns = view.Columns.Select(k => record.Fields[k]).ToList();
        var rows = raw.Select(r => new ViewRow((Guid)r[0]!, r.Skip(1).Select(v => v?.ToString()).ToList())).ToList();
        await LabelUsersAsync(db, columns, rows, ct);

        return new ViewResult(columns.Select(c => new ViewColumn(c.Key, c.Label, c.Kind.ToString())).ToList(),
            rows, total, current, size, totalPages);
    }

    public async Task<ReportResult> RunReportAsync(TenantDbContext db, RecordDef record, QueryContext context, ReportSpec spec,
        CancellationToken ct, int timeoutMs = InteractiveTimeoutMs)
    {
        var query = new QueryCompiler(record, context).CompileReport(spec, ReportGroupCap);
        var raw = await InsightSql.QueryAsync(db, query, timeoutMs, ct);

        var truncated = raw.Count > ReportGroupCap;
        var rows = raw.Take(ReportGroupCap).Select(r => new ReportRow(
            r[0]?.ToString(), r[0]?.ToString(), r[1] as DateTime?,
            r.Skip(2).Select(v => v is null ? (decimal?)null : Convert.ToDecimal(v)).ToList())).ToList();

        // A UserRef grouping is stored as an id; show the person's name.
        if (spec.GroupBy is { } g && record.Fields.TryGetValue(g, out var gf) && gf.Kind == FieldKind.UserRef)
        {
            var names = await UserNamesAsync(db, rows.Select(r => r.Group), ct);
            rows = rows.Select(r => r with { GroupLabel = r.Group is { } id && names.TryGetValue(id, out var n) ? n : r.Group ?? "Unassigned" }).ToList();
        }

        var labels = spec.Measures.Select(m => m.Fn.ToLowerInvariant() == "count"
            ? "Count"
            : $"{char.ToUpperInvariant(m.Fn[0])}{m.Fn[1..].ToLowerInvariant()} of {(m.Field is not null && record.Fields.TryGetValue(m.Field, out var f) ? f.Label : m.Field)}").ToList();

        return new ReportResult(spec.GroupBy, spec.TimeField, spec.TimeField is null ? null : (spec.TimeGranularity ?? "month"),
            labels, rows, truncated, ReportGroupCap, context.Zone.Id, timeProvider.GetUtcNow().UtcDateTime);
    }

    private static async Task LabelUsersAsync(TenantDbContext db, List<FieldDef> columns, List<ViewRow> rows, CancellationToken ct)
    {
        var userColumns = columns.Select((c, i) => (c, i)).Where(x => x.c.Kind == FieldKind.UserRef).Select(x => x.i).ToList();
        if (userColumns.Count == 0) return;

        var names = await UserNamesAsync(db, rows.SelectMany(r => userColumns.Select(i => r.Values[i])), ct);
        foreach (var row in rows)
            foreach (var i in userColumns)
                if (row.Values[i] is { } id && names.TryGetValue(id, out var name)) row.Values[i] = name;
    }

    private static async Task<Dictionary<string, string>> UserNamesAsync(TenantDbContext db, IEnumerable<string?> ids, CancellationToken ct)
    {
        var guids = ids.Where(i => Guid.TryParse(i, out _)).Select(Guid.Parse!).Distinct().ToList();
        if (guids.Count == 0) return [];
        return await db.Users.IgnoreQueryFilters().AsNoTracking()
            .Where(u => u.TenantId == db.TenantId && guids.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id.ToString(), u => u.Name, ct);
    }
}
