using System.Text;
using System.Text.Json;
using IronMonkey.ApiService.BackgroundJobs;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.ApiService.Features.Commerce;
using IronMonkey.ApiService.Features.Insights;
using IronMonkey.Data;
using IronMonkey.Data.Entities;
using IronMonkey.Data.Presentation;
using IronMonkey.Data.Visibility;
using IronMonkey.Tests.Fixtures;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace IronMonkey.Tests.Integration;

/// <summary>
/// Search, views, reports and exports against a real tenant database, under real visibility.
/// </summary>
[Collection("Integration")]
public class InsightsTests(PostgreSqlFixture fixture) : IClassFixture<PostgreSqlFixture>
{
    private readonly TenantDbContextFactory _factory = new();
    private static readonly DateTime Now = new(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);

    private sealed class FixedTenantService(Guid tenantId, string cs) : ITenantService
    {
        public Guid GetCurrentTenantId() => tenantId;
        public Task<string> GetConnectionStringAsync(CancellationToken cancellationToken = default) => Task.FromResult(cs);
    }

    private sealed class FixedUser(Guid userId) : IUserContext
    {
        public Guid UserId => userId;
        public string IdentityId => userId.ToString();
        public Guid TenantId => Guid.Empty;
        public string? ActAs => null;
    }

    private sealed class ZoneResolver(string zone) : ITenantCommerceContextResolver
    {
        public Task<TenantCommerceContext> ResolveAsync(Guid tenantId, CancellationToken ct) =>
            Task.FromResult(new TenantCommerceContext(null, TenantFormatting.From(new TenantLocale { TimeZoneId = zone })));
    }

    private sealed class Clock(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(now, TimeSpan.Zero);
    }

    private sealed class Registry(Guid tenantId, string cs) : ITenantRegistry
    {
        public Task<string> GetConnectionStringAsync(Guid id, CancellationToken cancellationToken = default) =>
            id == tenantId ? Task.FromResult(cs) : throw new InvalidOperationException("Unknown tenant");
        public Task<IReadOnlyList<(Guid TenantId, string ConnectionString)>> GetAllProvisionedTenantsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<(Guid, string)>>([(tenantId, cs)]);
    }

    private sealed record World(Guid TenantId, string Cs, FixedTenantService Tenant, Guid Admin, Guid Alice, Guid Bob,
        Guid ModelField, Guid NotesField, Guid AliceLead, Guid BobLead);

    private InsightService Insights(string zone = "Europe/London") => new(new ZoneResolver(zone), new Clock(Now));

    /// <summary>
    /// Admin plus TeleCallers Alice and Bob. A searchable custom field "Model" and a private
    /// one "Notes". Alice's lead stores the phone as "+447700900123"; Bob's lead is Bob's.
    /// </summary>
    private async Task<World> SeedAsync(string label)
    {
        var tenantId = Guid.NewGuid();
        var cs = fixture.ConnectionString.Replace("ironmonkey_test", $"ins_{label}_{Guid.NewGuid():N}");
        await using var db = Raw(tenantId, cs);
        await db.Database.MigrateAsync();

        var adminRole = await db.Roles.SingleAsync(r => r.Id == Role.Admin.Id);
        var tele = await db.Roles.SingleAsync(r => r.Id == Role.TeleCaller.Id);
        var admin = User.Create(tenantId, "admin", $"admin@{label}.test", "x", adminRole);
        var alice = User.Create(tenantId, "alice", $"alice@{label}.test", "x", tele);
        var bob = User.Create(tenantId, "bob", $"bob@{label}.test", "x", tele);
        db.Users.AddRange(admin, alice, bob);

        var model = CustomFieldDefinition.Create(tenantId, "Model", CustomFieldType.Dropdown, false, ["Golf", "Polo"]);
        model.SetSearchable(true);
        var notes = CustomFieldDefinition.Create(tenantId, "Notes", CustomFieldType.Text, false);
        var budget = CustomFieldDefinition.Create(tenantId, "Budget", CustomFieldType.Number, false);
        db.CustomFieldDefinitions.AddRange(model, notes, budget);

        var stage = PipelineStage.CreateFor(tenantId, PipelineRecordType.Lead, "New", 1, StageType.Entry);
        db.PipelineStages.Add(stage);
        await db.SaveChangesAsync();

        Lead L(string first, string last, string mobile, string email, Guid? owner, string modelValue, string note, int budgetValue)
        {
            var l = Lead.Create(tenantId, first, last, mobile, email, LeadSource.Manual, stage.Id);
            l.AssignTo(owner);
            l.CustomFields.Set(model.Id.ToString(), modelValue);
            l.CustomFields.Set(notes.Id.ToString(), note);
            l.CustomFields.Set(budget.Id.ToString(), budgetValue);
            return l;
        }

        var a = L("Priya", "Shah", "+447700900123", "priya.shah@example.com", alice.Id, "Golf", "zephyr secret", 20000);
        var b = L("Brian", "Okafor", "07800 111222", "brian@example.com", bob.Id, "Polo", "", 15000);
        db.Leads.AddRange(a, b);
        await db.SaveChangesAsync();

        // Many same-named leads for the tiebreak test.
        for (var i = 0; i < 30; i++)
            db.Leads.Add(L("Same", "Name", "0700000" + i.ToString("D4"), $"same{i}@example.com", alice.Id, "Golf", "", 1000));
        await db.SaveChangesAsync();

        return new World(tenantId, cs, new FixedTenantService(tenantId, cs), admin.Id, alice.Id, bob.Id, model.Id, notes.Id, a.Id, b.Id);
    }

    private static TenantDbContext Raw(Guid tenantId, string cs) =>
        new(new DbContextOptionsBuilder<TenantDbContext>().UseNpgsql(cs).Options, tenantId, RecordVisibility.Unrestricted);

    private async Task NarrowLeadsToOwnAsync(World w)
    {
        await using var db = Raw(w.TenantId, w.Cs);
        db.RoleRecordScopes.Add(RoleRecordScope.Create(Role.TeleCaller.Id, VisibilityRecordType.Lead, VisibilityScope.Own));
        await db.SaveChangesAsync();
    }

    private async Task<RecordVisibility> VisibilityOf(World w, Guid user)
    {
        await using var db = Raw(w.TenantId, w.Cs);
        return await RecordVisibilityResolver.ResolveAsync(db, user, default);
    }

    private static FilterGroup Where(string logic, params (string Field, string Op, object? Value)[] conditions) => new()
    {
        Logic = logic,
        Conditions = conditions.Select(c => new FilterCondition
        {
            Field = c.Field, Op = c.Op, Value = c.Value is null ? null : JsonSerializer.SerializeToElement(c.Value)
        }).ToList()
    };

    // ── Search ───────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Search_finds_by_partial_name_email_differently_formatted_phone_and_searchable_custom_field()
    {
        var w = await SeedAsync("search");
        await using var db = _factory.CreateForTenant(w.Cs, w.TenantId);
        var search = new SearchService();

        async Task<List<Guid>> Leads(string q) =>
            (await search.SearchAsync(db, w.TenantId, q, [InsightRecordType.Lead], 10, 1, default)).Groups.Single().Hits.Select(h => h.Id).ToList();

        Assert.Contains(w.AliceLead, await Leads("pri"));
        Assert.Contains(w.AliceLead, await Leads("SHAH@example"));
        Assert.Contains(w.AliceLead, await Leads("07700 900123"));   // stored as +447700900123
        Assert.Contains(w.AliceLead, await Leads("+44 7700-900123"));
        Assert.Equal(w.AliceLead, (await Leads("priya.shah@example.com"))[0]); // exact email ranks first
        Assert.Contains(w.BobLead, await Leads("Polo"));             // searchable custom field
        Assert.Empty(await Leads("zephyr"));                          // non-searchable field never matches
    }

    [Fact]
    public async Task Search_results_and_counts_never_include_records_outside_the_callers_visibility()
    {
        var w = await SeedAsync("searchvis");
        await NarrowLeadsToOwnAsync(w);
        var search = new SearchService();

        using (RecordVisibility.Enter(await VisibilityOf(w, w.Bob)))
        {
            await using var db = _factory.CreateForTenant(w.Cs, w.TenantId);
            var group = (await search.SearchAsync(db, w.TenantId, "example.com", [InsightRecordType.Lead], 10, 1, default)).Groups.Single();
            Assert.Equal([w.BobLead], group.Hits.Select(h => h.Id).ToList());
            Assert.Equal(1, group.Count);            // the count discloses nothing either
            Assert.Empty((await search.SearchAsync(db, w.TenantId, "07700 900123", [InsightRecordType.Lead], 10, 1, default)).Groups.Single().Hits);
        }

        using (RecordVisibility.Enter(await VisibilityOf(w, w.Admin)))
        {
            await using var db = _factory.CreateForTenant(w.Cs, w.TenantId);
            var group = (await search.SearchAsync(db, w.TenantId, "example.com", [InsightRecordType.Lead], 5, 1, default)).Groups.Single();
            Assert.Equal(5, group.Hits.Count);       // per-type cap
            Assert.True(group.HasMore);
            Assert.Equal(32, group.Count);
        }
    }

    // ── Views ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_view_filters_on_custom_fields_with_explicit_and_or_semantics()
    {
        var w = await SeedAsync("views");
        await using var db = _factory.CreateForTenant(w.Cs, w.TenantId);
        var record = await RecordCatalog.LoadAsync(db, InsightRecordType.Lead, default);
        var insights = Insights();
        var ctx = await insights.ContextAsync(db, w.TenantId, w.Admin, default);
        var model = "cf:" + w.ModelField;

        // (Model = Polo) OR (firstName = Priya)
        var or = new ViewDefinition { Filter = Where("or", (model, "eq", "Polo"), ("firstName", "eq", "Priya")), Columns = ["firstName", model] };
        var orResult = await insights.RunViewAsync(db, record, ctx, or, 1, 50, default);
        Assert.Equal(2, orResult.TotalCount);
        Assert.Contains(orResult.Rows, r => r.Values[1] == "Polo");

        // (Model = Golf) AND (Budget > 10000) → only Priya
        var budget = "cf:" + (await db.CustomFieldDefinitions.SingleAsync(f => f.FieldName == "Budget")).Id;
        var and = new ViewDefinition { Filter = Where("and", (model, "eq", "Golf"), (budget, "gt", 10000)) };
        Assert.Equal(1, (await insights.RunViewAsync(db, record, ctx, and, 1, 50, default)).TotalCount);
    }

    [Fact]
    public async Task Paging_a_sort_on_a_shared_value_never_repeats_or_drops_a_row()
    {
        var w = await SeedAsync("tiebreak");
        await using var db = _factory.CreateForTenant(w.Cs, w.TenantId);
        var record = await RecordCatalog.LoadAsync(db, InsightRecordType.Lead, default);
        var insights = Insights();
        var ctx = await insights.ContextAsync(db, w.TenantId, w.Admin, default);
        var view = new ViewDefinition { Filter = Where("and", ("firstName", "eq", "Same")), Sort = new SortSpec { Field = "lastName" } };

        var seen = new List<Guid>();
        for (var page = 1; page <= 5; page++)
            seen.AddRange((await insights.RunViewAsync(db, record, ctx, view, page, 7, default)).Rows.Select(r => r.Id));

        Assert.Equal(30, seen.Count);
        Assert.Equal(30, seen.Distinct().Count());
    }

    [Fact]
    public async Task A_shared_view_runs_under_each_viewers_own_visibility_and_a_broken_one_says_so()
    {
        var w = await SeedAsync("shared");
        await NarrowLeadsToOwnAsync(w);
        var definition = new ViewDefinition { Filter = Where("and", ("email", "contains", "example.com")), Columns = ["firstName"] };

        // Admin saves and shares the view.
        Guid viewId;
        using (RecordVisibility.Enter(await VisibilityOf(w, w.Admin)))
        {
            var created = await SavedViewEndpoints.Create(new SaveViewRequest("Everyone", "Lead", definition, true),
                w.Tenant, _factory, new FixedUser(w.Admin), Insights(), default);
            viewId = (Guid)((dynamic)((IValueHttpResult)created).Value!).Id;
        }

        // Bob sees the shared view in his list, and running it shows only his lead.
        using (RecordVisibility.Enter(await VisibilityOf(w, w.Bob)))
        {
            var listed = (Ok<List<SavedViewResponse>>)await SavedViewEndpoints.List("Lead", w.Tenant, _factory, new FixedUser(w.Bob), default);
            var view = Assert.Single(listed.Value!);
            Assert.False(view.IsOwner);
            Assert.Empty(view.Problems);

            await using var db = _factory.CreateForTenant(w.Cs, w.TenantId);
            var record = await RecordCatalog.LoadAsync(db, InsightRecordType.Lead, default);
            var result = await Insights().RunViewAsync(db, record, await Insights().ContextAsync(db, w.TenantId, w.Bob, default), view.Definition, 1, 50, default);
            Assert.Equal(1, result.TotalCount);

            // Bob cannot edit someone else's view.
            Assert.IsType<NotFound>(await SavedViewEndpoints.Update(viewId, new SaveViewRequest("Mine now", "Lead", definition, true),
                w.Tenant, _factory, new FixedUser(w.Bob), Insights(), default));
        }

        // A private view is invisible to others.
        using (RecordVisibility.Enter(await VisibilityOf(w, w.Alice)))
            await SavedViewEndpoints.Create(new SaveViewRequest("Alice only", "Lead", definition, false), w.Tenant, _factory, new FixedUser(w.Alice), Insights(), default);
        using (RecordVisibility.Enter(await VisibilityOf(w, w.Bob)))
        {
            var listed = (Ok<List<SavedViewResponse>>)await SavedViewEndpoints.List("Lead", w.Tenant, _factory, new FixedUser(w.Bob), default);
            Assert.DoesNotContain(listed.Value!, v => v.Name == "Alice only");
        }

        // Deleting a custom field the view filters on makes it report itself broken.
        var broken = new ViewDefinition { Filter = Where("and", ("cf:" + w.ModelField, "eq", "Golf")) };
        using (RecordVisibility.Enter(await VisibilityOf(w, w.Admin)))
            await SavedViewEndpoints.Create(new SaveViewRequest("By model", "Lead", broken, false), w.Tenant, _factory, new FixedUser(w.Admin), Insights(), default);
        await using (var db = Raw(w.TenantId, w.Cs))
        {
            var field = await db.CustomFieldDefinitions.SingleAsync(f => f.Id == w.ModelField);
            field.IsDeleted = true;
            await db.SaveChangesAsync();
        }
        using (RecordVisibility.Enter(await VisibilityOf(w, w.Admin)))
        {
            var listed = (Ok<List<SavedViewResponse>>)await SavedViewEndpoints.List("Lead", w.Tenant, _factory, new FixedUser(w.Admin), default);
            Assert.Contains("no longer exists", Assert.Single(listed.Value!, v => v.Name == "By model").Problems.Single());
        }
    }

    // ── Reports ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_report_groups_on_a_custom_field_and_buckets_months_in_the_tenants_timezone()
    {
        var w = await SeedAsync("report");
        await using (var raw = Raw(w.TenantId, w.Cs))
        {
            // 23:30 UTC on 30 Sept is 00:30 on 1 Oct in London: an October lead to that tenant.
            await raw.Database.ExecuteSqlRawAsync(@"UPDATE leads SET ""CreatedAt"" = '2026-09-30T23:30:00Z' WHERE ""Id"" = {0}", w.AliceLead);
            await raw.Database.ExecuteSqlRawAsync(@"UPDATE leads SET ""CreatedAt"" = '2026-09-30T22:30:00Z' WHERE ""Id"" = {0}", w.BobLead);
        }

        await using var db = _factory.CreateForTenant(w.Cs, w.TenantId);
        var record = await RecordCatalog.LoadAsync(db, InsightRecordType.Lead, default);
        var insights = Insights("Europe/London");
        var ctx = await insights.ContextAsync(db, w.TenantId, w.Admin, default);
        var budget = "cf:" + (await db.CustomFieldDefinitions.SingleAsync(f => f.FieldName == "Budget")).Id;

        var spec = new ReportSpec
        {
            Filter = Where("or", ("firstName", "eq", "Priya"), ("firstName", "eq", "Brian")),
            GroupBy = "cf:" + w.ModelField,
            TimeField = "createdAt",
            TimeGranularity = "month",
            Measures = [new MeasureSpec { Fn = "count" }, new MeasureSpec { Fn = "sum", Field = budget }]
        };
        var result = await insights.RunReportAsync(db, record, ctx, spec, default);

        var golf = Assert.Single(result.Rows, r => r.Group == "Golf");
        var polo = Assert.Single(result.Rows, r => r.Group == "Polo");
        Assert.Equal(new DateTime(2026, 10, 1), golf.Bucket);   // London October
        Assert.Equal(new DateTime(2026, 9, 1), polo.Bucket);    // 23:30 BST on 30 Sept
        Assert.Equal(20000m, golf.Values[1]);

        // "This month" in London, evaluated on 15 Oct: Priya (1 Oct local) is in, Brian is out.
        var month = await insights.RunReportAsync(db, record, ctx, new ReportSpec
        {
            Filter = spec.Filter, DateRange = new DateRangeSpec { Field = "createdAt", Preset = "thismonth" },
            Measures = [new MeasureSpec { Fn = "count" }]
        }, default);
        Assert.Equal(1m, month.Rows.Single().Values[0]);
    }

    [Fact]
    public async Task Report_grouping_cardinality_is_capped_and_flagged()
    {
        var w = await SeedAsync("cap");
        await using (var raw = Raw(w.TenantId, w.Cs))
        {
            var stage = await raw.PipelineStages.FirstAsync();
            for (var i = 0; i < InsightService.ReportGroupCap + 5; i++)
                raw.Leads.Add(Lead.Create(w.TenantId, "G" + i, "X", "1", $"g{i}@x.test", LeadSource.Manual, stage.Id));
            await raw.SaveChangesAsync();
        }

        await using var db = _factory.CreateForTenant(w.Cs, w.TenantId);
        var record = await RecordCatalog.LoadAsync(db, InsightRecordType.Lead, default);
        // firstName is not groupable; email is not either — group on a custom Text field instead.
        var notes = "cf:" + w.NotesField;
        await db.Database.ExecuteSqlRawAsync(
            @"UPDATE leads SET custom_field_values = jsonb_set(custom_field_values, ARRAY['Values', {0}], to_jsonb(""Email""), true)", w.NotesField.ToString());

        var result = await Insights().RunReportAsync(db, record, await Insights().ContextAsync(db, w.TenantId, w.Admin, default),
            new ReportSpec { GroupBy = notes, Measures = [new MeasureSpec { Fn = "count" }] }, default);

        Assert.True(result.Truncated);
        Assert.Equal(InsightService.ReportGroupCap, result.Rows.Count);
    }

    [Fact]
    public async Task Insight_statements_are_read_only_and_cancelled_by_their_timeout()
    {
        var w = await SeedAsync("limits");
        await using var db = _factory.CreateForTenant(w.Cs, w.TenantId);

        await Assert.ThrowsAsync<QueryTimeoutException>(() =>
            InsightSql.QueryAsync(db, new CompiledQuery("SELECT pg_sleep(2)", []), 200, default));

        var ex = await Assert.ThrowsAsync<PostgresException>(() =>
            InsightSql.QueryAsync(db, new CompiledQuery("DELETE FROM leads", []), 5_000, default));
        Assert.Equal("25006", ex.SqlState); // read_only_sql_transaction

        await using var check = Raw(w.TenantId, w.Cs);
        Assert.Equal(32, await check.Leads.CountAsync());
    }

    [Fact]
    public async Task No_crafted_definition_can_inject_or_read_another_tenant()
    {
        var w = await SeedAsync("inject");
        var other = await SeedAsync("inject2");
        await using var db = _factory.CreateForTenant(w.Cs, w.TenantId);
        var record = await RecordCatalog.LoadAsync(db, InsightRecordType.Lead, default);
        var insights = Insights();
        var ctx = await insights.ContextAsync(db, w.TenantId, w.Admin, default);

        string[] hostile =
        [
            "x' OR '1'='1", "'; DROP TABLE leads; --", "%' UNION SELECT \"Password\" FROM \"Users\" --",
            "\\'; SELECT pg_sleep(10); --", "$$; DELETE FROM leads; $$"
        ];
        foreach (var value in hostile)
        {
            var result = await insights.RunViewAsync(db, record, ctx,
                new ViewDefinition { Filter = Where("and", ("firstName", "eq", value)), Columns = ["email"] }, 1, 50, default);
            Assert.Equal(0, result.TotalCount);
        }

        // Hostile structure is refused outright.
        await Assert.ThrowsAsync<QueryValidationException>(() => insights.RunReportAsync(db, record, ctx, new ReportSpec
        {
            GroupBy = "\"Password\" FROM \"Users\" --",
            Measures = [new MeasureSpec { Fn = "sum); DROP TABLE leads; --", Field = "firstName" }]
        }, default));

        // The other tenant shares this database server, and its rows remain unreachable: every
        // statement is pinned to this tenant's id.
        var everything = await insights.RunViewAsync(db, record, ctx, new ViewDefinition(), 1, 200, default);
        Assert.Equal(32, everything.TotalCount);
        await using var check = Raw(w.TenantId, w.Cs);
        Assert.Equal(32, await check.Leads.CountAsync());
    }

    // ── Exports ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task An_export_runs_as_its_requester_honours_visibility_is_audited_and_downloads_only_for_them()
    {
        var w = await SeedAsync("export");
        await NarrowLeadsToOwnAsync(w);
        await using (var raw = Raw(w.TenantId, w.Cs))
        {
            // Give TeleCallers export rights for this test, and plant a formula in Bob's lead.
            raw.RolePermissions.Add(new RolePermission { RoleId = Role.TeleCaller.Id, PermissionId = Permission.DataExport.Id });
            var bobLead = await raw.Leads.SingleAsync(l => l.Id == w.BobLead);
            bobLead.UpdateLeadInfo("=HYPERLINK(\"http://evil\")", bobLead.LastName, bobLead.Mobile, bobLead.Email, bobLead.Source);
            await raw.SaveChangesAsync();
        }

        Guid exportId;
        await using (var raw = Raw(w.TenantId, w.Cs))
        {
            var job = ExportJob.Queue(w.TenantId, w.Bob, InsightRecordType.Lead, "View", "Lead list",
                JsonSerializer.Serialize(new ViewDefinition { Columns = ["firstName", "email"] }, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                Now.AddDays(7));
            raw.ExportJobs.Add(job);
            await raw.SaveChangesAsync();
            exportId = job.Id;
        }

        await new DataExportJob(new Registry(w.TenantId, w.Cs), _factory, Insights(), new Clock(Now), NullLogger<DataExportJob>.Instance)
            .RunAsync(w.TenantId, exportId, default);

        await using (var raw = Raw(w.TenantId, w.Cs))
        {
            var job = await raw.ExportJobs.SingleAsync(e => e.Id == exportId);
            Assert.Equal(ExportStatus.Completed, job.Status);
            Assert.Equal(1, job.RowCount);  // Bob's lead only — not Alice's 31

            var csv = Encoding.UTF8.GetString(job.Content!);
            Assert.Contains("'=HYPERLINK", csv);
            Assert.DoesNotContain("priya", csv, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(await raw.ActivityLogs.ToListAsync(), a => a.EventType == "DataExported" && a.ActorId == w.Bob);
        }

        // Alice cannot download Bob's export.
        Assert.IsType<NotFound>(await ExportEndpoints.Download(exportId, w.Tenant, _factory, new FixedUser(w.Alice), new Clock(Now), default));
        Assert.IsType<FileContentHttpResult>(await ExportEndpoints.Download(exportId, w.Tenant, _factory, new FixedUser(w.Bob), new Clock(Now), default));
        // And after expiry nobody can.
        Assert.IsType<NotFound>(await ExportEndpoints.Download(exportId, w.Tenant, _factory, new FixedUser(w.Bob), new Clock(Now.AddDays(8)), default));
    }

    [Fact]
    public async Task An_export_whose_requester_lost_permission_while_queued_does_not_run()
    {
        var w = await SeedAsync("revoked");
        Guid exportId;
        await using (var raw = Raw(w.TenantId, w.Cs))
        {
            // Alice never held data:export.
            var job = ExportJob.Queue(w.TenantId, w.Alice, InsightRecordType.Lead, "View", "Lead list", "{}", Now.AddDays(7));
            raw.ExportJobs.Add(job);
            await raw.SaveChangesAsync();
            exportId = job.Id;
        }

        await new DataExportJob(new Registry(w.TenantId, w.Cs), _factory, Insights(), new Clock(Now), NullLogger<DataExportJob>.Instance)
            .RunAsync(w.TenantId, exportId, default);

        await using var check = Raw(w.TenantId, w.Cs);
        var result = await check.ExportJobs.SingleAsync(e => e.Id == exportId);
        Assert.Equal(ExportStatus.Failed, result.Status);
        Assert.Null(result.Content);
    }

    [Fact]
    public async Task A_scheduled_report_runs_as_its_owner_and_stores_the_result()
    {
        var w = await SeedAsync("sched");
        await NarrowLeadsToOwnAsync(w);
        Guid reportId;
        await using (var raw = Raw(w.TenantId, w.Cs))
        {
            var report = ReportDefinition.Create(w.TenantId, w.Bob, "My leads", InsightRecordType.Lead,
                JsonSerializer.Serialize(new ReportSpec(), new JsonSerializerOptions(JsonSerializerDefaults.Web)), false, ReportSchedule.Daily);
            raw.ReportDefinitions.Add(report);
            await raw.SaveChangesAsync();
            reportId = report.Id;
        }

        await new ScheduledReportJob(new Registry(w.TenantId, w.Cs), _factory, Insights(), new Clock(Now), NullLogger<ScheduledReportJob>.Instance)
            .RunAsync(w.TenantId, reportId, default);

        await using var check = Raw(w.TenantId, w.Cs);
        var run = await check.ReportRuns.SingleAsync(r => r.ReportDefinitionId == reportId);
        Assert.Null(run.Error);
        var result = JsonSerializer.Deserialize<ReportResult>(run.ResultJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal(1m, result.Rows.Single().Values[0]); // Bob's own lead only
    }
}
