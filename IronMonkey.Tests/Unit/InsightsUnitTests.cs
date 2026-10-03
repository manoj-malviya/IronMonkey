using System.Text;
using System.Text.Json;
using IronMonkey.ApiService.Features.Insights;
using IronMonkey.ApiService.Features.Reports.Dashboard;
using IronMonkey.Data.Entities;
using IronMonkey.Data.Visibility;
using Xunit;

namespace IronMonkey.Tests.Unit;

public class InsightsUnitTests
{
    // ── Normalisation ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("07700 900123", "7700900123")]
    [InlineData("+44 7700 900123", "7700900123")]
    [InlineData("(0)7700-900123", "7700900123")]
    [InlineData("+447700900123", "7700900123")]
    [InlineData("900123", "900123")]
    public void Phone_numbers_reduce_to_a_tail_contained_in_every_stored_form(string typed, string tail)
    {
        Assert.Equal(tail, SearchNormalizer.PhoneTail(typed));
        Assert.Contains(tail, "447700900123");
        Assert.Contains(tail, "07700900123");
    }

    [Theory]
    [InlineData("12345")]      // too short to be a phone number
    [InlineData("jane 0770")]  // has letters: a name search, not a phone search
    [InlineData("000000")]     // only trunk zeros
    public void Non_phone_input_is_not_treated_as_a_phone_search(string typed) =>
        Assert.Null(SearchNormalizer.PhoneTail(typed));

    [Fact]
    public void Emails_normalise_case_and_whitespace() =>
        Assert.Equal("jane@example.com", SearchNormalizer.NormalizeEmail("  Jane@Example.COM "));

    // ── Tenant-timezone ranges ───────────────────────────────────────────────────────

    [Fact]
    public void This_month_is_the_tenants_month_and_stays_half_open()
    {
        var london = TimeZoneInfo.FindSystemTimeZoneById("Europe/London");
        // 00:30 on 1 Oct in London is still 30 Sept in UTC (BST is UTC+1).
        var now = new DateTime(2026, 9, 30, 23, 30, 0, DateTimeKind.Utc);

        var range = DashboardDateRange.ResolveIn("thismonth", null, null, london, now);

        Assert.Equal(new DateTime(2026, 9, 30, 23, 0, 0, DateTimeKind.Utc), range.From); // London midnight, 1 Oct
        Assert.Equal(new DateTime(2026, 10, 1, 23, 0, 0, DateTimeKind.Utc), range.ToExclusive); // London midnight, 2 Oct

        // A UTC "this month" at the same instant would have been September — the wrong month.
        Assert.Equal(9, DashboardDateRange.Resolve("thismonth", null, null, now).From.Month);
    }

    [Fact]
    public void A_custom_range_is_read_as_tenant_local_dates()
    {
        var tokyo = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo");
        var range = DashboardDateRange.ResolveIn(null, new DateTime(2026, 10, 1), new DateTime(2026, 10, 1), tokyo,
            new DateTime(2026, 10, 3, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(new DateTime(2026, 9, 30, 15, 0, 0, DateTimeKind.Utc), range.From);
        Assert.Equal(new DateTime(2026, 10, 1, 15, 0, 0, DateTimeKind.Utc), range.ToExclusive);
    }

    // ── CSV ──────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("=HYPERLINK(\"http://evil\")", "\"'=HYPERLINK(\"\"http://evil\"\")\"")]
    [InlineData("+1 555", "'+1 555")]
    [InlineData("-2", "'-2")]
    [InlineData("@SUM(A1)", "'@SUM(A1)")]
    [InlineData("a,b", "\"a,b\"")]
    [InlineData("plain", "plain")]
    public void Csv_cells_cannot_execute_as_spreadsheet_formulas(string value, string expected) =>
        Assert.Equal(expected, CsvWriter.Cell(value));

    [Fact]
    public void Csv_starts_with_a_utf8_bom_so_excel_reads_names_correctly()
    {
        var bytes = CsvWriter.Write(["Name"], [["Zoë"]]);
        Assert.Equal(Encoding.UTF8.GetPreamble(), bytes[..3]);
    }

    // ── Compiler: injection and limits ───────────────────────────────────────────────

    private static readonly RecordDef Leads = new(InsightRecordType.Lead, "leads t", "leads:read", new Dictionary<string, FieldDef>
    {
        ["firstName"] = new("firstName", "First name", FieldKind.Text, "t.\"FirstName\""),
        ["createdAt"] = new("createdAt", "Created", FieldKind.Instant, "t.\"CreatedAt\""),
        ["cf:abc"] = new("cf:abc", "Model", FieldKind.Text, "{key}", ParameterValue: "abc")
    }, CustomFieldEntity.Lead);

    private static QueryCompiler Compiler() => new(Leads,
        new QueryContext(Guid.NewGuid(), Guid.NewGuid(), RecordVisibility.Unrestricted, TimeZoneInfo.Utc, DateTime.UtcNow));

    private static FilterGroup One(string field, string op, object? value) => new()
    {
        Conditions = [new FilterCondition { Field = field, Op = op, Value = value is null ? null : JsonSerializer.SerializeToElement(value) }]
    };

    [Theory]
    [InlineData("firstName\"; DROP TABLE leads; --", "eq")]
    [InlineData("t.\"Id\" OR 1=1", "eq")]
    [InlineData("cf:abc' OR '1'='1", "eq")]
    [InlineData("firstName", "= '' OR 1=1; --")]
    [InlineData("firstName", "eq) OR (1=1")]
    public void Hostile_field_names_and_operators_are_rejected_before_any_sql_exists(string field, string op)
    {
        var ex = Assert.Throws<QueryValidationException>(() => Compiler().CompileCount(One(field, op, "x")));
        Assert.NotEmpty(ex.Problems);
    }

    [Fact]
    public void Hostile_values_are_parameters_and_never_appear_in_sql_text()
    {
        const string payload = "x'); DELETE FROM leads; SELECT ('";
        var compiled = Compiler().CompileCount(One("firstName", "contains", payload));

        Assert.DoesNotContain("DELETE", compiled.Sql);
        Assert.DoesNotContain(payload, compiled.Sql);
        Assert.Contains(compiled.Parameters, p => (p.Value as string)?.Contains("DELETE") == true);

        // Custom field keys are bound too: the definition id is a parameter, not SQL.
        var custom = Compiler().CompileCount(One("cf:abc", "eq", "Golf"));
        Assert.DoesNotContain("abc", custom.Sql);
        Assert.Contains(custom.Parameters, p => (p.Value as string) == "abc");
    }

    [Fact]
    public void Wildcards_typed_by_the_user_match_literally()
    {
        var compiled = Compiler().CompileCount(One("firstName", "contains", "100%_off"));
        Assert.Contains(compiled.Parameters, p => (p.Value as string) == "%100\\%\\_off%");
    }

    [Fact]
    public void Filters_are_bounded_in_depth_conditions_and_list_size()
    {
        var deep = new FilterGroup { Groups = [new() { Groups = [new() { Groups = [new() { Conditions = [new() { Field = "firstName", Op = "eq", Value = JsonSerializer.SerializeToElement("a") }] }] }] }] };
        Assert.Contains(Assert.Throws<QueryValidationException>(() => Compiler().CompileCount(deep)).Problems, p => p.Contains("nest"));

        var many = new FilterGroup
        {
            Conditions = Enumerable.Range(0, QueryCompiler.MaxConditions + 1)
                .Select(i => new FilterCondition { Field = "firstName", Op = "eq", Value = JsonSerializer.SerializeToElement("a") }).ToList()
        };
        Assert.Contains(Assert.Throws<QueryValidationException>(() => Compiler().CompileCount(many)).Problems, p => p.Contains("at most"));

        var list = One("firstName", "in", Enumerable.Range(0, QueryCompiler.MaxListValues + 1).Select(i => i.ToString()).ToArray());
        Assert.Contains(Assert.Throws<QueryValidationException>(() => Compiler().CompileCount(list)).Problems, p => p.Contains("at most"));
    }

    [Fact]
    public void Every_sort_ends_with_an_id_tiebreak_and_unknown_sort_keys_are_refused()
    {
        var sorted = Compiler().CompilePage(new ViewDefinition { Sort = new SortSpec { Field = "firstName" } }, 25, 0);
        Assert.Contains("t.\"FirstName\" ASC NULLS LAST, t.\"Id\" ASC", sorted.Sql);

        var unsorted = Compiler().CompilePage(new ViewDefinition(), 25, 0);
        Assert.Contains("t.\"Id\" DESC", unsorted.Sql);

        Assert.Throws<QueryValidationException>(() =>
            Compiler().CompilePage(new ViewDefinition { Sort = new SortSpec { Field = "Password" } }, 25, 0));
    }

    [Fact]
    public void Date_ranges_compile_to_half_open_bounds()
    {
        var compiled = Compiler().CompileCount(One("createdAt", "in_range", new { preset = "thismonth" }));
        Assert.Contains(">=", compiled.Sql);
        Assert.Contains("t.\"CreatedAt\" < ", compiled.Sql);
        Assert.DoesNotContain("<=", compiled.Sql);
    }

    [Fact]
    public void Restricted_visibility_becomes_an_owner_predicate_in_the_sql()
    {
        var me = Guid.NewGuid();
        var visibility = RecordVisibility.Create(new Dictionary<VisibilityRecordType, IReadOnlyCollection<Guid>?> { [VisibilityRecordType.Lead] = [me] });
        var compiled = new QueryCompiler(Leads, new QueryContext(Guid.NewGuid(), me, visibility, TimeZoneInfo.Utc, DateTime.UtcNow))
            .CompileCount(new FilterGroup());

        Assert.Contains("t.\"AssignedToUserId\" = ANY(", compiled.Sql);
        Assert.Contains(compiled.Parameters, p => p.Value is Guid[] owners && owners.SequenceEqual([me]));
    }
}
