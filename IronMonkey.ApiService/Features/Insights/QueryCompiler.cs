using System.Text;
using System.Text.Json;
using IronMonkey.ApiService.Features.Reports.Dashboard;
using IronMonkey.Data.Entities;
using IronMonkey.Data.Visibility;
using Npgsql;
using NpgsqlTypes;

namespace IronMonkey.ApiService.Features.Insights;

// ── Wire shapes ─────────────────────────────────────────────────────────────────────

/// <summary>A filter tree: conditions and nested groups joined by one explicit logic.</summary>
public sealed class FilterGroup
{
    /// <summary>"and" or "or". Explicit on every group, so precedence is never implied.</summary>
    public string Logic { get; set; } = "and";
    public List<FilterCondition> Conditions { get; set; } = [];
    public List<FilterGroup> Groups { get; set; } = [];
}

/// <param name="Value">Shape depends on the operator: a scalar, an array (in), a
/// <c>{min,max}</c> object (between) or a <c>{preset,from,to}</c> object (in_range).</param>
public sealed class FilterCondition
{
    public string Field { get; set; } = string.Empty;
    public string Op { get; set; } = string.Empty;
    public JsonElement? Value { get; set; }
}

public sealed class SortSpec
{
    public string Field { get; set; } = string.Empty;
    public bool Descending { get; set; }
}

/// <summary>What a saved view stores and what the list query takes.</summary>
public sealed class ViewDefinition
{
    public FilterGroup Filter { get; set; } = new();
    public List<string> Columns { get; set; } = [];
    public SortSpec? Sort { get; set; }
}

public sealed class MeasureSpec
{
    /// <summary>count, sum, avg, min or max.</summary>
    public string Fn { get; set; } = "count";
    public string? Field { get; set; }
}

public sealed class DateRangeSpec
{
    public string Field { get; set; } = "createdAt";
    public string? Preset { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
}

/// <summary>A report: filter, optional grouping, optional time dimension, measures.</summary>
public sealed class ReportSpec
{
    public FilterGroup Filter { get; set; } = new();
    public string? GroupBy { get; set; }
    public string? TimeField { get; set; }

    /// <summary>day, week, month, quarter or year.</summary>
    public string? TimeGranularity { get; set; }
    public DateRangeSpec? DateRange { get; set; }
    public List<MeasureSpec> Measures { get; set; } = [new()];
}

/// <summary>A definition the compiler refused, with every reason — a broken filter is
/// reported as broken, never run as an empty result.</summary>
public sealed class QueryValidationException(IReadOnlyList<string> problems) : Exception(string.Join(" ", problems))
{
    public IReadOnlyList<string> Problems { get; } = problems;
}

/// <summary>A compiled statement: SQL text that contains no tenant-supplied text, plus parameters.</summary>
public sealed record CompiledQuery(string Sql, IReadOnlyList<NpgsqlParameter> Parameters);

/// <summary>Context a compilation needs: who, where, and in which timezone.</summary>
public sealed record QueryContext(Guid TenantId, Guid UserId, RecordVisibility Visibility, TimeZoneInfo Zone, DateTime NowUtc);

/// <summary>
/// Compiles view and report definitions into parameterised SQL.
///
/// <para><b>Injection.</b> SQL text is assembled only from constants: field expressions from
/// <see cref="RecordCatalog"/>, operators from a switch below, sort direction from a bool.
/// Every tenant-supplied value — filter values, custom field keys, timezone, granularity — is
/// an <see cref="NpgsqlParameter"/>. Unknown fields and operators are rejected before SQL
/// exists.</para>
///
/// <para><b>Isolation.</b> Raw SQL bypasses EF's global filters, so every statement starts from
/// an explicit <c>TenantId</c> and soft-delete predicate and the caller's record visibility as
/// owner-set predicates — the same rules as the query filters, restated here once.</para>
///
/// <para><b>Cost.</b> Filter depth, condition count, list sizes and string lengths are capped;
/// row and group limits and statement timeouts are applied by the callers.</para>
/// </summary>
public sealed class QueryCompiler(RecordDef record, QueryContext context)
{
    public const int MaxDepth = 3;
    public const int MaxConditions = 30;
    public const int MaxListValues = 100;
    public const int MaxStringLength = 500;
    public const int MaxColumns = 20;

    private static readonly string[] Granularities = ["day", "week", "month", "quarter", "year"];

    private readonly List<NpgsqlParameter> _parameters = [];
    private readonly List<string> _problems = [];
    private int _conditionCount;

    public IReadOnlyList<NpgsqlParameter> Parameters => _parameters;

    // ── Entry points ────────────────────────────────────────────────────────────────

    /// <summary>COUNT(*) of the records a filter matches, under the caller's visibility.</summary>
    public CompiledQuery CompileCount(FilterGroup filter)
    {
        var where = Where(filter);
        Throw();
        return new CompiledQuery($"SELECT count(*) FROM {record.From} WHERE {where}", _parameters);
    }

    /// <summary>A page of rows: Id plus each requested column rendered as text.</summary>
    public CompiledQuery CompilePage(ViewDefinition view, int limit, int offset)
    {
        var where = Where(view.Filter);
        var columns = ColumnList(view.Columns);
        var order = OrderBy(view.Sort);
        Throw();

        var select = string.Join(", ", columns.Select((c, i) => $"{RenderText(c)} AS c{i}"));
        var sql = $"SELECT t.\"Id\"{(select.Length > 0 ? ", " + select : "")} FROM {record.From} WHERE {where} " +
                  $"ORDER BY {order} LIMIT {Param(limit, NpgsqlDbType.Integer)} OFFSET {Param(offset, NpgsqlDbType.Integer)}";
        return new CompiledQuery(sql, _parameters);
    }

    /// <summary>Grouped aggregates. Returns at most <paramref name="groupCap"/> + 1 rows so the
    /// caller can tell a capped result from a complete one.</summary>
    public CompiledQuery CompileReport(ReportSpec spec, int groupCap)
    {
        var filter = spec.Filter;
        var where = Where(filter);

        if (spec.DateRange is { } range)
            where += " AND " + RangePredicate(range);

        string groupSql = "NULL::text";
        if (!string.IsNullOrWhiteSpace(spec.GroupBy))
        {
            var g = Field(spec.GroupBy, "group by");
            if (g is not null)
            {
                if (!g.Groupable) _problems.Add($"'{g.Label}' cannot be grouped on.");
                groupSql = RenderText(g);
            }
        }

        string bucketSql = "NULL::timestamp";
        if (!string.IsNullOrWhiteSpace(spec.TimeField))
        {
            var tf = Field(spec.TimeField, "time dimension");
            var granularity = spec.TimeGranularity?.ToLowerInvariant() ?? "month";
            if (!Granularities.Contains(granularity))
                _problems.Add($"Time granularity must be one of: {string.Join(", ", Granularities)}.");
            else if (tf is not null)
            {
                var gran = Param(granularity, NpgsqlDbType.Text);
                bucketSql = tf.Kind switch
                {
                    // Bucketed on the tenant's wall clock: "October" is the tenant's October.
                    FieldKind.Instant => $"date_trunc({gran}, ({Expr(tf)}) AT TIME ZONE {Param(context.Zone.Id, NpgsqlDbType.Text)})",
                    FieldKind.Date => $"date_trunc({gran}, ({DateExpr(tf)})::timestamp)",
                    _ => Invalid<string>($"'{tf.Label}' is not a date field.", "NULL::timestamp")
                };
            }
        }

        if (spec.Measures.Count is 0 or > 6) _problems.Add("A report needs between one and six measures.");
        var measures = spec.Measures.Take(6).Select(Measure).ToList();

        Throw();

        var measureSql = string.Join(", ", measures.Select((m, i) => $"{m} AS m{i}"));
        var sql = $"SELECT {groupSql} AS g, {bucketSql} AS b, {measureSql} FROM {record.From} WHERE {where} " +
                  $"GROUP BY 1, 2 ORDER BY 2 NULLS LAST, 1 NULLS LAST LIMIT {Param(groupCap + 1, NpgsqlDbType.Integer)}";
        return new CompiledQuery(sql, _parameters);
    }

    /// <summary>Validates without compiling to a statement (used when saving a definition).</summary>
    public IReadOnlyList<string> Validate(ViewDefinition view)
    {
        Where(view.Filter);
        ColumnList(view.Columns);
        OrderBy(view.Sort);
        return _problems;
    }

    // ── WHERE ───────────────────────────────────────────────────────────────────────

    private string Where(FilterGroup filter)
    {
        var tenant = Param(context.TenantId, NpgsqlDbType.Uuid);
        var parts = new List<string> { $"t.\"TenantId\" = {tenant}", "t.\"IsDeleted\" = false" };
        parts.AddRange(VisibilityPredicates(tenant));

        var compiled = Group(filter, 1);
        if (compiled is not null) parts.Add(compiled);

        return string.Join(" AND ", parts);
    }

    /// <summary>
    /// The caller's record visibility, restated for raw SQL. Must match the TenantDbContext
    /// query filters: owner in the visible set (unowned is visible only when unrestricted), and
    /// for tasks also the parent lead's visibility.
    /// </summary>
    private IEnumerable<string> VisibilityPredicates(string tenantParam)
    {
        var v = context.Visibility;
        string Owners(VisibilityRecordType t) => Param(v.OwnersFor(t), NpgsqlDbType.Array | NpgsqlDbType.Uuid);

        switch (record.Type)
        {
            case InsightRecordType.Lead when !v.IsUnrestricted(VisibilityRecordType.Lead):
                yield return $"t.\"AssignedToUserId\" = ANY({Owners(VisibilityRecordType.Lead)})";
                break;
            case InsightRecordType.Contact when !v.IsUnrestricted(VisibilityRecordType.Contact):
                yield return $"t.\"OwnerUserId\" = ANY({Owners(VisibilityRecordType.Contact)})";
                break;
            case InsightRecordType.Opportunity when !v.IsUnrestricted(VisibilityRecordType.Opportunity):
                yield return $"t.\"OwnerUserId\" = ANY({Owners(VisibilityRecordType.Opportunity)})";
                break;
            case InsightRecordType.Task:
                yield return $"l.\"TenantId\" = {tenantParam} AND l.\"IsDeleted\" = false";
                if (!v.IsUnrestricted(VisibilityRecordType.Task))
                    yield return $"t.\"AssignedToUserId\" = ANY({Owners(VisibilityRecordType.Task)})";
                if (!v.IsUnrestricted(VisibilityRecordType.Lead))
                    yield return $"l.\"AssignedToUserId\" = ANY({Owners(VisibilityRecordType.Lead)})";
                break;
        }
    }

    private string? Group(FilterGroup group, int depth)
    {
        if (depth > MaxDepth)
        {
            _problems.Add($"Filters may nest at most {MaxDepth} levels deep.");
            return null;
        }

        var logic = group.Logic?.Trim().ToLowerInvariant();
        if (logic is not ("and" or "or"))
        {
            _problems.Add("Each filter group must say \"and\" or \"or\".");
            return null;
        }

        var parts = group.Conditions.Select(Condition)
            .Concat(group.Groups.Select(g => Group(g, depth + 1)))
            .Where(p => p is not null)
            .ToList();

        if (parts.Count == 0) return null;
        return "(" + string.Join(logic == "and" ? " AND " : " OR ", parts) + ")";
    }

    private string? Condition(FilterCondition c)
    {
        if (++_conditionCount > MaxConditions)
        {
            if (_conditionCount == MaxConditions + 1) _problems.Add($"A filter may have at most {MaxConditions} conditions.");
            return null;
        }

        var f = Field(c.Field, "filter");
        if (f is null) return null;

        var op = c.Op?.Trim().ToLowerInvariant() ?? "";
        var e = Expr(f);

        switch (f.Kind)
        {
            case FieldKind.Text:
                return op switch
                {
                    "eq" => $"{e} = {Str(c)}",
                    "neq" => $"({e} IS DISTINCT FROM {Str(c)})",
                    "contains" => $"{e} ILIKE {Like(c, "%", "%")}",
                    "not_contains" => $"({e} IS NULL OR {e} NOT ILIKE {Like(c, "%", "%")})",
                    "starts_with" => $"{e} ILIKE {Like(c, "", "%")}",
                    "in" => $"{e} = ANY({StrList(c)})",
                    "is_empty" => $"({e} IS NULL OR {e} = '')",
                    "is_not_empty" => $"({e} IS NOT NULL AND {e} <> '')",
                    _ => BadOp(f, op)
                };

            case FieldKind.Number:
                var n = NumExpr(f);
                return op switch
                {
                    "eq" => $"{n} = {Num(c)}",
                    "neq" => $"({n} IS DISTINCT FROM {Num(c)})",
                    "gt" => $"{n} > {Num(c)}",
                    "gte" => $"{n} >= {Num(c)}",
                    "lt" => $"{n} < {Num(c)}",
                    "lte" => $"{n} <= {Num(c)}",
                    "between" => Between(c, n),
                    "is_empty" => $"{n} IS NULL",
                    "is_not_empty" => $"{n} IS NOT NULL",
                    _ => BadOp(f, op)
                };

            case FieldKind.Instant:
                return op switch
                {
                    "in_range" => InstantRange(e, c.Value),
                    "before" => $"{e} < {Instant(c)}",
                    "after" => $"{e} >= {Instant(c)}",
                    "is_empty" => $"{e} IS NULL",
                    "is_not_empty" => $"{e} IS NOT NULL",
                    _ => BadOp(f, op)
                };

            case FieldKind.Date:
                var d = DateExpr(f);
                return op switch
                {
                    "in_range" => DateRange(d, c.Value),
                    "before" => $"{d} < {LocalDate(c)}",
                    "after" => $"{d} >= {LocalDate(c)}",
                    "is_empty" => $"{d} IS NULL",
                    "is_not_empty" => $"{d} IS NOT NULL",
                    _ => BadOp(f, op)
                };

            case FieldKind.Bool:
                var b = BoolExpr(f);
                return op switch
                {
                    "is_true" => $"{b} = true",
                    "is_false" => $"({b} = false OR {b} IS NULL)",
                    _ => BadOp(f, op)
                };

            case FieldKind.UserRef:
                return op switch
                {
                    "eq" => $"{e} = {Guid(c)}",
                    "neq" => $"({e} IS DISTINCT FROM {Guid(c)})",
                    "in" => $"{e} = ANY({GuidList(c)})",
                    "is_me" => $"{e} = {Param(context.UserId, NpgsqlDbType.Uuid)}",
                    "is_empty" => $"{e} IS NULL",
                    "is_not_empty" => $"{e} IS NOT NULL",
                    _ => BadOp(f, op)
                };

            case FieldKind.MultiSelect:
                var path = JsonPath(f);
                return op switch
                {
                    // jsonb_exists() rather than ?, which Npgsql reads as a parameter placeholder.
                    "contains" => $"jsonb_exists({path}, {Str(c)})",
                    "is_empty" => $"({path} IS NULL OR {path} = '[]'::jsonb)",
                    "is_not_empty" => $"({path} IS NOT NULL AND {path} <> '[]'::jsonb)",
                    _ => BadOp(f, op)
                };
        }

        return BadOp(f, op);
    }

    // ── Columns, sort, measures, ranges ─────────────────────────────────────────────

    private List<FieldDef> ColumnList(List<string> keys)
    {
        if (keys.Count > MaxColumns) _problems.Add($"A view may show at most {MaxColumns} columns.");
        return keys.Take(MaxColumns).Select(k => Field(k, "column")).Where(f => f is not null).Select(f => f!).ToList();
    }

    private string OrderBy(SortSpec? sort)
    {
        // The Id tiebreak is unconditional: rows sharing a sort value otherwise have no defined
        // order between pages, so a row can appear twice or never.
        if (sort is null || string.IsNullOrWhiteSpace(sort.Field)) return "t.\"CreatedAt\" DESC, t.\"Id\" DESC";

        var f = Field(sort.Field, "sort");
        if (f is null) return "t.\"Id\"";
        if (!f.Sortable) { _problems.Add($"'{f.Label}' cannot be sorted on."); return "t.\"Id\""; }

        var expr = f.Kind switch
        {
            FieldKind.Number => NumExpr(f),
            FieldKind.Date => DateExpr(f),
            FieldKind.Bool => BoolExpr(f),
            _ => Expr(f)
        };
        var dir = sort.Descending ? "DESC" : "ASC";
        return $"{expr} {dir} NULLS LAST, t.\"Id\" {dir}";
    }

    private string Measure(MeasureSpec m)
    {
        var fn = m.Fn?.Trim().ToLowerInvariant();
        if (fn == "count") return "count(*)::numeric";
        if (fn is not ("sum" or "avg" or "min" or "max"))
            return Invalid<string>($"'{m.Fn}' is not a measure. Use count, sum, avg, min or max.", "NULL::numeric");

        var f = m.Field is null ? Invalid<FieldDef?>($"{fn} needs a numeric field.", null) : Field(m.Field, "measure");
        if (f is null) return "NULL::numeric";
        if (f.Kind != FieldKind.Number) return Invalid<string>($"'{f.Label}' is not numeric, so it cannot be {fn}med.", "NULL::numeric");
        return $"{fn}({NumExpr(f)})::numeric";
    }

    private string RangePredicate(DateRangeSpec range)
    {
        var f = Field(range.Field, "date range");
        if (f is null) return "true";
        var value = JsonSerializer.SerializeToElement(new { preset = range.Preset, from = range.From, to = range.To });
        return f.Kind switch
        {
            FieldKind.Instant => InstantRange(Expr(f), value),
            FieldKind.Date => DateRange(DateExpr(f), value),
            _ => Invalid<string>($"'{f.Label}' is not a date field.", "true")
        };
    }

    /// <summary>
    /// A date range through <see cref="DashboardDateRange.ResolveIn"/> — the one place range
    /// arithmetic lives — evaluated in the tenant's zone. Half-open: <c>&gt;= From AND &lt;
    /// ToExclusive</c>; never a closed end-of-day bound.
    /// </summary>
    private string InstantRange(string expr, JsonElement? value)
    {
        var range = ResolveRange(value);
        return $"({expr} >= {Param(range.From, NpgsqlDbType.TimestampTz)} AND {expr} < {Param(range.ToExclusive, NpgsqlDbType.TimestampTz)})";
    }

    private string DateRange(string expr, JsonElement? value)
    {
        var range = ResolveRange(value);
        var fromLocal = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(range.From == DateTime.MinValue ? DateTime.MinValue.AddDays(2) : range.From, context.Zone));
        var toLocal = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(range.ToExclusive, context.Zone));
        return $"({expr} >= {Param(fromLocal, NpgsqlDbType.Date)} AND {expr} < {Param(toLocal, NpgsqlDbType.Date)})";
    }

    private DashboardDateRange ResolveRange(JsonElement? value)
    {
        string? preset = null;
        DateTime? from = null, to = null;
        if (value is { ValueKind: JsonValueKind.Object } v)
        {
            if (v.TryGetProperty("preset", out var p) && p.ValueKind == JsonValueKind.String) preset = p.GetString();
            if (v.TryGetProperty("from", out var fr) && fr.ValueKind == JsonValueKind.String && fr.TryGetDateTime(out var fd)) from = fd;
            if (v.TryGetProperty("to", out var tt) && tt.ValueKind == JsonValueKind.String && tt.TryGetDateTime(out var td)) to = td;
        }
        return DashboardDateRange.ResolveIn(preset, from, to, context.Zone, context.NowUtc);
    }

    // ── Field expressions ───────────────────────────────────────────────────────────

    private FieldDef? Field(string? key, string usage)
    {
        if (key is not null && record.Fields.TryGetValue(key, out var f)) return f;

        // The key is echoed only after length-capping; it is never placed in SQL.
        var shown = key is null ? "(none)" : key.Length > 60 ? key[..60] + "…" : key;
        _problems.Add(key is not null && key.StartsWith(RecordCatalog.CustomPrefix, StringComparison.Ordinal)
            ? $"The custom field used for {usage} no longer exists."
            : $"'{shown}' is not a {record.Type} field that can be used for {usage}.");
        return null;
    }

    /// <summary>A custom field's jsonb text value, its key bound as a parameter.</summary>
    private string Expr(FieldDef f) =>
        f.IsCustom ? $"(t.custom_field_values->'Values'->>{Param(f.ParameterValue!, NpgsqlDbType.Text)})" : f.Sql;

    private string JsonPath(FieldDef f) =>
        $"(t.custom_field_values->'Values'->{Param(f.ParameterValue!, NpgsqlDbType.Text)})";

    /// <summary>Numeric value, or NULL for text that is not a number — a malformed stored
    /// value must not make the whole query throw.</summary>
    private string NumExpr(FieldDef f)
    {
        if (!f.IsCustom) return f.Sql;
        var e = Expr(f);
        return $"(CASE WHEN {e} ~ '^-?[0-9]+(\\.[0-9]+)?$' THEN ({e})::numeric END)";
    }

    private string DateExpr(FieldDef f)
    {
        var e = Expr(f);
        return $"(CASE WHEN {e} ~ '^[0-9]{{4}}-[0-9]{{2}}-[0-9]{{2}}' THEN left({e}, 10)::date END)";
    }

    private string BoolExpr(FieldDef f) => f.IsCustom ? $"(lower({Expr(f)}) = 'true')" : f.Sql;

    /// <summary>A column rendered as text for a row grid.</summary>
    private string RenderText(FieldDef f) => f.Kind switch
    {
        FieldKind.Instant => $"to_char(({Expr(f)}) AT TIME ZONE {Param(context.Zone.Id, NpgsqlDbType.Text)}, 'YYYY-MM-DD\"T\"HH24:MI:SS')",
        FieldKind.MultiSelect => $"({JsonPath(f)})::text",
        _ => $"({Expr(f)})::text"
    };

    // ── Values (always parameters) ──────────────────────────────────────────────────

    private string Param(object value, NpgsqlDbType type)
    {
        var name = "@p" + _parameters.Count;
        _parameters.Add(new NpgsqlParameter(name, type) { Value = value });
        return name;
    }

    private string Str(FilterCondition c)
    {
        var s = c.Value is { ValueKind: JsonValueKind.String } v ? v.GetString() ?? ""
              : c.Value is { ValueKind: JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False } o ? o.ToString()
              : Invalid<string>($"'{c.Field}' needs a text value.", "");
        if (s.Length > MaxStringLength) { _problems.Add($"Filter values are limited to {MaxStringLength} characters."); s = s[..MaxStringLength]; }
        return Param(s, NpgsqlDbType.Text);
    }

    /// <summary>ILIKE pattern with the user's own % and _ escaped, so they match literally.</summary>
    private string Like(FilterCondition c, string prefix, string suffix)
    {
        var raw = c.Value is { ValueKind: JsonValueKind.String } v ? v.GetString() ?? "" : Invalid<string>($"'{c.Field}' needs a text value.", "");
        if (raw.Length > MaxStringLength) { _problems.Add($"Filter values are limited to {MaxStringLength} characters."); raw = raw[..MaxStringLength]; }
        return Param(prefix + EscapeLike(raw) + suffix, NpgsqlDbType.Text);
    }

    public static string EscapeLike(string s) => s.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    private string StrList(FilterCondition c)
    {
        var items = Array(c).Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() ?? "" : e.ToString()).ToArray();
        return Param(items, NpgsqlDbType.Array | NpgsqlDbType.Text);
    }

    private string GuidList(FilterCondition c)
    {
        var items = Array(c).Select(e => e.ValueKind == JsonValueKind.String && System.Guid.TryParse(e.GetString(), out var g)
            ? g : Invalid($"'{c.Field}' values must be user ids.", System.Guid.Empty)).ToArray();
        return Param(items, NpgsqlDbType.Array | NpgsqlDbType.Uuid);
    }

    private List<JsonElement> Array(FilterCondition c)
    {
        if (c.Value is not { ValueKind: JsonValueKind.Array } v) return Invalid($"'{c.Field}' needs a list of values.", new List<JsonElement>());
        var items = v.EnumerateArray().ToList();
        if (items.Count == 0) _problems.Add($"'{c.Field}' needs at least one value.");
        if (items.Count > MaxListValues) { _problems.Add($"A list may hold at most {MaxListValues} values."); items = items.Take(MaxListValues).ToList(); }
        return items;
    }

    private string Num(FilterCondition c) =>
        Param(c.Value is { ValueKind: JsonValueKind.Number } v && v.TryGetDecimal(out var d)
            ? d : Invalid($"'{c.Field}' needs a number.", 0m), NpgsqlDbType.Numeric);

    private string Between(FilterCondition c, string expr)
    {
        if (c.Value is not { ValueKind: JsonValueKind.Object } v
            || !v.TryGetProperty("min", out var min) || !min.TryGetDecimal(out var lo)
            || !v.TryGetProperty("max", out var max) || !max.TryGetDecimal(out var hi))
            return Invalid<string>($"'{c.Field}' needs {{min, max}} numbers.", "false");
        return $"({expr} >= {Param(lo, NpgsqlDbType.Numeric)} AND {expr} <= {Param(hi, NpgsqlDbType.Numeric)})";
    }

    private string Instant(FilterCondition c) =>
        Param(c.Value is { ValueKind: JsonValueKind.String } v && v.TryGetDateTime(out var d)
            ? DateTime.SpecifyKind(d, d.Kind == DateTimeKind.Unspecified ? DateTimeKind.Utc : d.Kind).ToUniversalTime()
            : Invalid($"'{c.Field}' needs a date.", DateTime.UnixEpoch), NpgsqlDbType.TimestampTz);

    private string LocalDate(FilterCondition c) =>
        Param(c.Value is { ValueKind: JsonValueKind.String } v && DateOnly.TryParse(v.GetString()?[..Math.Min(10, v.GetString()!.Length)], out var d)
            ? d : Invalid($"'{c.Field}' needs a date.", DateOnly.MinValue), NpgsqlDbType.Date);

    private string Guid(FilterCondition c) =>
        Param(c.Value is { ValueKind: JsonValueKind.String } v && System.Guid.TryParse(v.GetString(), out var g)
            ? g : Invalid($"'{c.Field}' needs a user id.", System.Guid.Empty), NpgsqlDbType.Uuid);

    private string? BadOp(FieldDef f, string op)
    {
        var shown = op.Length > 30 ? op[..30] + "…" : op;
        _problems.Add($"'{shown}' is not an operator for '{f.Label}'.");
        return null;
    }

    private T Invalid<T>(string problem, T fallback)
    {
        _problems.Add(problem);
        return fallback;
    }

    private void Throw()
    {
        if (_problems.Count > 0) throw new QueryValidationException(_problems.Distinct().ToList());
    }
}
