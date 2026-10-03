using IronMonkey.ApiService.Features.Commerce;
using IronMonkey.ApiService.Features.Quotes;
using IronMonkey.Data;
using IronMonkey.Data.Entities;
using IronMonkey.Tests.Fixtures;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using static IronMonkey.Tests.Integration.ProductsAndQuotesTests;

namespace IronMonkey.Tests.Integration;

/// <summary>
/// The customer quote page is unauthenticated, attacker-reachable surface. These pin that a
/// link is unguessable and stored hashed, resolves only its own quote in its own tenant,
/// stops working when it expires or is revoked, and that every failure looks the same.
/// </summary>
[Collection("Integration")]
public class PublicQuoteLinkTests(PostgreSqlFixture fixture) : IClassFixture<PostgreSqlFixture>
{
    private readonly TenantDbContextFactory _factory = new();
    private static readonly DateTime Now = new(2026, 10, 3, 12, 0, 0, DateTimeKind.Utc);

    private sealed record Setup(Guid TenantId, string ConnectionString, Guid QuoteId, string Token, string ShareUrl,
        FakeTenantRouting Routing, FakeRegistry Registry, FixedClock Clock, QuoteService Quotes);

    private async Task<CentralDbContext> CentralAsync()
    {
        var central = new CentralDbContext(new DbContextOptionsBuilder<CentralDbContext>().UseNpgsql(fixture.ConnectionString).Options);
        await central.Database.MigrateAsync();
        return central;
    }

    private async Task<Guid> NewTenantDbAsync(Dictionary<Guid, string> connections, string label)
    {
        var tenantId = Guid.NewGuid();
        var cs = fixture.ConnectionString.Replace("ironmonkey_test", $"pub_{label}_{Guid.NewGuid():N}");
        await using var db = _factory.CreateForTenant(cs, tenantId);
        await db.Database.MigrateAsync();
        connections[tenantId] = cs;
        return tenantId;
    }

    private async Task<Setup> SendQuoteAsync(string label, Dictionary<Guid, string>? connections = null, Guid? tenantId = null)
    {
        connections ??= new Dictionary<Guid, string>();
        var tid = tenantId ?? await NewTenantDbAsync(connections, label);
        var routing = new FakeTenantRouting([.. connections.Keys]);
        var clock = new FixedClock(Now);
        var quotes = new QuoteService(clock, new FixedCommerceContextResolver("GBP"), routing, NullLogger<QuoteService>.Instance);

        await using var db = _factory.CreateForTenant(connections[tid], tid);
        var stages = await OpportunityStageSeed.EnsureAsync(db, tid);
        var contact = Contact.Create(tid, "Jane Buyer", "555", "jane@t.test");
        db.Contacts.Add(contact);
        await db.SaveChangesAsync();
        var deal = Opportunity.Create(tid, "Secret internal deal title", contact.Id, Now, stages["Qualification"]);
        deal.AddLine(new LineDetails(null, null, null, "Vehicle", null, null, 1m, 20000m, 12345.67m,
            Data.Commerce.ChargeType.OneOff, Data.Commerce.BillingFrequency.None, 1, 0m, 0m));
        db.Opportunities.Add(deal);
        await db.SaveChangesAsync();

        var quoteId = (await quotes.CreateDraftAsync(db, tid, deal.Id, Guid.NewGuid(), "Your vehicle offer", null, null, default)).Quote!.Id;
        var sent = await quotes.SendAsync(db, tid, quoteId, Guid.NewGuid(), "https://crm.test", false, default);
        Assert.True(sent.Operation.Succeeded, sent.Operation.Error);

        var token = sent.ShareUrl!.Split('/').Last();
        return new Setup(tid, connections[tid], quoteId, token, sent.ShareUrl, routing, new FakeRegistry(connections), clock, quotes);
    }

    private static DefaultHttpContext Http(string path)
    {
        var http = new DefaultHttpContext { RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider() };
        http.Request.Path = path;
        http.Response.Body = new MemoryStream();
        return http;
    }

    private static async Task<(int Status, string Body, IHeaderDictionary Headers)> ExecuteAsync(IResult result, HttpContext http)
    {
        await result.ExecuteAsync(http);
        http.Response.Body.Position = 0;
        return (http.Response.StatusCode, await new StreamReader(http.Response.Body).ReadToEndAsync(), http.Response.Headers);
    }

    private async Task<(int Status, string Body, IHeaderDictionary Headers)> ViewAsync(Setup s, string routingToken, string token, FixedClock? clock = null)
    {
        await using var central = await CentralAsync();
        var http = Http($"/q/{routingToken}/{token}");
        var result = await PublicQuoteEndpoints.View(routingToken, token, http, s.Routing, s.Registry, _factory, s.Quotes,
            central, new FixedCommerceContextResolver("GBP"), clock ?? s.Clock, default);
        return await ExecuteAsync(result, http);
    }

    [Fact]
    public async Task A_link_is_unguessable_stored_only_as_a_hash_and_shows_only_that_quote()
    {
        var s = await SendQuoteAsync("view");

        Assert.True(s.Token.Length >= 43); // 256 bits, base64url
        Assert.Equal($"https://crm.test/q/{s.Routing.TokenFor(s.TenantId)}/{s.Token}", s.ShareUrl);

        await using (var db = _factory.CreateForTenant(s.ConnectionString, s.TenantId))
        {
            var link = await db.QuoteShareLinks.SingleAsync(l => l.QuoteId == s.QuoteId);
            Assert.NotEqual(s.Token, link.TokenHash);
            Assert.Equal(QuoteShareLink.Hash(s.Token), link.TokenHash);
            Assert.DoesNotContain(s.Token, link.TokenHash);
        }

        var (status, body, headers) = await ViewAsync(s, s.Routing.TokenFor(s.TenantId), s.Token);

        Assert.Equal(StatusCodes.Status200OK, status);
        Assert.Contains("Your vehicle offer", body);
        Assert.Contains("20,000.00", body);
        // Nothing beyond the quote: not the internal deal title, not unit cost.
        Assert.DoesNotContain("Secret internal deal title", body);
        Assert.DoesNotContain("12,345.67", body);
        Assert.Equal("no-referrer", headers["Referrer-Policy"].ToString());
        Assert.Equal("no-store", headers["Cache-Control"].ToString());
        Assert.Contains("default-src 'none'", headers["Content-Security-Policy"].ToString());
    }

    [Fact]
    public async Task Expired_revoked_unknown_and_cross_tenant_links_all_fail_identically()
    {
        var connections = new Dictionary<Guid, string>();
        var a = await NewTenantDbAsync(connections, "ta");
        var b = await NewTenantDbAsync(connections, "tb");
        var s = await SendQuoteAsync("fail", connections, a);
        var unavailable = QuoteDocumentRenderer.RenderUnavailable();

        // A tenant's valid token under another tenant's routing token resolves nothing.
        var crossTenant = await ViewAsync(s, s.Routing.TokenFor(b), s.Token);
        var unknownToken = await ViewAsync(s, s.Routing.TokenFor(a), new string('x', 43));
        var unknownTenant = await ViewAsync(s, "nope", s.Token);
        var expired = await ViewAsync(s, s.Routing.TokenFor(a), s.Token, new FixedClock(Now.AddDays(15)));

        await using (var db = _factory.CreateForTenant(s.ConnectionString, s.TenantId))
        {
            var link = await db.QuoteShareLinks.SingleAsync(l => l.QuoteId == s.QuoteId);
            link.Revoke(Now);
            await db.SaveChangesAsync();
        }
        var revoked = await ViewAsync(s, s.Routing.TokenFor(a), s.Token);

        foreach (var (status, body, _) in new[] { crossTenant, unknownToken, unknownTenant, expired, revoked })
        {
            Assert.Equal(StatusCodes.Status404NotFound, status);
            Assert.Equal(unavailable, body);
        }
    }

    [Fact]
    public async Task A_customer_can_accept_through_the_link_but_not_after_the_quote_expires()
    {
        var s = await SendQuoteAsync("accept");
        await using var central = await CentralAsync();

        // Past validity: the response is refused and the quote is recorded as expired.
        var late = new FixedClock(Now.AddDays(31));
        await using (var db = _factory.CreateForTenant(s.ConnectionString, s.TenantId))
        {
            var quote = await s.Quotes.LoadAsync(db, s.QuoteId, default);
            var lateQuotes = new QuoteService(late, new FixedCommerceContextResolver("GBP"), s.Routing, NullLogger<QuoteService>.Instance);
            var refused = await lateQuotes.RespondAsync(db, s.TenantId, quote!, true, null, "Jane", null, default);
            Assert.False(refused.Succeeded);
        }

        await using (var db = _factory.CreateForTenant(s.ConnectionString, s.TenantId))
            Assert.Equal(QuoteStatus.Expired, (await db.Quotes.SingleAsync(q => q.Id == s.QuoteId)).Status);

        // A fresh quote, accepted in time through the public form.
        var fresh = await SendQuoteAsync("accept2");
        var http = Http($"/q/{fresh.Routing.TokenFor(fresh.TenantId)}/{fresh.Token}");
        var result = await PublicQuoteEndpoints.Respond(fresh.Routing.TokenFor(fresh.TenantId), fresh.Token, http,
            "accept", "Jane Buyer", "Looks good", fresh.Routing, fresh.Registry, _factory, fresh.Quotes, central, fresh.Clock, default);
        var (status, body, _) = await ExecuteAsync(result, http);

        Assert.Equal(StatusCodes.Status200OK, status);
        Assert.Contains("acceptance has been recorded", body);
        Assert.DoesNotContain("<form", body); // no longer open for a second response

        await using var verify = _factory.CreateForTenant(fresh.ConnectionString, fresh.TenantId);
        var accepted = await verify.Quotes.Include(q => q.StatusChanges).SingleAsync(q => q.Id == fresh.QuoteId);
        Assert.Equal(QuoteStatus.Accepted, accepted.Status);
        Assert.Equal("Jane Buyer", accepted.RespondedByName);
        Assert.Contains(accepted.StatusChanges, c => c.ToStatus == QuoteStatus.Accepted && c.ChangedByName == "Jane Buyer" && c.ChangedByUserId == null);
    }
}
