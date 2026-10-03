using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.BackgroundJobs;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Features.Commerce;
using IronMonkey.ApiService.Features.Communications.Webhooks;
using IronMonkey.Data;
using IronMonkey.Data.Entities;
using IronMonkey.Data.Presentation;

namespace IronMonkey.ApiService.Features.Quotes;

/// <summary>
/// The customer-facing quote page reached through a share link. <b>Unauthenticated,
/// attacker-reachable surface</b>, so:
///
/// <list type="bullet">
/// <item>The tenant comes from the HMAC routing token in the path, never from anything the
///   visitor supplies in a body or query string.</item>
/// <item>The quote comes from the SHA-256 of the share token, looked up in that tenant's
///   database only. Nothing else is looked up — no opportunity, contact, user or other
///   quote — so the page cannot be steered to expose other tenant data.</item>
/// <item>Unknown tenant, unknown token, expired and revoked links all render the same page
///   with the same 404, so the endpoint is not an oracle for which tokens exist.</item>
/// <item>A restrictive CSP, <c>no-referrer</c> (the token is in the URL and must not leak to
///   a logo host), <c>no-store</c>, and <c>noindex</c> on every response.</item>
/// </list>
/// </summary>
public class PublicQuoteEndpoints : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/q/{routingToken}/{token}", View)
            .AllowAnonymous()
            .ExcludeFromDescription();

        // A plain HTML form post. Antiforgery is disabled deliberately: the share token in the
        // URL is itself the unguessable secret, and a cross-site attacker who held it could
        // simply open the page.
        app.MapPost("/q/{routingToken}/{token}", Respond)
            .AllowAnonymous()
            .DisableAntiforgery()
            .ExcludeFromDescription();
    }

    private const int MaxNameLength = 200;
    private const int MaxNoteLength = 2000;

    internal static async Task<IResult> View(
        string routingToken, string token, HttpContext http,
        IWebhookTenantResolver tenantResolver, ITenantRegistry tenantRegistry,
        ITenantDbContextFactory dbContextFactory, IQuoteService quotes, CentralDbContext centralDb,
        ITenantCommerceContextResolver commerce, TimeProvider timeProvider, CancellationToken ct)
    {
        Harden(http);

        var resolved = await ResolveAsync(routingToken, token, tenantResolver, tenantRegistry, dbContextFactory, quotes, timeProvider, ct);
        if (resolved is null) return Unavailable();

        var (tenantId, db, link, quote) = resolved.Value;
        await using (db)
        {
            var context = await commerce.ResolveAsync(tenantId, ct);
            var now = timeProvider.GetUtcNow().UtcDateTime;

            quote.ExpireIfPastValidity(context.TodayAt(now), now);
            link.RecordView(now);
            await db.SaveChangesAsync(ct);

            return await RenderAsync(quote, tenantId, centralDb, http, notice: null, ct);
        }
    }

    internal static async Task<IResult> Respond(
        string routingToken, string token, HttpContext http,
        [FromForm] string? decision, [FromForm] string? name, [FromForm] string? note,
        IWebhookTenantResolver tenantResolver, ITenantRegistry tenantRegistry,
        ITenantDbContextFactory dbContextFactory, IQuoteService quotes, CentralDbContext centralDb,
        TimeProvider timeProvider, CancellationToken ct)
    {
        Harden(http);

        var resolved = await ResolveAsync(routingToken, token, tenantResolver, tenantRegistry, dbContextFactory, quotes, timeProvider, ct);
        if (resolved is null) return Unavailable();

        var (tenantId, db, _, quote) = resolved.Value;
        await using (db)
        {
            var trimmedName = name?.Trim() ?? string.Empty;
            if (trimmedName.Length is 0 or > MaxNameLength || decision is not ("accept" or "reject"))
                return await RenderAsync(quote, tenantId, centralDb, http, "Please enter your name and choose accept or decline.", ct);

            var trimmedNote = note?.Trim();
            if (trimmedNote?.Length > MaxNoteLength) trimmedNote = trimmedNote[..MaxNoteLength];

            var op = await quotes.RespondAsync(db, tenantId, quote, decision == "accept",
                userId: null, trimmedName, trimmedNote, ct);

            var notice = op.Succeeded
                ? (decision == "accept" ? "Thank you — your acceptance has been recorded." : "Thank you — your response has been recorded.")
                : op.Error;

            return await RenderAsync(quote, tenantId, centralDb, http, notice, ct);
        }
    }

    private static async Task<(Guid TenantId, TenantDbContext Db, QuoteShareLink Link, Quote Quote)?> ResolveAsync(
        string routingToken, string token,
        IWebhookTenantResolver tenantResolver, ITenantRegistry tenantRegistry,
        ITenantDbContextFactory dbContextFactory, IQuoteService quotes, TimeProvider timeProvider, CancellationToken ct)
    {
        // Cheap shape checks first, so garbage never costs a tenant scan or a DB connection.
        if (string.IsNullOrWhiteSpace(token) || token.Length is < 40 or > 64) return null;

        var tenantId = await tenantResolver.ResolveAsync(routingToken, ct);
        if (tenantId is null) return null;

        var connectionString = await tenantRegistry.GetConnectionStringAsync(tenantId.Value, ct);
        var db = dbContextFactory.CreateForTenant(connectionString, tenantId.Value);

        var hash = QuoteShareLink.Hash(token);
        var link = await db.QuoteShareLinks.SingleOrDefaultAsync(l => l.TokenHash == hash, ct);
        var now = timeProvider.GetUtcNow().UtcDateTime;

        if (link is null || !link.IsUsableAt(now))
        {
            await db.DisposeAsync();
            return null;
        }

        var quote = await quotes.LoadAsync(db, link.QuoteId, ct);
        if (quote is null || quote.Status is QuoteStatus.Draft)
        {
            await db.DisposeAsync();
            return null;
        }

        return (tenantId.Value, db, link, quote);
    }

    private static async Task<IResult> RenderAsync(Quote quote, Guid tenantId, CentralDbContext centralDb,
        HttpContext http, string? notice, CancellationToken ct)
    {
        var tenant = await centralDb.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tenantId, ct);

        // Only an open offer gets the response form; everything else is read-only.
        var action = quote.Status == QuoteStatus.Sent ? http.Request.Path.ToString() : null;

        var html = QuoteDocumentRenderer.Render(quote, tenant?.Presentation?.Branding, tenant?.Name ?? string.Empty,
            TenantFormatting.From(tenant?.Presentation?.Locale), action, notice);

        return Results.Content(html, "text/html; charset=utf-8");
    }

    private static IResult Unavailable() =>
        Results.Content(QuoteDocumentRenderer.RenderUnavailable(), "text/html; charset=utf-8", statusCode: StatusCodes.Status404NotFound);

    private static void Harden(HttpContext http)
    {
        var headers = http.Response.Headers;
        headers["Content-Security-Policy"] =
            "default-src 'none'; style-src 'unsafe-inline'; img-src https:; form-action 'self'; frame-ancestors 'none'; base-uri 'none'";
        headers["Referrer-Policy"] = "no-referrer";
        headers["Cache-Control"] = "no-store";
        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Robots-Tag"] = "noindex, nofollow";
    }
}
