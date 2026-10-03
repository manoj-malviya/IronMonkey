using System.Net;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Features.Commerce;
using IronMonkey.ApiService.Features.Communications;
using IronMonkey.ApiService.Features.Communications.Webhooks;
using IronMonkey.ApiService.Features.Leads.Workflow.Rules;
using IronMonkey.Data;
using IronMonkey.Data.Communications;
using IronMonkey.Data.Entities;

namespace IronMonkey.ApiService.Features.Quotes;

public sealed record QuoteOperation(Quote? Quote, string? Error, int StatusCode = StatusCodes.Status200OK)
{
    public bool Succeeded => Error is null;
    public static QuoteOperation Ok(Quote quote) => new(quote, null);
    public static QuoteOperation Invalid(string error) => new(null, error, StatusCodes.Status400BadRequest);
    public static QuoteOperation Conflict(string error) => new(null, error, StatusCodes.Status409Conflict);
    public static QuoteOperation NotFound() => new(null, "Quote not found.", StatusCodes.Status404NotFound);
}

/// <param name="ShareUrl">The plain share link — returned once, never stored.</param>
public sealed record SendQuoteOutcome(QuoteOperation Operation, string? ShareUrl, DispatchOutcome? Delivery);

public interface IQuoteService
{
    Task<QuoteOperation> CreateDraftAsync(TenantDbContext db, Guid tenantId, Guid opportunityId, Guid userId,
        string? title, DateOnly? validUntil, string? terms, CancellationToken ct);
    Task<QuoteOperation> ReviseAsync(TenantDbContext db, Guid tenantId, Guid quoteId, Guid userId, CancellationToken ct);
    Task<QuoteOperation> RefreshAsync(TenantDbContext db, Guid quoteId, CancellationToken ct);
    Task<QuoteOperation> ApproveAsync(TenantDbContext db, Guid quoteId, Guid userId, CancellationToken ct);
    Task<SendQuoteOutcome> SendAsync(TenantDbContext db, Guid tenantId, Guid quoteId, Guid userId,
        string publicBaseUrl, bool deliverByEmail, CancellationToken ct);
    Task<QuoteOperation> RespondAsync(TenantDbContext db, Guid tenantId, Quote quote, bool accept,
        Guid? userId, string respondedByName, string? note, CancellationToken ct);
    Task<(QuoteShareLink Link, string Url)> CreateShareLinkAsync(TenantDbContext db, Guid tenantId, Quote quote,
        Guid userId, string publicBaseUrl, CancellationToken ct);
    Task<Quote?> LoadAsync(TenantDbContext db, Guid quoteId, CancellationToken ct);
    Task<QuoteSettings> GetSettingsAsync(TenantDbContext db, Guid tenantId, CancellationToken ct);
    string ShareUrl(Guid tenantId, string token, string publicBaseUrl);
}

/// <summary>
/// The quote lifecycle. Endpoints and tests both go through here, so the rules — immutability,
/// approval, supersession, expiry — live in one place.
///
/// <para>Delivery uses <see cref="IMessageDispatcher"/>, and quote events fire the workflow
/// engine through <see cref="IWorkflowTriggerDispatcher"/> — there is no private notification
/// path. Both are optional so tests can construct the service directly.</para>
/// </summary>
public sealed class QuoteService(
    TimeProvider timeProvider,
    ITenantCommerceContextResolver commerce,
    IWebhookTenantResolver tenantRouting,
    ILogger<QuoteService> logger,
    IMessageDispatcher? messageDispatcher = null,
    IWorkflowTriggerDispatcher? workflowDispatcher = null) : IQuoteService
{
    private DateTime Now => timeProvider.GetUtcNow().UtcDateTime;

    public async Task<Quote?> LoadAsync(TenantDbContext db, Guid quoteId, CancellationToken ct) =>
        await db.Quotes
            .Include(q => q.Lines)
            .Include(q => q.StatusChanges)
            .SingleOrDefaultAsync(q => q.Id == quoteId, ct);

    public async Task<QuoteSettings> GetSettingsAsync(TenantDbContext db, Guid tenantId, CancellationToken ct) =>
        await db.QuoteSettings.FirstOrDefaultAsync(ct) ?? QuoteSettings.CreateDefault(tenantId);

    public async Task<QuoteOperation> CreateDraftAsync(TenantDbContext db, Guid tenantId, Guid opportunityId, Guid userId,
        string? title, DateOnly? validUntil, string? terms, CancellationToken ct)
    {
        var opportunity = await LoadOpportunityAsync(db, opportunityId, ct);
        if (opportunity is null) return QuoteOperation.NotFound();
        if (opportunity.LineItems.Count == 0)
            return QuoteOperation.Invalid("Add at least one line to the deal before generating a quote.");

        var settings = await GetSettingsAsync(db, tenantId, ct);
        var context = await commerce.ResolveAsync(tenantId, ct);
        var validity = validUntil ?? context.TodayAt(Now).AddDays(settings.DefaultValidityDays);
        if (validity < context.TodayAt(Now)) return QuoteOperation.Invalid("A quote cannot be valid until a date in the past.");

        // Number allocation: the next number for the tenant, counting soft-deleted quotes so a
        // number is never reissued. The unique (TenantId, Number, Version) index is the real
        // guard against two concurrent creates; the loser gets a 409 and simply retries.
        var lastNumber = await db.Quotes.IgnoreQueryFilters()
            .Where(q => q.TenantId == tenantId)
            .MaxAsync(q => (int?)q.Number, ct) ?? 0;

        var quote = Quote.CreateDraft(opportunity, lastNumber + 1, settings.NumberPrefix,
            string.IsNullOrWhiteSpace(title) ? opportunity.Title : title,
            opportunity.Contact?.Name ?? "Customer", opportunity.Contact?.Email,
            validity, terms ?? settings.DefaultTerms, settings.ApprovalDiscountThresholdPercent, userId, Now);

        db.Quotes.Add(quote);
        return await SaveAsync(db, quote, ct);
    }

    public async Task<QuoteOperation> ReviseAsync(TenantDbContext db, Guid tenantId, Guid quoteId, Guid userId, CancellationToken ct)
    {
        var current = await LoadAsync(db, quoteId, ct);
        if (current is null) return QuoteOperation.NotFound();
        if (current.Status == QuoteStatus.Draft)
            return QuoteOperation.Invalid("A draft can be edited directly; revise a quote once it has been sent.");
        if (current.Status == QuoteStatus.Accepted)
            return QuoteOperation.Invalid("An accepted quote is final. Generate a new quote for further work.");

        var opportunity = await LoadOpportunityAsync(db, current.OpportunityId, ct);
        if (opportunity is null) return QuoteOperation.NotFound();

        var settings = await GetSettingsAsync(db, tenantId, ct);
        var context = await commerce.ResolveAsync(tenantId, ct);

        var latestVersion = await db.Quotes.IgnoreQueryFilters()
            .Where(q => q.TenantId == tenantId && q.Number == current.Number)
            .MaxAsync(q => q.Version, ct);

        var next = current.Revise(opportunity, latestVersion + 1, settings.ApprovalDiscountThresholdPercent,
            userId, Now, context.TodayAt(Now).AddDays(settings.DefaultValidityDays));

        db.Quotes.Add(next);
        return await SaveAsync(db, next, ct);
    }

    public async Task<QuoteOperation> RefreshAsync(TenantDbContext db, Guid quoteId, CancellationToken ct)
    {
        var quote = await LoadAsync(db, quoteId, ct);
        if (quote is null) return QuoteOperation.NotFound();

        var opportunity = await LoadOpportunityAsync(db, quote.OpportunityId, ct);
        if (opportunity is null) return QuoteOperation.NotFound();

        var settings = await GetSettingsAsync(db, quote.TenantId, ct);

        // Old line rows are removed explicitly: the snapshot replaces the collection, and
        // orphaned quote_lines would otherwise linger with the quote's id.
        db.QuoteLines.RemoveRange(quote.Lines);
        var result = quote.RefreshFrom(opportunity, settings.ApprovalDiscountThresholdPercent);
        if (!result.Succeeded) return QuoteOperation.Conflict(result.Error!);

        return await SaveAsync(db, quote, ct);
    }

    public async Task<QuoteOperation> ApproveAsync(TenantDbContext db, Guid quoteId, Guid userId, CancellationToken ct)
    {
        var quote = await LoadAsync(db, quoteId, ct);
        if (quote is null) return QuoteOperation.NotFound();

        var result = quote.Approve(userId, Now);
        if (!result.Succeeded) return QuoteOperation.Conflict(result.Error!);

        return await SaveAsync(db, quote, ct);
    }

    public async Task<SendQuoteOutcome> SendAsync(TenantDbContext db, Guid tenantId, Guid quoteId, Guid userId,
        string publicBaseUrl, bool deliverByEmail, CancellationToken ct)
    {
        var quote = await LoadAsync(db, quoteId, ct);
        if (quote is null) return new SendQuoteOutcome(QuoteOperation.NotFound(), null, null);

        var context = await commerce.ResolveAsync(tenantId, ct);
        if (quote.ValidUntil < context.TodayAt(Now))
            return new SendQuoteOutcome(QuoteOperation.Invalid("The validity date has passed. Update it before sending."), null, null);

        // Server-side approval enforcement lives in MarkSent: a client that never calls the
        // approve endpoint is refused here, not trusted.
        var result = quote.MarkSent(userId, Now);
        if (!result.Succeeded) return new SendQuoteOutcome(QuoteOperation.Conflict(result.Error!), null, null);

        // Supersession is explicit and covers every other open version of this number, not
        // just the direct predecessor: v1 sent, v2 drafted and abandoned, v3 sent must leave
        // neither v1 nor v2 open for acceptance.
        var others = await db.Quotes
            .Where(q => q.Number == quote.Number && q.Id != quote.Id
                        && (q.Status == QuoteStatus.Sent || q.Status == QuoteStatus.Draft))
            .Include(q => q.StatusChanges)
            .ToListAsync(ct);

        foreach (var other in others)
        {
            // Earlier share links stop working with the version they pointed at.
            foreach (var link in await db.QuoteShareLinks.Where(l => l.QuoteId == other.Id && l.RevokedAt == null).ToListAsync(ct))
                link.Revoke(Now);
            other.MarkSuperseded(quote.Id, userId, Now);
        }

        var (_, shareUrl) = await CreateShareLinkAsync(db, tenantId, quote, userId, publicBaseUrl, ct);

        var saved = await SaveAsync(db, quote, ct);
        if (!saved.Succeeded) return new SendQuoteOutcome(saved, null, null);

        DispatchOutcome? delivery = null;
        if (deliverByEmail && messageDispatcher is not null && !string.IsNullOrWhiteSpace(quote.RecipientEmail))
        {
            var contactId = await db.Opportunities.Where(o => o.Id == quote.OpportunityId)
                .Select(o => (Guid?)o.ContactId).FirstOrDefaultAsync(ct);

            // The key is derived from the quote id alone: a retried send of the same version
            // must not deliver the customer a second copy, and nothing optional feeds it.
            delivery = await messageDispatcher.QueueAsync(db, new SendMessageCommand(
                tenantId, MessageChannel.Email, quote.RecipientEmail!,
                $"Quote {quote.DisplayNumberWithVersion}: {quote.Title}",
                BuildEmailBody(quote, shareUrl, context),
                IdempotencyKey: $"quote:{quote.Id}:sent",
                ContactId: contactId, SentByUserId: userId), ct);
        }

        await PublishAsync(db, tenantId, quote, WorkflowTrigger.QuoteSent, ct);
        return new SendQuoteOutcome(saved, shareUrl, delivery);
    }

    public async Task<QuoteOperation> RespondAsync(TenantDbContext db, Guid tenantId, Quote quote, bool accept,
        Guid? userId, string respondedByName, string? note, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(respondedByName))
            return QuoteOperation.Invalid("A name is required to respond to a quote.");

        var context = await commerce.ResolveAsync(tenantId, ct);

        // Expiry is checked at the moment of response, so an expired quote can never be
        // accepted even if nothing has swept it yet.
        if (quote.ExpireIfPastValidity(context.TodayAt(Now), Now))
        {
            await db.SaveChangesAsync(ct);
            return QuoteOperation.Conflict("This quote has expired.");
        }

        var result = accept
            ? quote.Accept(userId, respondedByName, note, Now)
            : quote.Reject(userId, respondedByName, note, Now);
        if (!result.Succeeded) return QuoteOperation.Conflict(result.Error!);

        var saved = await SaveAsync(db, quote, ct);
        if (saved.Succeeded)
            await PublishAsync(db, tenantId, quote, accept ? WorkflowTrigger.QuoteAccepted : WorkflowTrigger.QuoteRejected, ct);
        return saved;
    }

    public async Task<(QuoteShareLink Link, string Url)> CreateShareLinkAsync(TenantDbContext db, Guid tenantId, Quote quote,
        Guid userId, string publicBaseUrl, CancellationToken ct)
    {
        var settings = await GetSettingsAsync(db, tenantId, ct);
        var (link, token) = QuoteShareLink.Create(tenantId, quote.Id, Now.AddDays(settings.ShareLinkLifetimeDays), userId);
        db.QuoteShareLinks.Add(link);
        return (link, ShareUrl(tenantId, token, publicBaseUrl));
    }

    /// <summary>
    /// The public URL. The tenant is selected by the HMAC routing token in the path — the
    /// same derivation webhooks use — never by anything a visitor could alter to reach another
    /// tenant; the share token then selects exactly one quote within it.
    /// </summary>
    public string ShareUrl(Guid tenantId, string token, string publicBaseUrl) =>
        $"{publicBaseUrl.TrimEnd('/')}/q/{tenantRouting.TokenFor(tenantId)}/{token}";

    private static async Task<Opportunity?> LoadOpportunityAsync(TenantDbContext db, Guid opportunityId, CancellationToken ct) =>
        await db.Opportunities
            .Include(o => o.LineItems)
            .Include(o => o.Contact)
            .SingleOrDefaultAsync(o => o.Id == opportunityId, ct);

    private static async Task<QuoteOperation> SaveAsync(TenantDbContext db, Quote quote, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return QuoteOperation.Ok(quote);
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
        {
            return QuoteOperation.Conflict("Another change to this quote landed first. Reload and try again.");
        }
    }

    /// <summary>
    /// Fires the quote event into the workflow engine. The engine evaluates rules against a
    /// lead, so the event is raised on the lead this deal was converted from; a deal created
    /// directly has no lead and the event is logged rather than invented a subject for.
    /// </summary>
    private async Task PublishAsync(TenantDbContext db, Guid tenantId, Quote quote, WorkflowTrigger trigger, CancellationToken ct)
    {
        if (workflowDispatcher is null) return;

        var leadId = await db.Leads
            .Where(l => l.ConvertedOpportunityId == quote.OpportunityId)
            .Select(l => (Guid?)l.Id)
            .FirstOrDefaultAsync(ct);

        if (leadId is null)
        {
            logger.LogInformation(
                "Quote {QuoteId} {Trigger}: opportunity {OpportunityId} has no originating lead, so no workflow rules were evaluated",
                quote.Id, trigger, quote.OpportunityId);
            return;
        }

        workflowDispatcher.Dispatch(tenantId, leadId.Value, trigger);
    }

    private static string BuildEmailBody(Quote quote, string shareUrl, TenantCommerceContext context)
    {
        string E(string? v) => WebUtility.HtmlEncode(v ?? string.Empty);
        return $"<p>Hello {E(quote.RecipientName)},</p>" +
               $"<p>Please find your quote <strong>{E(quote.DisplayNumberWithVersion)}</strong> for " +
               $"{E(quote.Title)}, totalling <strong>{E(context.Formatting.Money(quote.Total, quote.CurrencyCode))}</strong>, " +
               $"valid until {E(quote.ValidUntil.ToString("d MMM yyyy"))}.</p>" +
               $"<p><a href=\"{E(shareUrl)}\">View and respond to your quote</a></p>";
    }
}
