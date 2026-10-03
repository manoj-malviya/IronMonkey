using IronMonkey.ApiService.Common.Auth;
using IronMonkey.ApiService.Features.Configuration;
using IronMonkey.ApiService.Features.Leads;
using IronMonkey.ApiService.Features.Opportunities;
using IronMonkey.ApiService.Features.Pipelines;
using IronMonkey.ApiService.Features.Reports.Dashboard;
using IronMonkey.Data;
using IronMonkey.Data.Entities;
using IronMonkey.Tests.Fixtures;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace IronMonkey.Tests.Integration;

/// <summary>
/// Multiple named pipelines: scoping, membership, precedence and removal.
///
/// The failure this file mostly exists to prevent is the silent-wrong-number one — a tenant
/// with two pipelines seeing one pipeline's count presented as the tenant total. That is
/// invisible in the UI and wrong in a way nobody notices until a decision is made on it, so
/// the aggregates are asserted against hand-counted figures rather than against each other.
/// </summary>
[Collection("Integration")]
public class MultiplePipelinesTests(PostgreSqlFixture fixture) : IClassFixture<PostgreSqlFixture>
{
    private readonly TenantDbContextFactory _factory = new();
    private readonly PipelineScopeResolver _scope = new();

    private sealed class FixedTenantService(Guid tenantId, string connectionString) : ITenantService
    {
        public Guid GetCurrentTenantId() => tenantId;

        public Task<string> GetConnectionStringAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(connectionString);
    }

    private sealed class FixedUserContext(Guid userId) : IUserContext
    {
        public Guid UserId => userId;
        public string IdentityId => string.Empty;
        public Guid TenantId => Guid.NewGuid();
        public string? ActAs => null;
    }

    private async Task<string> CreateDbAsync(Guid tenantId, string label)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var connectionString = fixture.ConnectionString.Replace("ironmonkey_test", $"multipipe_{label}_{suffix}");
        await using var db = _factory.CreateForTenant(connectionString, tenantId);
        await db.Database.MigrateAsync();
        return connectionString;
    }

    private static async Task<Contact> SeedContactAsync(TenantDbContext db, Guid tenantId, string name)
    {
        var contact = Contact.Create(tenantId, name, "555", $"{Guid.NewGuid():N}@t.com");
        db.Contacts.Add(contact);
        await db.SaveChangesAsync();
        return contact;
    }

    // ── Cross-pipeline isolation of aggregates ──────────────────────────────

    [Fact]
    public async Task Dashboard_opportunity_figures_cover_one_pipeline_and_say_which()
    {
        var tenantId = Guid.NewGuid();
        var connectionString = await CreateDbAsync(tenantId, "aggisolate");

        await using var db = _factory.CreateForTenant(connectionString, tenantId);

        // The tenant's original funnel, created the way every existing fixture does — through
        // a plain stage save, which attaches it to the default pipeline.
        var salesEntry = PipelineStage.CreateFor(tenantId, PipelineRecordType.Opportunity, "Qualification", 1, StageType.Entry);
        var salesWon = PipelineStage.CreateFor(tenantId, PipelineRecordType.Opportunity, "Won", 2, StageType.ClosedWon);
        db.PipelineStages.AddRange(salesEntry, salesWon);
        await db.SaveChangesAsync();

        var (service, serviceStages) = await PipelineTestHelpers.CreatePipelineAsync(
            db, tenantId, PipelineRecordType.Opportunity, "Service bookings",
            [("Booked", StageType.Entry), ("Completed", StageType.ClosedWon)]);

        var contact = await SeedContactAsync(db, tenantId, "Buyer");

        // Sales: 1 deal worth 1000. Service: 2 deals worth 250 total.
        var sales = Opportunity.Create(tenantId, "Car", contact.Id, DateTime.UtcNow, salesEntry.Id);
        sales.SetAmount(1000m);

        var svc1 = Opportunity.Create(tenantId, "Service A", contact.Id, DateTime.UtcNow, serviceStages[0].Id);
        svc1.SetAmount(100m);
        var svc2 = Opportunity.Create(tenantId, "Service B", contact.Id, DateTime.UtcNow, serviceStages[0].Id);
        svc2.SetAmount(150m);

        db.Opportunities.AddRange(sales, svc1, svc2);
        await db.SaveChangesAsync();

        var tenant = new FixedTenantService(tenantId, connectionString);

        // ── Default (no pipelineId) ──────────────────────────────────────────
        // Absence resolves to ONE pipeline, never to all of them. If it widened, this figure
        // would be 1250 under a heading naming a single funnel.
        var defaultResult = (await GetDashboardOpportunitiesEndpoint.Handle(
            DashboardDateRange.AllTime, null, null, null,
            tenant, _factory, _scope, CancellationToken.None)).Ok();

        Assert.Equal(1, defaultResult.TotalCount);
        Assert.Equal(1000m, defaultResult.TotalValue);
        Assert.False(defaultResult.IsTenantWide);
        Assert.True(defaultResult.IsMultiPipeline);

        // ── The second pipeline, named explicitly ────────────────────────────
        var serviceResult = (await GetDashboardOpportunitiesEndpoint.Handle(
            DashboardDateRange.AllTime, null, null, service.Id.ToString(),
            tenant, _factory, _scope, CancellationToken.None)).Ok();

        Assert.Equal(2, serviceResult.TotalCount);
        Assert.Equal(250m, serviceResult.TotalValue);
        Assert.Equal("Service bookings", serviceResult.ScopeLabel);

        // ── The tenant total, asked for deliberately ─────────────────────────
        var allResult = (await GetDashboardOpportunitiesEndpoint.Handle(
            DashboardDateRange.AllTime, null, null, "all",
            tenant, _factory, _scope, CancellationToken.None)).Ok();

        Assert.Equal(3, allResult.TotalCount);
        Assert.Equal(1250m, allResult.TotalValue);
        Assert.True(allResult.IsTenantWide);
        Assert.Equal("All pipelines", allResult.ScopeLabel);

        // And it is broken down, so the reader can see what the total is made of rather than
        // being handed one number whose composition is invisible.
        Assert.Equal(2, allResult.ByPipeline.Count);
        Assert.Equal(1250m, allResult.ByPipeline.Sum(p => p.TotalValue));
        Assert.Equal(250m, allResult.ByPipeline.Single(p => p.PipelineId == service.Id).TotalValue);
    }

    [Fact]
    public async Task Lead_list_total_covers_only_the_scoped_pipeline()
    {
        var tenantId = Guid.NewGuid();
        var connectionString = await CreateDbAsync(tenantId, "leadscope");

        await using var db = _factory.CreateForTenant(connectionString, tenantId);

        var admissionsEntry = PipelineStage.Create(tenantId, "Enquiry", 1, StageType.Entry);
        db.PipelineStages.Add(admissionsEntry);
        await db.SaveChangesAsync();

        var (exec, execStages) = await PipelineTestHelpers.CreatePipelineAsync(
            db, tenantId, PipelineRecordType.Lead, "Executive education",
            [("Interest", StageType.Entry), ("Enrolled", StageType.ClosedWon)]);

        for (var i = 0; i < 3; i++)
            db.Leads.Add(Lead.Create(tenantId, "A", $"{i}", "1", $"a{i}@t.com", LeadSource.Manual, admissionsEntry.Id));

        for (var i = 0; i < 5; i++)
            db.Leads.Add(Lead.Create(tenantId, "E", $"{i}", "1", $"e{i}@t.com", LeadSource.Manual, execStages[0].Id));

        await db.SaveChangesAsync();

        var tenant = new FixedTenantService(tenantId, connectionString);

        var defaultPage = (await ListLeadsEndpoint.Handle(
            null, null, null, null, null, null, tenant, _factory, _scope, CancellationToken.None)).Ok();

        // "1–3 of 3" and not "of 8". The total is counted over the same scope the rows come
        // from, so the count on screen describes the list on screen.
        Assert.Equal(3, defaultPage.TotalCount);
        Assert.Equal(3, defaultPage.Items.Count);
        Assert.True(defaultPage.IsMultiPipeline);

        var execPage = (await ListLeadsEndpoint.Handle(
            null, null, null, null, null, exec.Id.ToString(),
            tenant, _factory, _scope, CancellationToken.None)).Ok();

        Assert.Equal(5, execPage.TotalCount);
        Assert.Equal("Executive education", execPage.ScopeLabel);

        var allPage = (await ListLeadsEndpoint.Handle(
            null, null, null, null, null, "all", tenant, _factory, _scope, CancellationToken.None)).Ok();

        Assert.Equal(8, allPage.TotalCount);
        Assert.True(allPage.IsTenantWide);
    }

    [Fact]
    public async Task An_unknown_pipeline_id_is_refused_rather_than_widened()
    {
        // A typo'd or stale id that fell back to "all" would present the tenant total under a
        // pipeline's name — the exact silent-wrong-number failure, arrived at by accident.
        var tenantId = Guid.NewGuid();
        var connectionString = await CreateDbAsync(tenantId, "badscope");

        await using var db = _factory.CreateForTenant(connectionString, tenantId);
        db.PipelineStages.Add(PipelineStage.Create(tenantId, "New", 1, StageType.Entry));
        await db.SaveChangesAsync();

        var tenant = new FixedTenantService(tenantId, connectionString);

        var result = await ListLeadsEndpoint.Handle(
            null, null, null, null, null, Guid.NewGuid().ToString(),
            tenant, _factory, _scope, CancellationToken.None);

        Assert.Contains("does not exist", result.BadRequest());
    }

    [Fact]
    public async Task A_retired_pipeline_id_still_resolves()
    {
        // A pipeline can be deactivated while records remain in it. Resolving its id must
        // keep working, or a tenant loses every way of reaching those records — and the
        // scope resolver looks up a named pipeline without filtering on IsActive for
        // exactly this reason.
        var tenantId = Guid.NewGuid();
        var connectionString = await CreateDbAsync(tenantId, "retired");

        await using var db = _factory.CreateForTenant(connectionString, tenantId);
        db.PipelineStages.Add(PipelineStage.Create(tenantId, "New", 1, StageType.Entry));
        await db.SaveChangesAsync();

        var retired = Pipeline.Create(tenantId, PipelineRecordType.Lead, "Retired", order: 2);
        retired.SetActive(false);
        db.Pipelines.Add(retired);
        await db.SaveChangesAsync();

        var tenant = new FixedTenantService(tenantId, connectionString);

        var page = (await ListLeadsEndpoint.Handle(
            null, null, null, null, null, retired.Id.ToString(),
            tenant, _factory, _scope, CancellationToken.None)).Ok();

        Assert.Equal(retired.Id, page.PipelineId);
        Assert.Empty(page.Items);
    }

    [Fact]
    public async Task A_lead_pipeline_id_is_not_accepted_by_an_opportunity_endpoint()
    {
        var tenantId = Guid.NewGuid();
        var connectionString = await CreateDbAsync(tenantId, "wrongtype");

        await using var db = _factory.CreateForTenant(connectionString, tenantId);
        db.PipelineStages.Add(PipelineStage.Create(tenantId, "New", 1, StageType.Entry));
        db.PipelineStages.Add(PipelineStage.CreateFor(
            tenantId, PipelineRecordType.Opportunity, "Qualification", 1, StageType.Entry));
        await db.SaveChangesAsync();

        var leadPipeline = await PipelineTestHelpers.DefaultPipelineAsync(db, PipelineRecordType.Lead);
        var tenant = new FixedTenantService(tenantId, connectionString);

        var result = await GetDashboardOpportunitiesEndpoint.Handle(
            DashboardDateRange.AllTime, null, null, leadPipeline.Id.ToString(),
            tenant, _factory, _scope, CancellationToken.None);

        // Refused, not answered with an empty board that reads as "no deals".
        Assert.Contains("does not exist", result.BadRequest());
    }

    // ── Moving a record between pipelines ───────────────────────────────────

    [Fact]
    public async Task Moving_a_lead_lands_it_on_a_stage_of_the_destination_pipeline()
    {
        var tenantId = Guid.NewGuid();
        var connectionString = await CreateDbAsync(tenantId, "moveland");

        await using var db = _factory.CreateForTenant(connectionString, tenantId);

        var originStage = PipelineStage.Create(tenantId, "Enquiry", 1, StageType.Entry);
        db.PipelineStages.Add(originStage);
        await db.SaveChangesAsync();

        var (target, targetStages) = await PipelineTestHelpers.CreatePipelineAsync(
            db, tenantId, PipelineRecordType.Lead, "Service",
            [("Booked", StageType.Entry), ("Done", StageType.ClosedWon)]);

        var lead = Lead.Create(tenantId, "Move", "Me", "1", "m@t.com", LeadSource.Manual, originStage.Id);
        db.Leads.Add(lead);
        await db.SaveChangesAsync();

        var originPipelineId = lead.PipelineId;
        var actor = Guid.NewGuid();

        var result = await MoveRecordPipelineEndpoint.HandleLead(
            lead.Id, new MoveRecordPipelineEndpoint.Request(target.Id),
            new FixedTenantService(tenantId, connectionString), _factory,
            new FixedUserContext(actor), CancellationToken.None);

        var ok = Assert.IsType<Ok<MoveRecordPipelineEndpoint.Response>>(result.Result).Value!;

        await using var fresh = _factory.CreateForTenant(connectionString, tenantId);
        var moved = await fresh.Leads.SingleAsync(l => l.Id == lead.Id);

        // Both values changed together. The stage it held belongs to the pipeline it left and
        // is never carried over — a record pointing at another pipeline's stage shows on no
        // board and is counted in the wrong pipeline's totals.
        Assert.Equal(target.Id, moved.PipelineId);
        Assert.Equal(targetStages[0].Id, moved.PipelineStageId);
        Assert.Equal(targetStages[0].Id, ok.StageId);

        // And the stage it landed on really is one of the destination's.
        var landing = await fresh.PipelineStages.SingleAsync(s => s.Id == moved.PipelineStageId);
        Assert.Equal(target.Id, landing.PipelineId);

        // History records the jump as a pipeline change, so it is legible rather than reading
        // as an inexplicable move between two stages that were never adjacent.
        var change = await fresh.StageChanges.SingleAsync(c => c.RecordId == lead.Id);
        Assert.Equal(originPipelineId, change.FromPipelineId);
        Assert.Equal(target.Id, change.ToPipelineId);
        Assert.True(change.IsPipelineChange);
        Assert.Equal(actor, change.ChangedByUserId);
    }

    [Fact]
    public async Task Moving_to_a_stage_from_another_pipeline_is_refused()
    {
        // The central correctness risk, asserted directly: naming a stage that belongs
        // somewhere else must be a refusal, never a quiet substitution.
        var tenantId = Guid.NewGuid();
        var connectionString = await CreateDbAsync(tenantId, "moverefuse");

        await using var db = _factory.CreateForTenant(connectionString, tenantId);

        var originStage = PipelineStage.Create(tenantId, "Enquiry", 1, StageType.Entry);
        db.PipelineStages.Add(originStage);
        await db.SaveChangesAsync();

        var (target, _) = await PipelineTestHelpers.CreatePipelineAsync(
            db, tenantId, PipelineRecordType.Lead, "Service", [("Booked", StageType.Entry)]);

        var lead = Lead.Create(tenantId, "Move", "Me", "1", "m2@t.com", LeadSource.Manual, originStage.Id);
        db.Leads.Add(lead);
        await db.SaveChangesAsync();

        var result = await MoveRecordPipelineEndpoint.HandleLead(
            lead.Id,
            // originStage belongs to the pipeline being LEFT, not to target.
            new MoveRecordPipelineEndpoint.Request(target.Id, originStage.Id),
            new FixedTenantService(tenantId, connectionString), _factory,
            new FixedUserContext(Guid.NewGuid()), CancellationToken.None);

        var bad = Assert.IsType<BadRequest<string>>(result.Result);
        Assert.Contains("does not belong", bad.Value!);

        await using var fresh = _factory.CreateForTenant(connectionString, tenantId);
        var unchanged = await fresh.Leads.SingleAsync(l => l.Id == lead.Id);

        // Nothing moved. A refusal that half-applied would be worse than the bug.
        Assert.Equal(originStage.Id, unchanged.PipelineStageId);
        Assert.NotEqual(target.Id, unchanged.PipelineId);
    }

    [Fact]
    public async Task An_ordinary_stage_move_cannot_reach_another_pipelines_stage()
    {
        // The easiest way to hit the central correctness risk is a board drag or a form save
        // carrying a stage id from the wrong funnel. Both go through the ordinary stage-move
        // path, so that path refuses it rather than trusting the UI to send the right id.
        var tenantId = Guid.NewGuid();
        var connectionString = await CreateDbAsync(tenantId, "stageguard");

        await using var db = _factory.CreateForTenant(connectionString, tenantId);

        var ownStage = PipelineStage.Create(tenantId, "Enquiry", 1, StageType.Entry);
        var ownSecond = PipelineStage.Create(tenantId, "Contacted", 2);
        db.PipelineStages.AddRange(ownStage, ownSecond);
        await db.SaveChangesAsync();

        var (_, otherStages) = await PipelineTestHelpers.CreatePipelineAsync(
            db, tenantId, PipelineRecordType.Lead, "Other", [("Foreign", StageType.Entry)]);

        var lead = Lead.Create(tenantId, "Guard", "Me", "1", "g@t.com", LeadSource.Manual, ownStage.Id);
        db.Leads.Add(lead);
        await db.SaveChangesAsync();

        // Sanity: the two stages really are in different pipelines.
        Assert.NotEqual(lead.PipelineId, otherStages[0].PipelineId);

        // A stage of the lead's own pipeline resolves; a foreign one does not.
        Assert.NotNull(await PipelineStageResolution.FindInPipelineAsync(
            db, lead.PipelineId, ownSecond.Id, CancellationToken.None));

        Assert.Null(await PipelineStageResolution.FindInPipelineAsync(
            db, lead.PipelineId, otherStages[0].Id, CancellationToken.None));
    }

    [Fact]
    public async Task Moving_a_lead_keeps_its_assignment()
    {
        // A move is an administrative reorganisation, not a new lead. Re-running routing
        // would reassign a record whose owner has been working it.
        var tenantId = Guid.NewGuid();
        var connectionString = await CreateDbAsync(tenantId, "moveassign");

        await using var db = _factory.CreateForTenant(connectionString, tenantId);

        var originStage = PipelineStage.Create(tenantId, "Enquiry", 1, StageType.Entry);
        db.PipelineStages.Add(originStage);
        await db.SaveChangesAsync();

        var (target, _) = await PipelineTestHelpers.CreatePipelineAsync(
            db, tenantId, PipelineRecordType.Lead, "Service", [("Booked", StageType.Entry)]);

        var owner = Guid.NewGuid();
        var lead = Lead.Create(tenantId, "Owned", "Lead", "1", "o@t.com", LeadSource.Manual, originStage.Id);
        lead.AssignTo(owner);
        db.Leads.Add(lead);
        await db.SaveChangesAsync();

        await MoveRecordPipelineEndpoint.HandleLead(
            lead.Id, new MoveRecordPipelineEndpoint.Request(target.Id),
            new FixedTenantService(tenantId, connectionString), _factory,
            new FixedUserContext(Guid.NewGuid()), CancellationToken.None);

        await using var fresh = _factory.CreateForTenant(connectionString, tenantId);
        var moved = await fresh.Leads.SingleAsync(l => l.Id == lead.Id);

        Assert.Equal(owner, moved.AssignedToUserId);
    }

    // ── Targeting precedence ────────────────────────────────────────────────

    [Fact]
    public void A_pipeline_scoped_rule_replaces_the_tenant_wide_one_rather_than_adding_to_it()
    {
        // Unioning them would make a pipeline-scoped rule unable to OVERRIDE anything — it
        // could only ever add — so "scoped to this pipeline" would mean nothing beyond
        // "extra". The whole point of scoping is to be able to say "instead of".
        var pipelineA = Guid.NewGuid();
        var pipelineB = Guid.NewGuid();

        var tenantWide = new { Id = 1, PipelineId = (Guid?)null };
        var scopedToA = new { Id = 2, PipelineId = (Guid?)pipelineA };

        var items = new[] { tenantWide, scopedToA };

        var forA = PipelineTargeting.Resolve(items, pipelineA, i => i.PipelineId);
        Assert.Single(forA);
        Assert.Equal(2, forA[0].Id);

        // A pipeline with nothing scoped to it falls back to the tenant-wide set — which is
        // exactly the set that applied before pipelines existed.
        var forB = PipelineTargeting.Resolve(items, pipelineB, i => i.PipelineId);
        Assert.Single(forB);
        Assert.Equal(1, forB[0].Id);
    }

    [Fact]
    public void Custom_fields_are_additive_rather_than_overriding()
    {
        // A field is captured data, not a behaviour. Hiding the tenant-wide fields because a
        // pipeline added one of its own would make values already stored unreachable.
        var pipelineA = Guid.NewGuid();
        var pipelineB = Guid.NewGuid();

        var items = new[]
        {
            new { Id = 1, PipelineId = (Guid?)null },
            new { Id = 2, PipelineId = (Guid?)pipelineA },
            new { Id = 3, PipelineId = (Guid?)pipelineB }
        };

        var forA = PipelineTargeting.ResolveAdditive(items, pipelineA, i => i.PipelineId);

        Assert.Equal([1, 2], forA.Select(i => i.Id).OrderBy(i => i));
    }

    // ── Removing a pipeline ─────────────────────────────────────────────────

    [Fact]
    public async Task Removing_a_pipeline_moves_its_records_and_widens_its_scoped_rules()
    {
        var tenantId = Guid.NewGuid();
        var connectionString = await CreateDbAsync(tenantId, "removepipe");

        await using var db = _factory.CreateForTenant(connectionString, tenantId);

        var keepStage = PipelineStage.Create(tenantId, "Enquiry", 1, StageType.Entry);
        db.PipelineStages.Add(keepStage);
        await db.SaveChangesAsync();

        var keepPipeline = await PipelineTestHelpers.DefaultPipelineAsync(db, PipelineRecordType.Lead);

        var (doomed, doomedStages) = await PipelineTestHelpers.CreatePipelineAsync(
            db, tenantId, PipelineRecordType.Lead, "Retiring", [("Only", StageType.Entry)]);

        var lead = Lead.Create(tenantId, "Stranded", "Lead", "1", "s@t.com", LeadSource.Manual, doomedStages[0].Id);
        db.Leads.Add(lead);

        var scopedRule = WorkflowRule.Create(
            tenantId, "Scoped rule", WorkflowTrigger.StatusChange, "{}", "{}", doomed.Id);
        db.WorkflowRules.Add(scopedRule);
        await db.SaveChangesAsync();

        var tenant = new FixedTenantService(tenantId, connectionString);

        // Without a reassignment target the request is refused rather than stranding records.
        var refused = await DeletePipelineEndpoint.Handle(
            doomed.Id, null, tenant, _factory, new FixedUserContext(Guid.NewGuid()), CancellationToken.None);

        Assert.IsType<Conflict<string>>(refused.Result);

        var result = await DeletePipelineEndpoint.Handle(
            doomed.Id, keepPipeline.Id, tenant, _factory,
            new FixedUserContext(Guid.NewGuid()), CancellationToken.None);

        var ok = Assert.IsType<Ok<DeletePipelineEndpoint.Response>>(result.Result).Value!;
        Assert.Equal(1, ok.ReassignedRecords);
        Assert.Equal(1, ok.WidenedRules);

        await using var fresh = _factory.CreateForTenant(connectionString, tenantId);

        var moved = await fresh.Leads.SingleAsync(l => l.Id == lead.Id);
        Assert.Equal(keepPipeline.Id, moved.PipelineId);

        // Landed on the surviving pipeline's OWN entry stage, never on the stage it held in
        // the pipeline that was removed.
        Assert.Equal(keepStage.Id, moved.PipelineStageId);

        // The rule reverted to tenant-wide rather than being deleted. Deleting it would
        // destroy configuration nobody asked to lose; leaving it dangling would make it match
        // nothing, which is deletion without saying so.
        var rule = await fresh.WorkflowRules.SingleAsync(r => r.Id == scopedRule.Id);
        Assert.Null(rule.PipelineId);

        // History naming the removed pipeline is kept — what pipeline a record was in is a
        // fact about the past that removing configuration today must not rewrite.
        Assert.True(await fresh.StageChanges.AnyAsync(c => c.ToPipelineId == keepPipeline.Id));
    }

    [Fact]
    public async Task The_default_pipeline_cannot_be_removed()
    {
        // Removing it would leave the record type with no answer for "no pipeline
        // specified", which every existing caller depends on.
        var tenantId = Guid.NewGuid();
        var connectionString = await CreateDbAsync(tenantId, "removedefault");

        await using var db = _factory.CreateForTenant(connectionString, tenantId);
        db.PipelineStages.Add(PipelineStage.Create(tenantId, "New", 1, StageType.Entry));
        await db.SaveChangesAsync();

        await PipelineTestHelpers.CreatePipelineAsync(
            db, tenantId, PipelineRecordType.Lead, "Second", [("Only", StageType.Entry)]);

        var defaultPipeline = await PipelineTestHelpers.DefaultPipelineAsync(db, PipelineRecordType.Lead);

        var result = await DeletePipelineEndpoint.Handle(
            defaultPipeline.Id, null, new FixedTenantService(tenantId, connectionString), _factory,
            new FixedUserContext(Guid.NewGuid()), CancellationToken.None);

        var conflict = Assert.IsType<Conflict<string>>(result.Result);
        Assert.Contains("default pipeline", conflict.Value!);
    }

    // ── The single-pipeline tenant is unchanged ─────────────────────────────

    [Fact]
    public async Task A_single_pipeline_tenant_is_never_told_it_has_a_choice()
    {
        // The hard acceptance criterion. IsMultiPipeline is what every UI surface reads to
        // decide whether to render a picker, so false here means no picker anywhere — and an
        // omitted pipelineId resolves to the only pipeline, so nothing else changes either.
        var tenantId = Guid.NewGuid();
        var connectionString = await CreateDbAsync(tenantId, "singlepipe");

        await using var db = _factory.CreateForTenant(connectionString, tenantId);

        var stage = PipelineStage.Create(tenantId, "New", 1, StageType.Entry);
        db.PipelineStages.Add(stage);
        db.Leads.Add(Lead.Create(tenantId, "Only", "Lead", "1", "only@t.com", LeadSource.Manual, stage.Id));
        await db.SaveChangesAsync();

        var tenant = new FixedTenantService(tenantId, connectionString);

        var pipelines = (await ListPipelinesEndpoint.Handle(
            "Lead", null, null, tenant, _factory, CancellationToken.None)).Ok();

        Assert.False(pipelines.IsMultiPipeline);
        Assert.Single(pipelines.Items);

        var page = (await ListLeadsEndpoint.Handle(
            null, null, null, null, null, null, tenant, _factory, _scope, CancellationToken.None)).Ok();

        // Same total it would have had before pipelines existed, and the UI is told not to
        // render a picker.
        Assert.Equal(1, page.TotalCount);
        Assert.False(page.IsMultiPipeline);
        Assert.False(page.IsTenantWide);

        var summary = (await GetDashboardSummaryEndpoint.Handle(
            DashboardDateRange.AllTime, null, null, null,
            tenant, _factory, _scope, CancellationToken.None)).Ok();

        Assert.Equal(1, summary.TotalLeads);
        Assert.False(summary.IsMultiPipeline);
    }

    [Fact]
    public async Task Two_pipelines_may_each_have_a_stage_of_the_same_name()
    {
        // Stage name uniqueness is per pipeline, not per record type. Scoping it wider would
        // make a second pipeline unable to reuse any name the first had taken — and "New" is
        // the obvious first stage of almost any funnel.
        var tenantId = Guid.NewGuid();
        var connectionString = await CreateDbAsync(tenantId, "samename");

        await using var db = _factory.CreateForTenant(connectionString, tenantId);
        db.PipelineStages.Add(PipelineStage.Create(tenantId, "Qualified", 1, StageType.Entry));
        await db.SaveChangesAsync();

        var (second, secondStages) = await PipelineTestHelpers.CreatePipelineAsync(
            db, tenantId, PipelineRecordType.Lead, "Second", [("Qualified", StageType.Entry)]);

        Assert.Equal("Qualified", secondStages[0].Name);
        Assert.Equal(second.Id, secondStages[0].PipelineId);

        // Both exist, in different pipelines.
        Assert.Equal(2, await db.PipelineStages.CountAsync(s => s.Name == "Qualified"));
    }

    [Fact]
    public async Task The_last_active_stage_guard_is_per_pipeline()
    {
        // Counting active stages across the whole record type would let a second pipeline's
        // stages keep THIS pipeline's last stage from ever looking like the last one — so the
        // guard would never fire and a pipeline could be left with nowhere to put a record.
        var tenantId = Guid.NewGuid();
        var connectionString = await CreateDbAsync(tenantId, "lastactive");

        await using var db = _factory.CreateForTenant(connectionString, tenantId);
        db.PipelineStages.Add(PipelineStage.Create(tenantId, "New", 1, StageType.Entry));
        db.PipelineStages.Add(PipelineStage.Create(tenantId, "Contacted", 2));
        await db.SaveChangesAsync();

        var (_, secondStages) = await PipelineTestHelpers.CreatePipelineAsync(
            db, tenantId, PipelineRecordType.Lead, "Second", [("Only", StageType.Entry)]);

        var usage = new ConfigurationUsageService();
        var onlyStageUsage = await usage.GetStageUsageAsync(db, secondStages[0].Id, CancellationToken.None);

        // The second pipeline has exactly one active stage, even though the tenant has three.
        Assert.True(onlyStageUsage.IsOnlyActiveStage);
    }
}
