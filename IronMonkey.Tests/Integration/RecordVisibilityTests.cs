using IronMonkey.ApiService.Common.Auth;
using IronMonkey.ApiService.Common.Results;
using IronMonkey.ApiService.Features.Activity.Timeline;
using IronMonkey.ApiService.Features.Leads;
using IronMonkey.ApiService.Features.Leads.Duplicates;
using IronMonkey.ApiService.Features.Quotes;
using IronMonkey.ApiService.Features.Reports.Dashboard;
using IronMonkey.ApiService.Features.RoleManagement;
using IronMonkey.Common.Auth;
using IronMonkey.Data;
using IronMonkey.Data.Entities;
using IronMonkey.Data.Visibility;
using IronMonkey.Tests.Fixtures;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace IronMonkey.Tests.Integration;

/// <summary>
/// Record-level visibility, end to end through the real handlers. Each test resolves a user's
/// visibility with the real resolver and makes it ambient, exactly as the request middleware
/// does — so the handler under test enforces it only if the query layer does.
/// </summary>
[Collection("Integration")]
public class RecordVisibilityTests(PostgreSqlFixture fixture) : IClassFixture<PostgreSqlFixture>
{
    private readonly TenantDbContextFactory _factory = new();

    private sealed class FixedTenantService(Guid tenantId, string connectionString) : ITenantService
    {
        public Guid GetCurrentTenantId() => tenantId;
        public Task<string> GetConnectionStringAsync(CancellationToken cancellationToken = default) => Task.FromResult(connectionString);
    }

    private sealed class FixedUser(Guid userId) : IUserContext
    {
        public Guid UserId => userId;
        public string IdentityId => userId.ToString();
        public Guid TenantId => Guid.Empty;
        public string? ActAs => null;
    }

    private sealed class NoCache : IRecordVisibilityCache
    {
        public int Invalidations { get; private set; }
        public Task<RecordVisibility> GetAsync(Guid tenantId, Guid userId, Func<Task<RecordVisibility>> resolve, CancellationToken ct) => resolve();
        public Task InvalidateTenantAsync(Guid tenantId, CancellationToken ct = default) { Invalidations++; return Task.CompletedTask; }
    }

    private sealed record World(Guid TenantId, string Cs, FixedTenantService Tenant, Guid Admin, Guid Alice, Guid Bob, Guid Carol,
        Guid StageId, Guid AliceLead, Guid BobLead, Guid CarolLead, Guid UnassignedLead);

    /// <summary>
    /// Admin (role 201); Alice, Bob, Carol are TeleCallers (302). One lead each, plus one
    /// unassigned lead. Alice and Bob share a team; Carol is outside it.
    /// </summary>
    private async Task<World> SeedAsync(string label)
    {
        var tenantId = Guid.NewGuid();
        var cs = fixture.ConnectionString.Replace("ironmonkey_test", $"vis_{label}_{Guid.NewGuid():N}");
        await using var db = new TenantDbContext(
            new DbContextOptionsBuilder<TenantDbContext>().UseNpgsql(cs).Options, tenantId, RecordVisibility.Unrestricted);
        await db.Database.MigrateAsync();

        var adminRole = await db.Roles.SingleAsync(r => r.Id == Role.Admin.Id);
        var teleRole = await db.Roles.SingleAsync(r => r.Id == Role.TeleCaller.Id);
        User U(string n, Role r) => User.Create(tenantId, n, $"{n}@{label}.test", "x", r);
        var admin = U("admin", adminRole);
        var alice = U("alice", teleRole);
        var bob = U("bob", teleRole);
        var carol = U("carol", teleRole);
        db.Users.AddRange(admin, alice, bob, carol);

        var stage = PipelineStage.CreateFor(tenantId, PipelineRecordType.Lead, "New", 1, StageType.Entry);
        db.PipelineStages.Add(stage);
        await db.SaveChangesAsync();

        Lead L(string first, Guid? owner)
        {
            var l = Lead.Create(tenantId, first, "Lead", "07700900" + Random.Shared.Next(100, 999), $"{first}@lead.test", LeadSource.Manual, stage.Id);
            l.AssignTo(owner);
            return l;
        }
        var a = L("aliceLead", alice.Id);
        var b = L("bobLead", bob.Id);
        var c = L("carolLead", carol.Id);
        var u = L("unassigned", null);
        db.Leads.AddRange(a, b, c, u);

        var team = Team.Create(tenantId, "Desk A", null, null);
        team.SetMembers([alice.Id, bob.Id]);
        db.Teams.Add(team);
        await db.SaveChangesAsync();

        return new World(tenantId, cs, new FixedTenantService(tenantId, cs), admin.Id, alice.Id, bob.Id, carol.Id,
            stage.Id, a.Id, b.Id, c.Id, u.Id);
    }

    private TenantDbContext Raw(World w) => new(
        new DbContextOptionsBuilder<TenantDbContext>().UseNpgsql(w.Cs).Options, w.TenantId, RecordVisibility.Unrestricted);

    private async Task SetScopeAsync(World w, int roleId, VisibilityRecordType type, VisibilityScope scope)
    {
        await using var db = Raw(w);
        db.RoleRecordScopes.Add(RoleRecordScope.Create(roleId, type, scope));
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Resolves a user's visibility. The caller enters it synchronously —
    /// <c>using (RecordVisibility.Enter(await VisibilityOf(...)))</c> — because an AsyncLocal
    /// assigned inside an awaited helper reverts when that helper returns.
    /// </summary>
    private async Task<RecordVisibility> VisibilityOf(World w, Guid userId)
    {
        await using var db = Raw(w);
        return await RecordVisibilityResolver.ResolveAsync(db, userId, default);
    }

    private Task<ListLeadsEndpoint.LeadPage> ListLeads(World w) =>
        ListLeadsEndpoint.Handle(null, null, null, 1, 25, "all", w.Tenant, _factory, PipelineTestHelpers.Scope(), default)
            .ContinueWith(t => t.Result.Ok());

    // ── Defaults ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task With_no_scopes_configured_every_user_sees_everything_as_before()
    {
        var w = await SeedAsync("default");
        await using var db = Raw(w);

        Assert.True((await RecordVisibilityResolver.ResolveAsync(db, w.Alice, default)).IsFullyUnrestricted);

        using (RecordVisibility.Enter(await VisibilityOf(w, w.Alice)))
            Assert.Equal(4, (await ListLeads(w)).TotalCount);
    }

    // ── Own scope ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Own_scope_limits_lists_counts_details_aggregates_duplicates_and_timeline()
    {
        var w = await SeedAsync("own");
        await SetScopeAsync(w, Role.TeleCaller.Id, VisibilityRecordType.Lead, VisibilityScope.Own);

        // Activity on Bob's lead, and Bob's lead email for a duplicate probe.
        await using (var db = Raw(w))
        {
            db.ActivityLogs.Add(ActivityLog.Create(w.TenantId, w.BobLead, w.Bob, "Note", "Lead", w.BobLead.ToString()));
            await db.SaveChangesAsync();
        }

        using (RecordVisibility.Enter(await VisibilityOf(w, w.Alice)))
        {
            // List and its total: only Alice's lead. The count is honest about what she may see.
            var page = await ListLeads(w);
            Assert.Equal(1, page.TotalCount);
            Assert.Equal(w.AliceLead, Assert.Single(page.Items).Id);

            // Direct navigation to Bob's lead is a 404 — the same answer as a lead that never existed.
            Assert.IsType<NotFound>((await GetLeadEndpoint.Handle(w.BobLead, w.Tenant, _factory, default)).Result);
            Assert.IsType<NotFound>((await GetLeadEndpoint.Handle(Guid.NewGuid(), w.Tenant, _factory, default)).Result);
            Assert.IsType<Ok<GetLeadEndpoint.Response>>((await GetLeadEndpoint.Handle(w.AliceLead, w.Tenant, _factory, default)).Result);

            // Dashboard aggregate.
            var summary = (await GetDashboardSummaryEndpoint.Handle("alltime", null, null, "all", w.Tenant, _factory,
                PipelineTestHelpers.Scope(), default)).Ok();
            Assert.Equal(1, summary.TotalLeads);

            // Duplicate detection must not report a lead Alice cannot see.
            var duplicates = await new DuplicateDetectionService(w.Tenant, _factory)
                .FindCandidatesAsync(w.TenantId, "bobLead@lead.test", "000", "bobLead Lead", default);
            Assert.DoesNotContain(duplicates, d => d.LeadId == w.BobLead);

            // Timeline of Bob's lead discloses nothing.
            var timeline = (await GetActivityTimelineEndpoint.Handle("Lead", w.BobLead, 1, null, w.Tenant, _factory, default)).Result
                as Ok<GetActivityTimelineEndpoint.TimelineResponse>;
            Assert.Equal(0, timeline!.Value!.TotalCount);
        }

        // The unassigned lead is visible only to an All scope (Admin keeps All by default).
        using (RecordVisibility.Enter(await VisibilityOf(w, w.Admin)))
            Assert.Equal(4, (await ListLeads(w)).TotalCount);
    }

    [Fact]
    public async Task Tasks_follow_their_assignee_scope_and_never_expose_an_invisible_lead()
    {
        var w = await SeedAsync("tasks");
        await SetScopeAsync(w, Role.TeleCaller.Id, VisibilityRecordType.Lead, VisibilityScope.Own);
        await using (var db = Raw(w))
        {
            // A task assigned to Alice on Bob's lead: Alice must not reach Bob's lead through it.
            db.LeadTasks.Add(LeadTask.Create(w.TenantId, w.BobLead, "Call back", null, TaskPriority.Medium, w.Alice));
            db.LeadTasks.Add(LeadTask.Create(w.TenantId, w.AliceLead, "Send brochure", null, TaskPriority.Medium, w.Alice));
            await db.SaveChangesAsync();
        }

        using (RecordVisibility.Enter(await VisibilityOf(w, w.Alice)))
        {
            await using var db = _factory.CreateForTenant(w.Cs, w.TenantId);
            var titles = await db.LeadTasks.Select(t => t.Title).ToListAsync();
            Assert.Equal(["Send brochure"], titles);
        }
    }

    // ── Team scope and hierarchy ─────────────────────────────────────────────────────

    [Fact]
    public async Task Team_scope_sees_teammates_and_a_manager_sees_sub_teams_but_not_outsiders()
    {
        var w = await SeedAsync("team");
        await SetScopeAsync(w, Role.TeleCaller.Id, VisibilityRecordType.Lead, VisibilityScope.Team);
        await SetScopeAsync(w, Role.Admin.Id, VisibilityRecordType.Lead, VisibilityScope.Team);

        using (RecordVisibility.Enter(await VisibilityOf(w, w.Alice)))
        {
            var ids = (await ListLeads(w)).Items.Select(i => i.Id).ToHashSet();
            Assert.Equal(new HashSet<Guid> { w.AliceLead, w.BobLead }, ids);
        }

        // Admin manages a parent team; Desk A sits beneath it. Carol is in neither.
        await using (var db = Raw(w))
        {
            var parent = Team.Create(w.TenantId, "Region", null, w.Admin);
            db.Teams.Add(parent);
            await db.SaveChangesAsync();
            var desk = await db.Teams.SingleAsync(t => t.Name == "Desk A");
            desk.SetParent(parent.Id);
            await db.SaveChangesAsync();
        }

        using (RecordVisibility.Enter(await VisibilityOf(w, w.Admin)))
        {
            var ids = (await ListLeads(w)).Items.Select(i => i.Id).ToHashSet();
            Assert.Contains(w.AliceLead, ids);
            Assert.Contains(w.BobLead, ids);
            Assert.DoesNotContain(w.CarolLead, ids);
            Assert.DoesNotContain(w.UnassignedLead, ids);
        }
    }

    [Fact]
    public async Task A_team_cycle_or_over_deep_hierarchy_is_rejected_at_write_time()
    {
        var w = await SeedAsync("cycle");
        var cache = new NoCache();
        var admin = new FixedUser(w.Admin);

        async Task<Guid> Create(string name, Guid? parent)
        {
            var r = await TeamEndpoints.Create(new SaveTeamRequest(name, null, null, parent, null), w.Tenant, admin, cache, default);
            return ((Created<TeamResponse>)r.Result).Value!.Id;
        }

        var a = await Create("A", null);
        var b = await Create("B", a);
        var c = await Create("C", b);

        // A under C closes the loop A → B → C → A.
        var cycle = await TeamEndpoints.Update(a, new SaveTeamRequest("A", null, null, c, null), w.Tenant, admin, cache, default);
        Assert.Contains("cycle", Detail(cycle.Result));

        var d = await Create("D", c);
        var e = await Create("E", d);
        var tooDeep = await TeamEndpoints.Create(new SaveTeamRequest("F", null, null, e, null), w.Tenant, admin, cache, default);
        Assert.Contains("levels deep", Detail(tooDeep.Result));

        Assert.True(cache.Invalidations >= 5);
    }

    // ── Contacts, opportunities and dependent records ────────────────────────────────

    [Fact]
    public async Task Owner_scoped_opportunities_hide_their_quotes_and_reassignment_works_across_scopes()
    {
        var w = await SeedAsync("opp");
        await SetScopeAsync(w, Role.TeleCaller.Id, VisibilityRecordType.Opportunity, VisibilityScope.Own);

        Guid bobDeal;
        await using (var db = Raw(w))
        {
            var stages = await OpportunityStageSeed.EnsureAsync(db, w.TenantId);
            var contact = Contact.Create(w.TenantId, "C", "1", "c@t.test");
            db.Contacts.Add(contact);
            await db.SaveChangesAsync();
            var deal = Opportunity.Create(w.TenantId, "Bob's deal", contact.Id, DateTime.UtcNow, stages["Qualification"]);
            deal.AssignOwner(w.Bob);
            deal.SetAmount(100m);
            db.Opportunities.Add(deal);
            await db.SaveChangesAsync();
            db.Quotes.Add(Quote.CreateDraft(deal, 1, "Q-", "Offer", "C", null, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(9),
                null, null, w.Bob, DateTime.UtcNow));
            await db.SaveChangesAsync();
            bobDeal = deal.Id;
        }

        using (RecordVisibility.Enter(await VisibilityOf(w, w.Alice)))
        {
            Assert.Empty((await ListQuotesEndpoint.Handle(bobDeal, w.Tenant, _factory, default)).Value!);
            Assert.IsType<NotFound>((await RecordOwnerEndpoints.SetOpportunityOwner(bobDeal, new SetOwnerRequest(w.Alice), w.Tenant, _factory, default)).Result);
        }

        // Bob hands the deal to Carol — a user whose records Bob cannot see — and then loses
        // sight of it himself: an intended outcome, reported as a 404, never silent data loss.
        using (RecordVisibility.Enter(await VisibilityOf(w, w.Bob)))
        {
            Assert.IsType<Ok<OwnerResponse>>((await RecordOwnerEndpoints.SetOpportunityOwner(bobDeal, new SetOwnerRequest(w.Carol), w.Tenant, _factory, default)).Result);
            Assert.IsType<NotFound>((await RecordOwnerEndpoints.SetOpportunityOwner(bobDeal, new SetOwnerRequest(w.Bob), w.Tenant, _factory, default)).Result);
        }

        using (RecordVisibility.Enter(await VisibilityOf(w, w.Carol)))
            Assert.Single((await ListQuotesEndpoint.Handle(bobDeal, w.Tenant, _factory, default)).Value!);
    }

    [Fact]
    public async Task Assignment_pickers_list_every_user_under_a_narrowed_scope()
    {
        var w = await SeedAsync("picker");
        await SetScopeAsync(w, Role.TeleCaller.Id, VisibilityRecordType.Lead, VisibilityScope.Own);

        using (RecordVisibility.Enter(await VisibilityOf(w, w.Alice)))
        {
            var users = (await AssignableUsersEndpoint.Handle(w.Tenant, _factory, default)).Value!;
            Assert.Equal(4, users.Count);
        }
    }

    // ── Privilege escalation ─────────────────────────────────────────────────────────

    [Fact]
    public async Task A_user_cannot_grant_or_assign_permissions_they_do_not_hold()
    {
        var w = await SeedAsync("esc");
        var admin = new FixedUser(w.Admin);

        // Admin holds users:write but not settings:write.
        var settingsWrite = Permission.SettingsWrite.Id;
        var create = await CreateRoleEndpoint.Handle(new CreateRoleEndpoint.Request("Configurer", [settingsWrite]),
            w.Tenant, _factory, admin, default);
        Assert.Contains("do not hold", Detail(create.Result));

        // Editing the Admin role to add it is refused the same way.
        await using (var db = Raw(w))
        {
            var current = await db.Roles.Where(r => r.Id == Role.Admin.Id).SelectMany(r => r.Permissions).Select(p => p.Id).ToListAsync();
            var update = await UpdateRoleEndpoint.Handle(Role.Admin.Id, new UpdateRoleEndpoint.Request("Admin", [.. current, settingsWrite]),
                w.Tenant, _factory, Cache(), admin, new NoCache(), default);
            Assert.Contains("do not hold", Detail(update.Result));

            // And the Admin role cannot drop user management, which would lock the tenant out.
            var withoutUsers = current.Where(id => id != Permission.UsersWrite.Id).ToList();
            var lockout = await UpdateRoleEndpoint.Handle(Role.Admin.Id, new UpdateRoleEndpoint.Request("Admin", withoutUsers),
                w.Tenant, _factory, Cache(), admin, new NoCache(), default);
            Assert.Contains("must keep", Detail(lockout.Result));
        }

        // A role holding something Admin lacks cannot be assigned by Admin.
        await using (var db = Raw(w))
        {
            Assert.NotNull(await PrivilegeGuard.CheckCanAssignRoleAsync(db, w.Admin, Role.SuperAdmin.Id, default));
            Assert.Null(await PrivilegeGuard.CheckCanAssignRoleAsync(db, w.Admin, Role.TeleCaller.Id, default));
            // A TeleCaller holds no users:* at all, so it cannot hand itself the Admin role.
            Assert.NotNull(await PrivilegeGuard.CheckCanAssignRoleAsync(db, w.Alice, Role.Admin.Id, default));
        }
    }

    [Fact]
    public async Task A_role_scope_cannot_be_widened_beyond_the_actors_own()
    {
        var w = await SeedAsync("scopewide");
        await SetScopeAsync(w, Role.Admin.Id, VisibilityRecordType.Lead, VisibilityScope.Team);

        var r = await SetRoleScopesEndpoint.Handle(Role.TeleCaller.Id,
            new SetRoleScopesRequest(new() { ["Lead"] = "All" }), w.Tenant, _factory, new FixedUser(w.Admin), new NoCache(), default);
        Assert.Contains("wider scope", Detail(r.Result));

        // Narrowing is always allowed, and is audited.
        var ok = await SetRoleScopesEndpoint.Handle(Role.TeleCaller.Id,
            new SetRoleScopesRequest(new() { ["Lead"] = "Own" }), w.Tenant, _factory, new FixedUser(w.Admin), new NoCache(), default);
        Assert.Equal("Own", ((Ok<RoleScopesResponse>)ok.Result).Value!.Scopes["Lead"]);

        await using var db = Raw(w);
        Assert.Contains(await db.ActivityLogs.ToListAsync(), a => a.EventType == "RoleScopesChanged" && a.ActorId == w.Admin);
    }

    [Fact]
    public async Task SuperAdmin_and_admin_access_stay_out_of_reach_of_every_tenant_role()
    {
        var w = await SeedAsync("platform");
        await using var db = Raw(w);

        // Seed-data invariant: only the platform role holds admin:access.
        var holders = await db.RolePermissions.Where(rp => rp.PermissionId == Permission.AdminAccess.Id).Select(rp => rp.RoleId).ToListAsync();
        Assert.Equal([Role.SuperAdmin.Id], holders);

        // A role cannot be created holding it, even by a user who (hypothetically) held it.
        Assert.False(TenantRoleRules.IsGrantableByTenant(PermissionConstants.AdminAccess));
        Assert.Null(await CreateRoleEndpoint.ResolvePermissionsAsync(db, [Permission.AdminAccess.Id], default));
        Assert.False(TenantRoleRules.IsVisibleToTenant(Role.SuperAdmin.Id));
        Assert.IsType<NotFound>((await SetRoleScopesEndpoint.Handle(Role.SuperAdmin.Id, new SetRoleScopesRequest([]),
            w.Tenant, _factory, new FixedUser(w.Admin), new NoCache(), default)).Result);
    }

    [Fact]
    public async Task Existing_roles_keep_their_crm_access_after_the_upgrade()
    {
        var w = await SeedAsync("seed");
        await using var db = Raw(w);

        foreach (var role in new[] { Role.Owner.Id, Role.TeleCaller.Id, Role.Admin.Id })
        {
            var names = await db.Roles.Where(r => r.Id == role).SelectMany(r => r.Permissions).Select(p => p.Name).ToListAsync();
            Assert.Contains(PermissionConstants.LeadsRead, names);
            Assert.Contains(PermissionConstants.LeadsWrite, names);
            Assert.Contains(PermissionConstants.OpportunitiesWrite, names);
        }
    }

    private static IronMonkey.ApiService.Common.Cache.ICacheService Cache() =>
        new IronMonkey.ApiService.Common.Cache.CacheService(new Microsoft.Extensions.Caching.Distributed.MemoryDistributedCache(
            Microsoft.Extensions.Options.Options.Create(new Microsoft.Extensions.Caching.Memory.MemoryDistributedCacheOptions())));

    private static string Detail(IResult? result) => result switch
    {
        ValidationError e => ((HttpValidationProblemDetails)e.Value!).Detail!,
        _ => throw new Xunit.Sdk.XunitException($"Expected a validation refusal, got {result?.GetType().Name}")
    };
}
