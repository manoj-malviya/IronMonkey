using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.ApiService.Common.Results;
using IronMonkey.ApiService.Features.Commerce;
using IronMonkey.Common.Auth;
using IronMonkey.Data;
using IronMonkey.Data.Commerce;
using IronMonkey.Data.Entities;

namespace IronMonkey.ApiService.Features.Quotes;

public sealed record QuoteLineResponse(
    int Position, Guid? ProductId, string Description, string? ProductCode, string? Category,
    decimal Quantity, decimal UnitPrice, string ChargeType, string BillingFrequency, int Periods,
    decimal DiscountPercent, decimal TaxRatePercent,
    decimal GrossAmount, decimal DiscountAmount, decimal NetAmount, decimal TaxAmount, decimal TotalAmount);

public sealed record QuoteHistoryResponse(
    string? FromStatus, string ToStatus, Guid? ChangedByUserId, string? ChangedByName, string? Note, DateTime ChangedAt);

public sealed record QuoteShareLinkResponse(Guid Id, DateTime CreatedAt, DateTime ExpiresAt, DateTime? RevokedAt,
    DateTime? LastViewedAt, int ViewCount, bool IsUsable);

public sealed record QuoteResponse(
    Guid Id, Guid OpportunityId, string Number, int Version, string Status, bool IsEditable,
    string Title, string? CurrencyCode, string? EffectiveCurrencyCode, int MinorUnits,
    DateOnly ValidUntil, string? Terms, string RecipientName, string? RecipientEmail,
    decimal Subtotal, decimal DiscountTotal, decimal TaxTotal, decimal Total, decimal OneOffTotal, decimal RecurringTotal,
    decimal MaxDiscountPercent, bool RequiresApproval, bool IsAwaitingApproval, Guid? ApprovedByUserId, DateTime? ApprovedAt,
    Guid? SupersedesQuoteId, Guid? SupersededByQuoteId,
    DateTime CreatedAt, DateTime? SentAt, DateTime? RespondedAt, string? RespondedByName, string? ResponseNote,
    List<QuoteLineResponse> Lines, List<QuoteHistoryResponse> History, List<QuoteShareLinkResponse> ShareLinks);

public sealed record QuoteSummaryResponse(
    Guid Id, string Number, int Version, string Status, string Title, string? CurrencyCode,
    decimal Total, DateOnly ValidUntil, bool IsAwaitingApproval, DateTime CreatedAt, DateTime? SentAt);

public sealed record CreateQuoteRequest(Guid OpportunityId, string? Title, DateOnly? ValidUntil, string? Terms);
public sealed record UpdateQuoteRequest(string Title, DateOnly ValidUntil, string? Terms);
public sealed record SendQuoteRequest(bool? DeliverByEmail);
public sealed record SendQuoteResponse(QuoteResponse Quote, string ShareUrl, string? DeliveryStatus, string? DeliveryError);
public sealed record RespondQuoteRequest(bool Accept, string RespondedByName, string? Note);
public sealed record ShareLinkCreatedResponse(QuoteShareLinkResponse Link, string Url);

public sealed record QuoteSettingsResponse(string NumberPrefix, int DefaultValidityDays, string? DefaultTerms,
    decimal? ApprovalDiscountThresholdPercent, int ShareLinkLifetimeDays);

internal static class QuoteMapping
{
    public static async Task<QuoteResponse> ToResponseAsync(TenantDbContext db, Quote q, string? baseCurrency, DateTime nowUtc, CancellationToken ct)
    {
        var links = await db.QuoteShareLinks.AsNoTracking()
            .Where(l => l.QuoteId == q.Id)
            .OrderByDescending(l => l.CreatedAt)
            .ToListAsync(ct);

        var effective = q.CurrencyCode ?? baseCurrency;

        return new QuoteResponse(
            q.Id, q.OpportunityId, q.DisplayNumber, q.Version, q.Status.ToString(), q.IsEditable,
            q.Title, q.CurrencyCode, effective, MoneyMath.MinorUnits(effective),
            q.ValidUntil, q.Terms, q.RecipientName, q.RecipientEmail,
            q.Subtotal, q.DiscountTotal, q.TaxTotal, q.Total, q.OneOffTotal, q.RecurringTotal,
            q.MaxDiscountPercent, q.RequiresApproval, q.IsAwaitingApproval, q.ApprovedByUserId, q.ApprovedAt,
            q.SupersedesQuoteId, q.SupersededByQuoteId,
            q.CreatedAt, q.SentAt, q.RespondedAt, q.RespondedByName, q.ResponseNote,
            q.Lines.OrderBy(l => l.Position).Select(l => new QuoteLineResponse(
                l.Position, l.ProductId, l.Description, l.ProductCode, l.Category,
                l.Quantity, l.UnitPrice, l.ChargeType.ToString(), l.BillingFrequency.ToString(), l.Periods,
                l.DiscountPercent, l.TaxRatePercent,
                l.GrossAmount, l.DiscountAmount, l.NetAmount, l.TaxAmount, l.TotalAmount)).ToList(),
            q.StatusChanges.OrderBy(c => c.ChangedAt).Select(c => new QuoteHistoryResponse(
                c.FromStatus?.ToString(), c.ToStatus.ToString(), c.ChangedByUserId, c.ChangedByName, c.Note, c.ChangedAt)).ToList(),
            links.Select(l => ToResponse(l, nowUtc)).ToList());
    }

    public static QuoteShareLinkResponse ToResponse(QuoteShareLink l, DateTime nowUtc) =>
        new(l.Id, l.CreatedAt, l.ExpiresAt, l.RevokedAt, l.LastViewedAt, l.ViewCount, l.IsUsableAt(nowUtc));

    public static IResult Problem(QuoteOperation op) => op.StatusCode switch
    {
        StatusCodes.Status404NotFound => TypedResults.NotFound(),
        StatusCodes.Status409Conflict => TypedResults.Conflict(op.Error),
        _ => new ValidationError(op.Error!)
    };

    /// <summary>
    /// The origin the public link is built on. Configured explicitly in production
    /// (<c>Quotes:PublicBaseUrl</c>) because behind a proxy the request's own host is the
    /// internal one; falls back to the request so local runs work unconfigured.
    /// </summary>
    public static string PublicBaseUrl(HttpContext http, IConfiguration configuration) =>
        configuration["Quotes:PublicBaseUrl"] is { Length: > 0 } configured
            ? configured
            : $"{http.Request.Scheme}://{http.Request.Host}";
}

public class ListQuotesEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapGet("/api/opportunities/{id:guid}/quotes", Handle)
        .WithSummary("List every quote and version generated for an opportunity")
        .WithTags("Quotes")
        .RequireAuthorization();

    internal static async Task<Ok<List<QuoteSummaryResponse>>> Handle(
        Guid id, ITenantService tenantService, ITenantDbContextFactory dbContextFactory, CancellationToken cancellationToken)
    {
        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);
        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        var quotes = await db.Quotes.AsNoTracking()
            .Where(q => q.OpportunityId == id)
            .OrderByDescending(q => q.Number).ThenByDescending(q => q.Version)
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(quotes.Select(q => new QuoteSummaryResponse(
            q.Id, q.DisplayNumber, q.Version, q.Status.ToString(), q.Title, q.CurrencyCode,
            q.Total, q.ValidUntil, q.IsAwaitingApproval, q.CreatedAt, q.SentAt)).ToList());
    }
}

public class GetQuoteEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapGet("/api/quotes/{id:guid}", Handle)
        .WithSummary("Get a quote with its lines, history and share links")
        .WithTags("Quotes")
        .RequireAuthorization();

    internal static async Task<Results<Ok<QuoteResponse>, NotFound>> Handle(
        Guid id, ITenantService tenantService, ITenantDbContextFactory dbContextFactory,
        IQuoteService quotes, ITenantCommerceContextResolver commerce, TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);
        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        var quote = await quotes.LoadAsync(db, id, cancellationToken);
        if (quote is null) return TypedResults.NotFound();

        var context = await commerce.ResolveAsync(tenantId, cancellationToken);
        var now = timeProvider.GetUtcNow().UtcDateTime;

        // Reading a sent quote past its validity records the expiry, so the CRM never shows a
        // lapsed offer as still open.
        if (quote.ExpireIfPastValidity(context.TodayAt(now), now))
            await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(await QuoteMapping.ToResponseAsync(db, quote, context.BaseCurrency, now, cancellationToken));
    }
}

public class GetQuoteDocumentEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapGet("/api/quotes/{id:guid}/document", Handle)
        .WithSummary("Render the quote document as HTML (internal preview)")
        .WithTags("Quotes")
        .RequireAuthorization();

    internal static async Task<IResult> Handle(
        Guid id, ITenantService tenantService, ITenantDbContextFactory dbContextFactory,
        IQuoteService quotes, CentralDbContext centralDb, CancellationToken cancellationToken)
    {
        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);
        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        var quote = await quotes.LoadAsync(db, id, cancellationToken);
        if (quote is null) return TypedResults.NotFound();

        var tenant = await centralDb.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken);
        var html = QuoteDocumentRenderer.Render(quote, tenant?.Presentation?.Branding, tenant?.Name ?? "",
            Data.Presentation.TenantFormatting.From(tenant?.Presentation?.Locale));

        return Results.Content(html, "text/html; charset=utf-8");
    }
}

public class CreateQuoteEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapPost("/api/quotes", Handle)
        .WithSummary("Generate a draft quote from an opportunity's current lines")
        .WithTags("Quotes")
        .RequireAuthorization();

    internal static async Task<IResult> Handle(
        CreateQuoteRequest request, ITenantService tenantService, ITenantDbContextFactory dbContextFactory,
        IUserContext userContext, IQuoteService quotes, ITenantCommerceContextResolver commerce,
        TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);
        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        var op = await quotes.CreateDraftAsync(db, tenantId, request.OpportunityId, userContext.UserId,
            request.Title, request.ValidUntil, request.Terms, cancellationToken);
        if (!op.Succeeded) return QuoteMapping.Problem(op);

        var context = await commerce.ResolveAsync(tenantId, cancellationToken);
        return TypedResults.Created($"/api/quotes/{op.Quote!.Id}",
            await QuoteMapping.ToResponseAsync(db, op.Quote, context.BaseCurrency, timeProvider.GetUtcNow().UtcDateTime, cancellationToken));
    }
}

public class UpdateQuoteEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapPut("/api/quotes/{id:guid}", Handle)
        .WithSummary("Edit a draft quote's title, validity and terms. Refused once sent.")
        .WithTags("Quotes")
        .RequireAuthorization();

    internal static async Task<IResult> Handle(
        Guid id, UpdateQuoteRequest request, ITenantService tenantService, ITenantDbContextFactory dbContextFactory,
        IQuoteService quotes, ITenantCommerceContextResolver commerce, TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Title)) return new ValidationError("Title is required.");

        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);
        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        var quote = await quotes.LoadAsync(db, id, cancellationToken);
        if (quote is null) return TypedResults.NotFound();

        var context = await commerce.ResolveAsync(tenantId, cancellationToken);
        if (request.ValidUntil < context.TodayAt(timeProvider.GetUtcNow().UtcDateTime))
            return new ValidationError("A quote cannot be valid until a date in the past.");

        var result = quote.UpdateDraft(request.Title, request.ValidUntil, request.Terms);
        if (!result.Succeeded) return TypedResults.Conflict(result.Error);

        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(await QuoteMapping.ToResponseAsync(db, quote, context.BaseCurrency, timeProvider.GetUtcNow().UtcDateTime, cancellationToken));
    }
}

/// <summary>Draft lifecycle actions that take no body: refresh, approve, revise.</summary>
public class QuoteActionEndpoints : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/quotes/{id:guid}/refresh", Refresh)
            .WithSummary("Re-copy a draft's lines from the opportunity (clears any approval)")
            .WithTags("Quotes")
            .RequireAuthorization();

        // Who may approve is a permission, granted through roles — not a hardcoded role name.
        app.MapPost("/api/quotes/{id:guid}/approve", Approve)
            .WithSummary("Approve a draft whose discount exceeds the tenant threshold")
            .WithTags("Quotes")
            .RequireAuthorization(PermissionConstants.QuotesApprove);

        app.MapPost("/api/quotes/{id:guid}/revise", Revise)
            .WithSummary("Create the next version of a sent quote as a new draft")
            .WithTags("Quotes")
            .RequireAuthorization();
    }

    internal static Task<IResult> Refresh(Guid id, ITenantService tenantService, ITenantDbContextFactory dbContextFactory,
        IQuoteService quotes, ITenantCommerceContextResolver commerce, TimeProvider timeProvider, CancellationToken ct) =>
        Run(tenantService, dbContextFactory, commerce, timeProvider, ct, (db, _) => quotes.RefreshAsync(db, id, ct));

    internal static Task<IResult> Approve(Guid id, ITenantService tenantService, ITenantDbContextFactory dbContextFactory,
        IUserContext userContext, IQuoteService quotes, ITenantCommerceContextResolver commerce, TimeProvider timeProvider, CancellationToken ct) =>
        Run(tenantService, dbContextFactory, commerce, timeProvider, ct, (db, _) => quotes.ApproveAsync(db, id, userContext.UserId, ct));

    internal static Task<IResult> Revise(Guid id, ITenantService tenantService, ITenantDbContextFactory dbContextFactory,
        IUserContext userContext, IQuoteService quotes, ITenantCommerceContextResolver commerce, TimeProvider timeProvider, CancellationToken ct) =>
        Run(tenantService, dbContextFactory, commerce, timeProvider, ct, (db, tenantId) => quotes.ReviseAsync(db, tenantId, id, userContext.UserId, ct));

    private static async Task<IResult> Run(ITenantService tenantService, ITenantDbContextFactory dbContextFactory,
        ITenantCommerceContextResolver commerce, TimeProvider timeProvider, CancellationToken ct,
        Func<TenantDbContext, Guid, Task<QuoteOperation>> action)
    {
        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(ct);
        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        var op = await action(db, tenantId);
        if (!op.Succeeded) return QuoteMapping.Problem(op);

        var context = await commerce.ResolveAsync(tenantId, ct);
        return TypedResults.Ok(await QuoteMapping.ToResponseAsync(db, op.Quote!, context.BaseCurrency, timeProvider.GetUtcNow().UtcDateTime, ct));
    }
}

public class SendQuoteEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapPost("/api/quotes/{id:guid}/send", Handle)
        .WithSummary("Send a draft: freezes it, supersedes older versions, issues a share link and optionally emails it")
        .WithTags("Quotes")
        .RequireAuthorization();

    internal static async Task<IResult> Handle(
        Guid id, SendQuoteRequest? request, HttpContext http, IConfiguration configuration,
        ITenantService tenantService, ITenantDbContextFactory dbContextFactory, IUserContext userContext,
        IQuoteService quotes, ITenantCommerceContextResolver commerce, TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);
        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        var outcome = await quotes.SendAsync(db, tenantId, id, userContext.UserId,
            QuoteMapping.PublicBaseUrl(http, configuration), request?.DeliverByEmail ?? true, cancellationToken);

        if (!outcome.Operation.Succeeded) return QuoteMapping.Problem(outcome.Operation);

        var context = await commerce.ResolveAsync(tenantId, cancellationToken);
        var response = await QuoteMapping.ToResponseAsync(db, outcome.Operation.Quote!, context.BaseCurrency,
            timeProvider.GetUtcNow().UtcDateTime, cancellationToken);

        return TypedResults.Ok(new SendQuoteResponse(response, outcome.ShareUrl!,
            outcome.Delivery?.Status.ToString(), outcome.Delivery?.ErrorMessage));
    }
}

public class RespondQuoteEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapPost("/api/quotes/{id:guid}/respond", Handle)
        .WithSummary("Record a customer's acceptance or rejection received outside the share link")
        .WithTags("Quotes")
        .RequireAuthorization();

    internal static async Task<IResult> Handle(
        Guid id, RespondQuoteRequest request, ITenantService tenantService, ITenantDbContextFactory dbContextFactory,
        IUserContext userContext, IQuoteService quotes, ITenantCommerceContextResolver commerce, TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);
        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        var quote = await quotes.LoadAsync(db, id, cancellationToken);
        if (quote is null) return TypedResults.NotFound();

        var op = await quotes.RespondAsync(db, tenantId, quote, request.Accept, userContext.UserId,
            request.RespondedByName, request.Note, cancellationToken);
        if (!op.Succeeded) return QuoteMapping.Problem(op);

        var context = await commerce.ResolveAsync(tenantId, cancellationToken);
        return TypedResults.Ok(await QuoteMapping.ToResponseAsync(db, quote, context.BaseCurrency, timeProvider.GetUtcNow().UtcDateTime, cancellationToken));
    }
}

public class QuoteShareLinkEndpoints : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/quotes/{id:guid}/share-links", Create)
            .WithSummary("Issue a new customer share link for a sent quote. The URL is shown once.")
            .WithTags("Quotes")
            .RequireAuthorization();

        app.MapPost("/api/quotes/{id:guid}/share-links/{linkId:guid}/revoke", Revoke)
            .WithSummary("Revoke a share link immediately")
            .WithTags("Quotes")
            .RequireAuthorization();
    }

    internal static async Task<IResult> Create(Guid id, HttpContext http, IConfiguration configuration,
        ITenantService tenantService, ITenantDbContextFactory dbContextFactory, IUserContext userContext,
        IQuoteService quotes, TimeProvider timeProvider, CancellationToken ct)
    {
        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(ct);
        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        var quote = await db.Quotes.SingleOrDefaultAsync(q => q.Id == id, ct);
        if (quote is null) return TypedResults.NotFound();

        // A draft is not an offer, and a superseded version must not be re-opened for
        // acceptance through a fresh link.
        if (quote.Status is not (QuoteStatus.Sent or QuoteStatus.Accepted or QuoteStatus.Rejected))
            return TypedResults.Conflict($"A {quote.Status.ToString().ToLowerInvariant()} quote cannot be shared.");

        var (link, url) = await quotes.CreateShareLinkAsync(db, tenantId, quote, userContext.UserId,
            QuoteMapping.PublicBaseUrl(http, configuration), ct);
        await db.SaveChangesAsync(ct);

        return TypedResults.Ok(new ShareLinkCreatedResponse(QuoteMapping.ToResponse(link, timeProvider.GetUtcNow().UtcDateTime), url));
    }

    internal static async Task<IResult> Revoke(Guid id, Guid linkId, ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory, TimeProvider timeProvider, CancellationToken ct)
    {
        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(ct);
        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        var link = await db.QuoteShareLinks.SingleOrDefaultAsync(l => l.Id == linkId && l.QuoteId == id, ct);
        if (link is null) return TypedResults.NotFound();

        var now = timeProvider.GetUtcNow().UtcDateTime;
        link.Revoke(now);
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(QuoteMapping.ToResponse(link, now));
    }
}

public class QuoteSettingsEndpoints : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/quote-settings", Get)
            .WithSummary("Get quote numbering, validity, terms and the discount approval threshold")
            .WithTags("Quotes")
            .RequireAuthorization();

        app.MapPut("/api/quote-settings", Put)
            .WithSummary("Update quote settings")
            .WithTags("Quotes")
            .RequireAuthorization(PermissionConstants.CatalogWrite);
    }

    internal static async Task<Ok<QuoteSettingsResponse>> Get(ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory, IQuoteService quotes, CancellationToken ct)
    {
        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(ct);
        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        return TypedResults.Ok(ToResponse(await quotes.GetSettingsAsync(db, tenantId, ct)));
    }

    internal static async Task<Results<Ok<QuoteSettingsResponse>, ValidationError>> Put(QuoteSettingsResponse request,
        ITenantService tenantService, ITenantDbContextFactory dbContextFactory, CancellationToken ct)
    {
        if (request.DefaultValidityDays is < 1 or > 365) return new ValidationError("Validity must be between 1 and 365 days.");
        if (request.ShareLinkLifetimeDays is < 1 or > 90) return new ValidationError("Share links must last between 1 and 90 days.");
        if (request.ApprovalDiscountThresholdPercent is < 0 or > 100)
            return new ValidationError("The approval threshold must be between 0 and 100 percent.");
        if (request.NumberPrefix?.Length > 20) return new ValidationError("The number prefix must be 20 characters or fewer.");

        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(ct);
        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        var settings = await db.QuoteSettings.FirstOrDefaultAsync(ct);
        if (settings is null)
        {
            settings = QuoteSettings.CreateDefault(tenantId);
            db.QuoteSettings.Add(settings);
        }

        settings.Update(request.NumberPrefix ?? QuoteSettings.DefaultPrefix, request.DefaultValidityDays,
            request.DefaultTerms, request.ApprovalDiscountThresholdPercent, request.ShareLinkLifetimeDays);
        await db.SaveChangesAsync(ct);

        return TypedResults.Ok(ToResponse(settings));
    }

    private static QuoteSettingsResponse ToResponse(QuoteSettings s) =>
        new(s.NumberPrefix, s.DefaultValidityDays, s.DefaultTerms, s.ApprovalDiscountThresholdPercent, s.ShareLinkLifetimeDays);
}
