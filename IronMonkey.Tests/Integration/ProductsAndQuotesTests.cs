using System.Reflection;
using IronMonkey.ApiService.BackgroundJobs;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.ApiService.Common.Results;
using IronMonkey.ApiService.Features.Commerce;
using IronMonkey.ApiService.Features.Communications.Webhooks;
using IronMonkey.ApiService.Features.Leads.Workflow.Rules;
using IronMonkey.ApiService.Features.Quotes;
using IronMonkey.ApiService.Features.Reports.Dashboard;
using IronMonkey.ApiService.Features.Reports.Revenue;
using IronMonkey.Common.Auth;
using IronMonkey.Data;
using IronMonkey.Data.Commerce;
using IronMonkey.Data.Entities;
using IronMonkey.Tests.Fixtures;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace IronMonkey.Tests.Integration;

/// <summary>
/// Catalog, line items, quotes and deal economics against a real tenant database, through
/// the real handlers and the real <see cref="QuoteService"/>.
/// </summary>
[Collection("Integration")]
public class ProductsAndQuotesTests(PostgreSqlFixture fixture) : IClassFixture<PostgreSqlFixture>
{
    private readonly TenantDbContextFactory _factory = new();
    private static readonly DateTime Now = new(2026, 10, 3, 12, 0, 0, DateTimeKind.Utc);

    // ── Fakes ────────────────────────────────────────────────────────────────────────

    internal sealed class FixedTenantService(Guid tenantId, string connectionString) : ITenantService
    {
        public Guid GetCurrentTenantId() => tenantId;
        public Task<string> GetConnectionStringAsync(CancellationToken cancellationToken = default) => Task.FromResult(connectionString);
    }

    internal sealed class FixedClock(DateTime utcNow) : TimeProvider
    {
        public DateTime UtcNow { get; set; } = utcNow;
        public override DateTimeOffset GetUtcNow() => new(UtcNow, TimeSpan.Zero);
    }

    /// <summary>Deterministic routing tokens; the real derivation is HMAC and is covered by
    /// the webhook tests.</summary>
    internal sealed class FakeTenantRouting(params Guid[] tenants) : IWebhookTenantResolver
    {
        public string TokenFor(Guid tenantId) => "route" + tenantId.ToString("N");
        public Task<Guid?> ResolveAsync(string? routingToken, CancellationToken cancellationToken) =>
            Task.FromResult<Guid?>(tenants.FirstOrDefault(t => TokenFor(t) == routingToken) is var id && id != Guid.Empty ? id : null);
    }

    internal sealed class FakeRegistry(Dictionary<Guid, string> connections) : ITenantRegistry
    {
        public Task<string> GetConnectionStringAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult(connections[tenantId]);
        public Task<IReadOnlyList<(Guid TenantId, string ConnectionString)>> GetAllProvisionedTenantsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<(Guid, string)>>(connections.Select(kv => (kv.Key, kv.Value)).ToList());
    }

    internal sealed class FixedUserContext(Guid userId) : IUserContext
    {
        public Guid UserId => userId;
        public string IdentityId => string.Empty;
        public Guid TenantId => Guid.Empty;
        public string? ActAs => null;
    }

    internal sealed class RecordingWorkflow : IWorkflowTriggerDispatcher
    {
        public List<(Guid LeadId, WorkflowTrigger Trigger)> Dispatched { get; } = [];
        public void Dispatch(Guid tenantId, Guid leadId, WorkflowTrigger trigger) => Dispatched.Add((leadId, trigger));
    }

    private sealed record Tenant(Guid Id, string ConnectionString, FixedTenantService Service);

    private async Task<Tenant> CreateTenantAsync(string label)
    {
        var tenantId = Guid.NewGuid();
        var connectionString = fixture.ConnectionString.Replace("ironmonkey_test", $"pq_{label}_{Guid.NewGuid():N}");
        await using var db = _factory.CreateForTenant(connectionString, tenantId);
        await db.Database.MigrateAsync();
        return new Tenant(tenantId, connectionString, new FixedTenantService(tenantId, connectionString));
    }

    private TenantDbContext Db(Tenant t) => _factory.CreateForTenant(t.ConnectionString, t.Id);

    private static QuoteService Quotes(FixedClock clock, string? baseCurrency = "GBP", IWorkflowTriggerDispatcher? workflow = null,
        IWebhookTenantResolver? routing = null) =>
        new(clock, new FixedCommerceContextResolver(baseCurrency), routing ?? new FakeTenantRouting(),
            NullLogger<QuoteService>.Instance, messageDispatcher: null, workflowDispatcher: workflow);

    private static async Task<(Guid DealId, Dictionary<string, Guid> Stages)> SeedDealAsync(TenantDbContext db, Guid tenantId, string stage = "Qualification")
    {
        var stages = await OpportunityStageSeed.EnsureAsync(db, tenantId);
        var contact = Contact.Create(tenantId, "Jane Buyer", "555", $"jane{Guid.NewGuid():N}@t.test");
        db.Contacts.Add(contact);
        await db.SaveChangesAsync();

        var deal = Opportunity.Create(tenantId, "Fleet renewal", contact.Id, Now.AddDays(10), stages[stage]);
        db.Opportunities.Add(deal);
        await db.SaveChangesAsync();
        return (deal.Id, stages);
    }

    private static async Task<Product> SeedProductAsync(TenantDbContext db, Guid tenantId, string code, decimal price,
        ChargeType chargeType = ChargeType.OneOff, string? category = "Vehicles", DateTime? effectiveFrom = null)
    {
        var product = Product.Create(tenantId, code, code + " product", chargeType,
            chargeType == ChargeType.Recurring ? BillingFrequency.Monthly : BillingFrequency.None, chargeType == ChargeType.Recurring ? 12 : 1);
        product.Update(code, code + " product", null, category, chargeType,
            chargeType == ChargeType.Recurring ? BillingFrequency.Monthly : BillingFrequency.None,
            chargeType == ChargeType.Recurring ? 12 : 1, null, 0m);
        db.Products.Add(product);

        var list = await db.PriceLists.FirstOrDefaultAsync(p => p.IsDefault) ?? db.PriceLists.Local.FirstOrDefault(p => p.IsDefault);
        if (list is null)
        {
            list = PriceList.Create(tenantId, "Standard", null, true);
            db.PriceLists.Add(list);
        }

        db.ProductPrices.Add(ProductPrice.Create(tenantId, product.Id, list.Id, price, price * 0.6m, effectiveFrom ?? Now.AddDays(-30)));
        await db.SaveChangesAsync();
        return product;
    }

    private static T Ok<T>(Results<Ok<T>, ValidationError, NotFound> result) =>
        result.Result is Ok<T> ok ? ok.Value! : throw new Xunit.Sdk.XunitException($"Expected Ok, got {result.Result?.GetType().Name}: {(result.Result as ValidationError)?.Value}");

    private static string Refusal<T>(Results<Ok<T>, ValidationError, NotFound> result) =>
        result.Result is ValidationError error
            ? ((Microsoft.AspNetCore.Http.HttpValidationProblemDetails)error.Value!).Detail!
            : throw new Xunit.Sdk.XunitException($"Expected a refusal, got {result.Result?.GetType().Name}");

    private Task<Results<Ok<DealEconomicsResponse>, ValidationError, NotFound>> AddLine(Tenant t, Guid dealId, AddLineRequest request,
        FixedClock clock, string? baseCurrency = "GBP") =>
        AddLineItemEndpoint.Handle(dealId, request, t.Service, _factory, new FixedCommerceContextResolver(baseCurrency), clock, CancellationToken.None);

    // ── Line items and deal value ────────────────────────────────────────────────────

    [Fact]
    public async Task A_deal_built_from_catalog_and_free_text_lines_totals_exactly_what_its_quote_says()
    {
        var t = await CreateTenantAsync("total");
        var clock = new FixedClock(Now);
        Guid dealId;
        Product vehicle, finance;
        await using (var db = Db(t))
        {
            (dealId, _) = await SeedDealAsync(db, t.Id);
            vehicle = await SeedProductAsync(db, t.Id, "VEH", 28000m);
            finance = await SeedProductAsync(db, t.Id, "FIN", 349.99m, ChargeType.Recurring, "Finance");
        }

        Ok(await AddLine(t, dealId, new AddLineRequest(vehicle.Id, null, null, 1m, null, null, 5m, 20m, null, null), clock));
        Ok(await AddLine(t, dealId, new AddLineRequest(finance.Id, null, null, 1m, null, 48, null, null, null, null), clock));
        var economics = Ok(await AddLine(t, dealId,
            new AddLineRequest(null, null, "Custom wrap (not in catalog)", 1m, 333.335m, null, null, null, null, null), clock));

        // 28000 − 5% = 26600, +20% tax = 31920; 349.99 × 48 = 16799.52; 333.335 → 333.34.
        Assert.Equal(31920m + 16799.52m + 333.34m, economics.Totals.Total);
        Assert.Equal(16799.52m, economics.Totals.RecurringTotal);
        Assert.Equal(31920m + 333.34m, economics.Totals.OneOffTotal);

        await using (var db = Db(t))
        {
            var stored = await db.Opportunities.SingleAsync(o => o.Id == dealId);
            Assert.Equal(economics.Totals.Total, stored.Amount);
        }

        var quotes = Quotes(clock);
        await using (var db = Db(t))
        {
            var op = await quotes.CreateDraftAsync(db, t.Id, dealId, Guid.NewGuid(), null, null, null, CancellationToken.None);
            Assert.True(op.Succeeded, op.Error);
            Assert.Equal(economics.Totals.Total, op.Quote!.Total);
            Assert.Equal(economics.Totals.RecurringTotal, op.Quote.RecurringTotal);
            Assert.Equal(economics.Lines.Select(l => l.TotalAmount), op.Quote.Lines.OrderBy(l => l.Position).Select(l => l.TotalAmount));
        }
    }

    [Fact]
    public async Task A_line_priced_in_another_currency_is_refused_rather_than_relabelled()
    {
        var t = await CreateTenantAsync("ccy");
        var clock = new FixedClock(Now);
        Guid dealId, productId, euroListId;
        await using (var db = Db(t))
        {
            (dealId, _) = await SeedDealAsync(db, t.Id);
            var product = await SeedProductAsync(db, t.Id, "VEH", 28000m);
            productId = product.Id;
            var euro = PriceList.Create(t.Id, "Eurozone", "EUR", false);
            db.PriceLists.Add(euro);
            db.ProductPrices.Add(ProductPrice.Create(t.Id, product.Id, euro.Id, 32000m, null, Now.AddDays(-1)));
            await db.SaveChangesAsync();
            euroListId = euro.Id;
        }

        var refusal = Refusal(await AddLine(t, dealId, new AddLineRequest(productId, euroListId, null, 1m, null, null, null, null, null, null), clock));
        Assert.Contains("EUR", refusal);

        // Moving the deal into EUR first makes the same line legitimate.
        Ok(await SetDealEconomicsEndpoint.Handle(dealId, new SetDealEconomicsRequest("EUR", 0.85m, null, euroListId),
            t.Service, _factory, new FixedCommerceContextResolver("GBP"), CancellationToken.None));
        var economics = Ok(await AddLine(t, dealId, new AddLineRequest(productId, null, null, 1m, null, null, null, null, null, null), clock));
        Assert.Equal(32000m, economics.Totals.Total);
        Assert.Equal("EUR", economics.CurrencyCode);
    }

    [Fact]
    public async Task A_lump_sum_amount_cannot_overwrite_a_deal_computed_from_catalog_lines()
    {
        var t = await CreateTenantAsync("lump");
        var clock = new FixedClock(Now);
        Guid dealId, productId;
        Dictionary<string, Guid> stages;
        await using (var db = Db(t))
        {
            (dealId, stages) = await SeedDealAsync(db, t.Id);
            productId = (await SeedProductAsync(db, t.Id, "VEH", 1000m)).Id;
        }
        Ok(await AddLine(t, dealId, new AddLineRequest(productId, null, null, 1m, null, null, null, null, null, null), clock));

        Guid contactId;
        await using (var db = Db(t))
            contactId = (await db.Opportunities.SingleAsync(o => o.Id == dealId)).ContactId;

        var result = await IronMonkey.ApiService.Features.Opportunities.UpdateOpportunityEndpoint.Handle(
            dealId, new("Fleet renewal", contactId, stages["Qualification"], 5m, Now, null),
            t.Service, _factory, new FixedUserContext(Guid.NewGuid()), CancellationToken.None);

        Assert.IsType<ValidationError>(result.Result);
        await using var verify = Db(t);
        Assert.Equal(1000m, (await verify.Opportunities.SingleAsync(o => o.Id == dealId)).Amount);
    }

    // ── Price versioning ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_price_change_creates_a_version_and_never_alters_a_sent_quote()
    {
        var t = await CreateTenantAsync("ver");
        var clock = new FixedClock(Now);
        var quotes = Quotes(clock);
        Guid dealId, productId, quoteId;
        await using (var db = Db(t))
        {
            (dealId, _) = await SeedDealAsync(db, t.Id);
            productId = (await SeedProductAsync(db, t.Id, "VEH", 28000m)).Id;
        }
        Ok(await AddLine(t, dealId, new AddLineRequest(productId, null, null, 1m, null, null, null, null, null, null), clock));

        await using (var db = Db(t))
        {
            var op = await quotes.CreateDraftAsync(db, t.Id, dealId, Guid.NewGuid(), null, null, null, CancellationToken.None);
            quoteId = op.Quote!.Id;
            var sent = await quotes.SendAsync(db, t.Id, quoteId, Guid.NewGuid(), "https://crm.test", false, CancellationToken.None);
            Assert.True(sent.Operation.Succeeded, sent.Operation.Error);
        }

        // Raise the price from tomorrow.
        await using (var db = Db(t))
        {
            var list = await db.PriceLists.SingleAsync(p => p.IsDefault);
            var (_, error) = await PriceResolver.AddVersionAsync(db, t.Id, productId, list.Id, 30000m, null, Now.AddDays(1), CancellationToken.None);
            Assert.Null(error);
            await db.SaveChangesAsync();

            // Back-dating before the current version is refused: that window is history.
            var (_, backdated) = await PriceResolver.AddVersionAsync(db, t.Id, productId, list.Id, 1m, null, Now.AddDays(-60), CancellationToken.None);
            Assert.NotNull(backdated);
        }

        await using (var db = Db(t))
        {
            var versions = await db.ProductPrices.Where(p => p.ProductId == productId).OrderBy(p => p.EffectiveFrom).ToListAsync();
            Assert.Equal(2, versions.Count);
            Assert.Equal(28000m, versions[0].UnitPrice);            // the old row is untouched...
            Assert.Equal(Now.AddDays(1), versions[0].EffectiveTo);  // ...only closed

            var today = await PriceResolver.ResolveAsync(db, productId, null, null, Now, CancellationToken.None);
            var nextWeek = await PriceResolver.ResolveAsync(db, productId, null, null, Now.AddDays(7), CancellationToken.None);
            Assert.Equal(28000m, today!.Price.UnitPrice);
            Assert.Equal(30000m, nextWeek!.Price.UnitPrice);

            var quote = await quotes.LoadAsync(db, quoteId, CancellationToken.None);
            Assert.Equal(28000m, quote!.Total);
            Assert.Equal(28000m, Assert.Single(quote.Lines).UnitPrice);
        }

        // The deal line keeps its price too; only a NEW line picks up the new version.
        clock.UtcNow = Now.AddDays(2);
        var economics = Ok(await AddLine(t, dealId, new AddLineRequest(productId, null, null, 1m, null, null, null, null, null, null), clock));
        Assert.Equal([28000m, 30000m], economics.Lines.Select(l => l.UnitPrice));
    }

    [Fact]
    public async Task Price_precedence_is_line_list_then_deal_list_then_default()
    {
        var t = await CreateTenantAsync("prec");
        await using var db = Db(t);
        var product = await SeedProductAsync(db, t.Id, "SKU", 100m);
        var fleet = PriceList.Create(t.Id, "Fleet", null, false);
        var partner = PriceList.Create(t.Id, "Partner", null, false);
        db.PriceLists.AddRange(fleet, partner);
        db.ProductPrices.Add(ProductPrice.Create(t.Id, product.Id, fleet.Id, 90m, null, Now.AddDays(-1)));
        db.ProductPrices.Add(ProductPrice.Create(t.Id, product.Id, partner.Id, 80m, null, Now.AddDays(-1)));
        await db.SaveChangesAsync();

        Assert.Equal(100m, (await PriceResolver.ResolveAsync(db, product.Id, null, null, Now, default))!.Price.UnitPrice);
        Assert.Equal(90m, (await PriceResolver.ResolveAsync(db, product.Id, null, fleet.Id, Now, default))!.Price.UnitPrice);
        // The line's explicit list beats the deal's — even though the deal's is not cheaper.
        var resolved = await PriceResolver.ResolveAsync(db, product.Id, partner.Id, fleet.Id, Now, default);
        Assert.Equal(80m, resolved!.Price.UnitPrice);
        Assert.Equal(PriceSource.LineOverride, resolved.Source);

        // A list with no price for the product falls through to the next level.
        var empty = PriceList.Create(t.Id, "Empty", null, false);
        db.PriceLists.Add(empty);
        await db.SaveChangesAsync();
        Assert.Equal(90m, (await PriceResolver.ResolveAsync(db, product.Id, empty.Id, fleet.Id, Now, default))!.Price.UnitPrice);
    }

    // ── Quote immutability and versions ──────────────────────────────────────────────

    [Fact]
    public async Task A_sent_quote_cannot_be_edited_and_a_revision_supersedes_it_with_both_in_history()
    {
        var t = await CreateTenantAsync("immut");
        var clock = new FixedClock(Now);
        var quotes = Quotes(clock);
        Guid dealId, v1Id, v2Id;
        await using (var db = Db(t))
        {
            (dealId, _) = await SeedDealAsync(db, t.Id);
            var deal = await db.Opportunities.Include(o => o.LineItems).SingleAsync(o => o.Id == dealId);
            deal.SetAmount(5000m);
            await db.SaveChangesAsync();

            v1Id = (await quotes.CreateDraftAsync(db, t.Id, dealId, Guid.NewGuid(), "Offer", null, null, default)).Quote!.Id;
            Assert.True((await quotes.SendAsync(db, t.Id, v1Id, Guid.NewGuid(), "https://crm.test", false, default)).Operation.Succeeded);
        }

        await using (var db = Db(t))
        {
            var v1 = await quotes.LoadAsync(db, v1Id, default);
            Assert.False(v1!.UpdateDraft("Changed", v1.ValidUntil, "new terms").Succeeded);
            Assert.False(v1.RefreshFrom((await db.Opportunities.Include(o => o.LineItems).SingleAsync(o => o.Id == dealId)), null).Succeeded);
            Assert.Equal(StatusCodes.Status409Conflict, (await quotes.RefreshAsync(db, v1Id, default)).StatusCode);
        }

        // Change the deal, then revise: v2 offers the new shape, v1 keeps the old.
        await using (var db = Db(t))
        {
            var deal = await db.Opportunities.Include(o => o.LineItems).SingleAsync(o => o.Id == dealId);
            deal.SetAmount(4500m);
            await db.SaveChangesAsync();

            var revised = await quotes.ReviseAsync(db, t.Id, v1Id, Guid.NewGuid(), default);
            Assert.True(revised.Succeeded, revised.Error);
            v2Id = revised.Quote!.Id;
            Assert.Equal(2, revised.Quote.Version);
            Assert.Equal(v1Id, revised.Quote.SupersedesQuoteId);

            Assert.True((await quotes.SendAsync(db, t.Id, v2Id, Guid.NewGuid(), "https://crm.test", false, default)).Operation.Succeeded);
        }

        await using (var verify = Db(t))
        {
            var both = await verify.Quotes.Include(q => q.StatusChanges).Where(q => q.OpportunityId == dealId).OrderBy(q => q.Version).ToListAsync();
            Assert.Equal(2, both.Count);
            Assert.Equal(both[0].Number, both[1].Number);
            Assert.Equal(QuoteStatus.Superseded, both[0].Status);
            Assert.Equal(v2Id, both[0].SupersededByQuoteId);
            Assert.Equal(5000m, both[0].Total);
            Assert.Equal(QuoteStatus.Sent, both[1].Status);
            Assert.Equal(4500m, both[1].Total);
            Assert.Contains(both[0].StatusChanges, c => c.ToStatus == QuoteStatus.Superseded);

            // A superseded version cannot be accepted.
            var v1 = await quotes.LoadAsync(verify, v1Id, default);
            var accept = await quotes.RespondAsync(verify, t.Id, v1!, true, null, "Jane", null, default);
            Assert.False(accept.Succeeded);
        }
    }

    // ── Approval ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_discount_above_the_threshold_cannot_be_sent_until_approved()
    {
        var t = await CreateTenantAsync("appr");
        var clock = new FixedClock(Now);
        var quotes = Quotes(clock);
        Guid dealId, productId, quoteId;
        await using (var db = Db(t))
        {
            (dealId, _) = await SeedDealAsync(db, t.Id);
            productId = (await SeedProductAsync(db, t.Id, "VEH", 10000m)).Id;
            var settings = QuoteSettings.CreateDefault(t.Id);
            settings.Update("Q-", 30, null, 10m, 14);
            db.QuoteSettings.Add(settings);
            await db.SaveChangesAsync();
        }
        Ok(await AddLine(t, dealId, new AddLineRequest(productId, null, null, 1m, null, null, 15m, null, null, null), clock));

        var approver = Guid.NewGuid();
        await using (var db = Db(t))
        {
            var draft = await quotes.CreateDraftAsync(db, t.Id, dealId, Guid.NewGuid(), null, null, null, default);
            quoteId = draft.Quote!.Id;
            Assert.True(draft.Quote.RequiresApproval);

            var refused = await quotes.SendAsync(db, t.Id, quoteId, Guid.NewGuid(), "https://crm.test", false, default);
            Assert.False(refused.Operation.Succeeded);
            Assert.Equal(StatusCodes.Status409Conflict, refused.Operation.StatusCode);
            Assert.Null(refused.ShareUrl);
        }

        await using (var db = Db(t))
        {
            Assert.Equal(QuoteStatus.Draft, (await db.Quotes.SingleAsync(q => q.Id == quoteId)).Status);
            Assert.True((await quotes.ApproveAsync(db, quoteId, approver, default)).Succeeded);
            var sent = await quotes.SendAsync(db, t.Id, quoteId, Guid.NewGuid(), "https://crm.test", false, default);
            Assert.True(sent.Operation.Succeeded, sent.Operation.Error);
            Assert.Equal(approver, sent.Operation.Quote!.ApprovedByUserId);
        }
    }

    [Fact]
    public async Task Refreshing_an_approved_draft_clears_the_approval()
    {
        var t = await CreateTenantAsync("reappr");
        var clock = new FixedClock(Now);
        var quotes = Quotes(clock);
        Guid dealId, productId, quoteId;
        await using (var db = Db(t))
        {
            (dealId, _) = await SeedDealAsync(db, t.Id);
            productId = (await SeedProductAsync(db, t.Id, "VEH", 10000m)).Id;
            var settings = QuoteSettings.CreateDefault(t.Id);
            settings.Update("Q-", 30, null, 10m, 14);
            db.QuoteSettings.Add(settings);
            await db.SaveChangesAsync();
        }
        Ok(await AddLine(t, dealId, new AddLineRequest(productId, null, null, 1m, null, null, 15m, null, null, null), clock));

        await using (var db = Db(t))
        {
            quoteId = (await quotes.CreateDraftAsync(db, t.Id, dealId, Guid.NewGuid(), null, null, null, default)).Quote!.Id;
            Assert.True((await quotes.ApproveAsync(db, quoteId, Guid.NewGuid(), default)).Succeeded);
            var refreshed = await quotes.RefreshAsync(db, quoteId, default);
            Assert.True(refreshed.Succeeded, refreshed.Error);
            Assert.True(refreshed.Quote!.IsAwaitingApproval);
        }
    }

    [Fact]
    public async Task Approval_is_gated_by_the_quotes_approve_permission_which_only_admin_roles_hold_by_default()
    {
        var t = await CreateTenantAsync("perm");
        await using var db = Db(t);

        // Read from the migrated database: the seed that ships, not a list in the test.
        var granted = await db.RolePermissions
            .Where(rp => rp.PermissionId == Permission.QuotesApprove.Id)
            .Select(rp => rp.RoleId)
            .ToListAsync();

        Assert.Contains(Role.Admin.Id, granted);
        Assert.Contains(Role.SuperAdmin.Id, granted);
        Assert.DoesNotContain(Role.TeleCaller.Id, granted);
        Assert.Contains(PermissionConstants.QuotesApprove, PermissionConstants.ForPlatformRole(RoleConstants.SuperAdmin));

        // Catalog writes must be reachable by a tenant Admin, who does not hold settings:write.
        var catalog = await db.RolePermissions.Where(rp => rp.PermissionId == Permission.CatalogWrite.Id).Select(rp => rp.RoleId).ToListAsync();
        Assert.Contains(Role.Admin.Id, catalog);
        Assert.DoesNotContain(Role.TeleCaller.Id, catalog);
    }

    // ── Workflow events ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Quote_events_fire_the_workflow_engine_on_the_originating_lead()
    {
        var t = await CreateTenantAsync("wf");
        var clock = new FixedClock(Now);
        var workflow = new RecordingWorkflow();
        var quotes = Quotes(clock, workflow: workflow);
        Guid dealId, leadId, quoteId;
        await using (var db = Db(t))
        {
            (dealId, _) = await SeedDealAsync(db, t.Id);
            var deal = await db.Opportunities.Include(o => o.LineItems).SingleAsync(o => o.Id == dealId);
            deal.SetAmount(100m);

            var leadStage = PipelineStage.CreateFor(t.Id, PipelineRecordType.Lead, "New", 1, StageType.Entry);
            db.PipelineStages.Add(leadStage);
            await db.SaveChangesAsync();
            var lead = Lead.Create(t.Id, "Jane", "Buyer", "555", "jane@t.test", LeadSource.Manual, leadStage.Id);
            lead.Convert(null, deal.ContactId, deal.Id);
            db.Leads.Add(lead);
            await db.SaveChangesAsync();
            leadId = lead.Id;

            quoteId = (await quotes.CreateDraftAsync(db, t.Id, dealId, Guid.NewGuid(), null, null, null, default)).Quote!.Id;
            await quotes.SendAsync(db, t.Id, quoteId, Guid.NewGuid(), "https://crm.test", false, default);
            var quote = await quotes.LoadAsync(db, quoteId, default);
            await quotes.RespondAsync(db, t.Id, quote!, true, null, "Jane", null, default);
        }

        Assert.Equal([(leadId, WorkflowTrigger.QuoteSent), (leadId, WorkflowTrigger.QuoteAccepted)], workflow.Dispatched);
    }

    // ── Multi-currency reporting ─────────────────────────────────────────────────────

    [Fact]
    public async Task Dashboards_and_revenue_report_exclude_foreign_deals_without_a_rate_and_convert_those_with_one()
    {
        var t = await CreateTenantAsync("fx");
        await using (var db = Db(t))
        {
            var stages = await OpportunityStageSeed.EnsureAsync(db, t.Id);
            var won = stages.Single(kv => kv.Key == "Won").Value;
            var contact = Contact.Create(t.Id, "C", "555", "c@t.test");
            db.Contacts.Add(contact);
            await db.SaveChangesAsync();

            var product = await SeedProductAsync(db, t.Id, "VEH", 1000m);

            Opportunity Deal(string? currency, decimal? rate, decimal value, bool catalog)
            {
                var d = Opportunity.Create(t.Id, "D", contact.Id, Now, won);
                d.SetCurrency(currency);
                d.SetExchangeRate(rate, rate is null ? null : DateOnly.FromDateTime(Now));
                if (catalog)
                    d.AddLine(new LineDetails(product.Id, null, null, product.Name, product.Code, product.Category,
                        1m, value, null, ChargeType.OneOff, BillingFrequency.None, 1, 0m, 0m));
                else d.SetAmount(value);
                return d;
            }

            db.Opportunities.AddRange(
                Deal(null, null, 1000m, true),        // GBP (tenant)
                Deal("EUR", 0.5m, 1000m, true),       // EUR with recorded rate → 500 GBP
                Deal("USD", null, 7777m, false));     // USD with no rate → excluded
            await db.SaveChangesAsync();
        }

        var dashboard = await GetDashboardOpportunitiesEndpoint.Handle(
            "alltime", null, null, "all", t.Service, _factory, PipelineTestHelpers.Scope(), CancellationToken.None,
            new FixedCommerceContextResolver("GBP"));
        var data = dashboard.Ok();
        Assert.Equal(1500m, data.WonValue);
        Assert.Equal(3, data.WonCount);
        Assert.Equal(1, data.UnconvertedCount);
        Assert.Equal(7777m, data.UnconvertedAmounts!["USD"]);

        var report = (await GetRevenueReportEndpoint.Handle("alltime", null, null, "all", t.Service, _factory,
            PipelineTestHelpers.Scope(), new FixedCommerceContextResolver("GBP"), CancellationToken.None)).Ok();
        Assert.Equal(1500m, report.WonRevenue);
        Assert.Equal(1, report.UnconvertedDealCount);
        var vehicle = report.ByProduct.Single(p => p.Code == "VEH");
        Assert.Equal(1500m, vehicle.Revenue);
        Assert.Equal(100m, vehicle.WinRatePercent);
        Assert.Equal("GBP", report.CurrencyCode);
    }

    [Fact]
    public async Task Revenue_report_separates_recurring_from_one_off_and_reports_discount_and_win_rate()
    {
        var t = await CreateTenantAsync("rev");
        await using (var db = Db(t))
        {
            var stages = await OpportunityStageSeed.EnsureAsync(db, t.Id);
            var won = stages.Single(kv => kv.Key == "Won").Value;
            var lost = stages.Single(kv => kv.Key == "Lost").Value;
            var contact = Contact.Create(t.Id, "C", "555", "c@t.test");
            db.Contacts.Add(contact);
            await db.SaveChangesAsync();

            var tuition = await SeedProductAsync(db, t.Id, "TUI", 4000m, ChargeType.Recurring, "Tuition");
            var fee = await SeedProductAsync(db, t.Id, "FEE", 100m, ChargeType.OneOff, "Fees");

            LineDetails Line(Product p, decimal price, int periods, decimal discount) =>
                new(p.Id, null, null, p.Name, p.Code, p.Category, 1m, price, null, p.ChargeType, p.BillingFrequency, periods, discount, 0m);

            var a = Opportunity.Create(t.Id, "A", contact.Id, Now, won);
            a.AddLine(Line(tuition, 4000m, 3, 10m));   // gross 12000, discount 1200 → 10800
            a.AddLine(Line(fee, 100m, 1, 0m));
            var b = Opportunity.Create(t.Id, "B", contact.Id, Now, lost);
            b.AddLine(Line(tuition, 4000m, 3, 0m));
            db.Opportunities.AddRange(a, b);
            await db.SaveChangesAsync();
        }

        var report = (await GetRevenueReportEndpoint.Handle("alltime", null, null, "all", t.Service, _factory,
            PipelineTestHelpers.Scope(), new FixedCommerceContextResolver(null), CancellationToken.None)).Ok();

        Assert.Equal(10900m, report.WonRevenue);
        Assert.Equal(10800m, report.WonRecurringRevenue);
        Assert.Equal(100m, report.WonOneOffRevenue);
        Assert.Equal(1200m, report.WonDiscount);

        var tuitionRow = report.ByProduct.Single(p => p.Code == "TUI");
        Assert.Equal(50m, tuitionRow.WinRatePercent);
        Assert.Equal(1, tuitionRow.WonDeals);
        Assert.Equal(1, tuitionRow.LostDeals);
        Assert.Equal(10m, tuitionRow.DiscountRatePercent);
        Assert.Contains(report.ByCategory, c => c.Category == "Tuition" && c.Revenue == 10800m);
        Assert.Contains(report.RecurringByFrequency, r => r.BillingFrequency == "Monthly");
    }
}
