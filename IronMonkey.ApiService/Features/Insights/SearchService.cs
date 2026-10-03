using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using IronMonkey.Data;
using IronMonkey.Data.Entities;
using IronMonkey.Data.Visibility;
using Npgsql;
using NpgsqlTypes;

namespace IronMonkey.ApiService.Features.Insights;

public sealed record SearchHit(Guid Id, string Type, string Title, string? Subtitle, string? Matched, double Score);

/// <param name="Count">Matches the caller may see, capped at <see cref="SearchService.CountCap"/>.
/// <paramref name="CountIsCapped"/> means "at least this many".</param>
public sealed record SearchGroup(string Type, List<SearchHit> Hits, int Count, bool CountIsCapped, int Page, bool HasMore);

public sealed record SearchResult(string Query, List<SearchGroup> Groups, bool TimedOut);

/// <summary>
/// Normalisation shared by every search path, kept pure so it is unit-tested.
/// </summary>
public static partial class SearchNormalizer
{
    /// <summary>Fewer digits than this is not treated as a phone number search.</summary>
    public const int MinPhoneDigits = 6;

    /// <summary>
    /// The national-significant tail of a phone number, for contains-matching against stored
    /// digits. "07700 900123", "+44 7700 900123" and "(0)7700-900123" all reduce to
    /// "7700900123", which is contained in every stored form of that number — "+447700900123"
    /// (digits 447700900123) and "07700900123" alike. Leading zeros are trunk prefixes and are
    /// dropped; beyond ten digits the excess is a country code and is dropped too.
    /// Returns null when the input is not phone-like.
    /// </summary>
    public static string? PhoneTail(string input)
    {
        if (input.Any(char.IsLetter)) return null;
        var digits = NonDigits().Replace(input, "");
        if (digits.Length < MinPhoneDigits) return null;

        digits = digits.TrimStart('0');
        if (digits.Length > 10) digits = digits[^10..];
        return digits.Length < MinPhoneDigits ? null : digits;
    }

    /// <summary>Emails match case-insensitively and without surrounding whitespace.</summary>
    public static string NormalizeEmail(string input) => input.Trim().ToLowerInvariant();

    public static bool LooksLikeEmail(string input) => input.Contains('@');

    [GeneratedRegex("[^0-9]")]
    private static partial Regex NonDigits();
}

/// <summary>
/// Cross-entity search over leads, contacts, opportunities and tasks, in PostgreSQL.
///
/// <list type="bullet">
/// <item><b>Indexed.</b> Every predicate is an ILIKE over a trigram-indexed column (or the
///   trigram-indexed jsonb text, as a prefilter for custom fields), so a large tenant is not
///   sequentially scanned.</item>
/// <item><b>Bounded.</b> Per-type result caps, paging, counts capped at
///   <see cref="CountCap"/>, and a server-side statement timeout.</item>
/// <item><b>Visible-only.</b> The same owner predicates as the query filters are applied in the
///   SQL, so neither results, counts nor ranking reflect a record the caller cannot open.</item>
/// <item><b>Ranked.</b> Exact email/phone first, then exact name, then name prefix, then
///   trigram similarity, with Id as the final tiebreak.</item>
/// </list>
/// </summary>
public sealed class SearchService
{
    public const int CountCap = 100;
    public const int DefaultPerType = 5;
    public const int MaxPerType = 25;
    public const int MinQueryLength = 2;
    public const int MaxQueryLength = 100;
    public const int TimeoutMs = 3_000;

    public async Task<SearchResult> SearchAsync(TenantDbContext db, Guid tenantId, string query,
        IReadOnlyCollection<InsightRecordType> types, int perType, int page, CancellationToken ct)
    {
        var q = query.Trim();
        if (q.Length > MaxQueryLength) q = q[..MaxQueryLength];

        var size = Math.Clamp(perType, 1, MaxPerType);
        var current = Math.Max(1, page);
        var groups = new List<SearchGroup>();
        var timedOut = false;

        foreach (var type in types)
        {
            var (from, select, match, rank) = await ShapeAsync(db, type, ct);
            var p = new Params();
            var where = Base(type, db, tenantId, p) + " AND (" + match(p, q) + ")";

            try
            {
                var hits = await InsightSql.QueryAsync(db, new CompiledQuery(
                    $"SELECT {select} , {rank(p, q)} AS score FROM {from} WHERE {where} " +
                    $"ORDER BY score DESC, t.\"Id\" LIMIT {p.Add(size + 1, NpgsqlDbType.Integer)} OFFSET {p.Add((current - 1) * size, NpgsqlDbType.Integer)}",
                    p.List), TimeoutMs, ct);

                // Separate parameter set: the count reuses the WHERE text, so it must bind the
                // same names in the same order.
                var pc = new Params();
                var whereCount = Base(type, db, tenantId, pc) + " AND (" + match(pc, q) + ")";
                var counted = await InsightSql.QueryAsync(db, new CompiledQuery(
                    $"SELECT count(*) FROM (SELECT 1 FROM {from} WHERE {whereCount} LIMIT {pc.Add(CountCap + 1, NpgsqlDbType.Integer)}) capped",
                    pc.List), TimeoutMs, ct);
                var count = Convert.ToInt32(counted[0][0]);

                groups.Add(new SearchGroup(type.ToString(),
                    hits.Take(size).Select(r => new SearchHit((Guid)r[0]!, type.ToString(), r[1]?.ToString() ?? "", r[2]?.ToString(),
                        r[3]?.ToString(), Convert.ToDouble(r[4]))).ToList(),
                    Math.Min(count, CountCap), count > CountCap, current, hits.Count > size));
            }
            catch (QueryTimeoutException)
            {
                // One slow type degrades alone; the others still answer.
                timedOut = true;
                groups.Add(new SearchGroup(type.ToString(), [], 0, false, current, false));
            }
        }

        return new SearchResult(q, groups, timedOut);
    }

    private sealed class Params
    {
        public List<NpgsqlParameter> List { get; } = [];

        public string Add(object value, NpgsqlDbType type)
        {
            var name = "@s" + List.Count;
            List.Add(new NpgsqlParameter(name, type) { Value = value });
            return name;
        }
    }

    /// <summary>Tenant, soft-delete and record visibility — the query filters, restated for raw SQL.</summary>
    private static string Base(InsightRecordType type, TenantDbContext db, Guid tenantId, Params p)
    {
        var v = db.Visibility;
        var tenant = p.Add(tenantId, NpgsqlDbType.Uuid);
        var parts = new List<string> { $"t.\"TenantId\" = {tenant}", "t.\"IsDeleted\" = false" };
        string Owners(VisibilityRecordType t) => p.Add(v.OwnersFor(t), NpgsqlDbType.Array | NpgsqlDbType.Uuid);

        switch (type)
        {
            case InsightRecordType.Lead when !v.IsUnrestricted(VisibilityRecordType.Lead):
                parts.Add($"t.\"AssignedToUserId\" = ANY({Owners(VisibilityRecordType.Lead)})"); break;
            case InsightRecordType.Contact when !v.IsUnrestricted(VisibilityRecordType.Contact):
                parts.Add($"t.\"OwnerUserId\" = ANY({Owners(VisibilityRecordType.Contact)})"); break;
            case InsightRecordType.Opportunity when !v.IsUnrestricted(VisibilityRecordType.Opportunity):
                parts.Add($"t.\"OwnerUserId\" = ANY({Owners(VisibilityRecordType.Opportunity)})"); break;
            case InsightRecordType.Task:
                parts.Add($"l.\"TenantId\" = {tenant} AND l.\"IsDeleted\" = false");
                if (!v.IsUnrestricted(VisibilityRecordType.Task)) parts.Add($"t.\"AssignedToUserId\" = ANY({Owners(VisibilityRecordType.Task)})");
                if (!v.IsUnrestricted(VisibilityRecordType.Lead)) parts.Add($"l.\"AssignedToUserId\" = ANY({Owners(VisibilityRecordType.Lead)})");
                break;
        }
        return string.Join(" AND ", parts);
    }

    private delegate string Builder(Params p, string q);

    private static async Task<(string From, string Select, Builder Match, Builder Rank)> ShapeAsync(
        TenantDbContext db, InsightRecordType type, CancellationToken ct)
    {
        switch (type)
        {
            case InsightRecordType.Lead:
            {
                var keys = await SearchableKeysAsync(db, CustomFieldEntity.Lead, ct);
                return ("leads t",
                    "t.\"Id\", t.\"FirstName\" || ' ' || t.\"LastName\", t.\"Email\", t.\"Mobile\"",
                    (p, q) => PersonMatch(p, q, "(t.\"FirstName\" || ' ' || t.\"LastName\")", ["t.\"FirstName\"", "t.\"LastName\""], keys),
                    (p, q) => PersonRank(p, q, "(t.\"FirstName\" || ' ' || t.\"LastName\")"));
            }
            case InsightRecordType.Contact:
            {
                var keys = await SearchableKeysAsync(db, CustomFieldEntity.Contact, ct);
                return ("contacts t",
                    "t.\"Id\", t.\"Name\", t.\"Email\", t.\"Mobile\"",
                    (p, q) => PersonMatch(p, q, "t.\"Name\"", ["t.\"Name\""], keys),
                    (p, q) => PersonRank(p, q, "t.\"Name\""));
            }
            case InsightRecordType.Opportunity:
                return ("opportunities t",
                    "t.\"Id\", t.\"Title\", NULL::text, NULL::text",
                    (p, q) => $"t.\"Title\" ILIKE {p.Add(Contains(q), NpgsqlDbType.Text)}",
                    (p, q) => TitleRank(p, q, "t.\"Title\""));
            default:
                return ("lead_tasks t JOIN leads l ON l.\"Id\" = t.\"LeadId\"",
                    // The fourth column carries the task's lead id, so a hit can link to the lead.
                    "t.\"Id\", t.\"Title\", l.\"FirstName\" || ' ' || l.\"LastName\", l.\"Id\"::text",
                    (p, q) => $"t.\"Title\" ILIKE {p.Add(Contains(q), NpgsqlDbType.Text)}",
                    (p, q) => TitleRank(p, q, "t.\"Title\""));
        }
    }

    private static string PersonMatch(Params p, string q, string fullName, string[] nameParts, string[] customKeys)
    {
        var pattern = p.Add(Contains(q), NpgsqlDbType.Text);
        var parts = new List<string> { $"{fullName} ILIKE {pattern}", $"t.\"Email\" ILIKE {pattern}" };
        parts.AddRange(nameParts.Select(n => $"{n} ILIKE {pattern}"));

        if (SearchNormalizer.PhoneTail(q) is { } tail)
            parts.Add($"t.\"MobileDigits\" LIKE {p.Add("%" + tail + "%", NpgsqlDbType.Text)}");

        if (customKeys.Length > 0)
        {
            // The jsonb text ILIKE is the indexed prefilter; the EXISTS confines the match to the
            // fields the tenant marked searchable, so a hit in a private note is not a result.
            var keys = p.Add(customKeys, NpgsqlDbType.Array | NpgsqlDbType.Text);
            parts.Add($"(t.custom_field_values::text ILIKE {pattern} AND EXISTS (SELECT 1 FROM unnest({keys}) k " +
                      $"WHERE (t.custom_field_values->'Values'->>k) ILIKE {pattern}))");
        }

        return string.Join(" OR ", parts);
    }

    private static string PersonRank(Params p, string q, string fullName)
    {
        var exact = p.Add(q, NpgsqlDbType.Text);
        var prefix = p.Add(QueryCompiler.EscapeLike(q) + "%", NpgsqlDbType.Text);
        var phone = SearchNormalizer.PhoneTail(q) is { } tail
            ? $"WHEN t.\"MobileDigits\" LIKE {p.Add("%" + tail, NpgsqlDbType.Text)} THEN 4.0 " : "";
        return $"(CASE WHEN lower(t.\"Email\") = lower({exact}) THEN 4.0 {phone}" +
               $"WHEN lower({fullName}) = lower({exact}) THEN 3.0 WHEN {fullName} ILIKE {prefix} THEN 2.0 ELSE 1.0 END " +
               $"+ similarity({fullName}, {exact}))::float8";
    }

    private static string TitleRank(Params p, string q, string column)
    {
        var exact = p.Add(q, NpgsqlDbType.Text);
        var prefix = p.Add(QueryCompiler.EscapeLike(q) + "%", NpgsqlDbType.Text);
        return $"(CASE WHEN lower({column}) = lower({exact}) THEN 3.0 WHEN {column} ILIKE {prefix} THEN 2.0 ELSE 1.0 END " +
               $"+ similarity({column}, {exact}))::float8";
    }

    private static string Contains(string q) => "%" + QueryCompiler.EscapeLike(q) + "%";

    private static async Task<string[]> SearchableKeysAsync(TenantDbContext db, CustomFieldEntity scope, CancellationToken ct) =>
        (await db.CustomFieldDefinitions.AsNoTracking()
            .Where(f => f.AppliesTo == scope && f.IsSearchable && !f.IsArchived)
            .Select(f => f.Id)
            .ToListAsync(ct)).Select(id => id.ToString()).ToArray();
}
