using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.ApiService.Common.Results;
using IronMonkey.Data;
using IronMonkey.Data.Commerce;
using IronMonkey.Data.Entities;

namespace IronMonkey.ApiService.Features.Commerce;

public sealed record LineItemResponse(
    Guid Id, int Position, Guid? ProductId, Guid? ProductPriceId, Guid? PriceListId,
    string Description, string? ProductCode, string? Category,
    decimal Quantity, decimal UnitPrice, string ChargeType, string BillingFrequency, int Periods,
    decimal DiscountPercent, decimal TaxRatePercent,
    decimal GrossAmount, decimal DiscountAmount, decimal NetAmount, decimal TaxAmount, decimal TotalAmount,
    bool IsMigrated);

public sealed record TotalsResponse(
    decimal Subtotal, decimal Discount, decimal Net, decimal Tax, decimal Total,
    decimal OneOffTotal, decimal RecurringTotal);

/// <param name="CurrencyCode">The deal's own currency; null means the tenant's.</param>
/// <param name="EffectiveCurrencyCode">What the figures are actually in — the deal's currency or,
/// when that is null, the tenant base currency (still null if the tenant configured none).</param>
/// <param name="MinorUnits">Decimal places figures are rounded to, so a client formats with
/// exactly the precision the server rounded to and never re-rounds.</param>
public sealed record DealEconomicsResponse(
    Guid OpportunityId, string? CurrencyCode, string? EffectiveCurrencyCode, int MinorUnits,
    decimal? ExchangeRate, DateOnly? ExchangeRateDate, Guid? PriceListId,
    List<LineItemResponse> Lines, TotalsResponse Totals);

/// <param name="ProductId">Catalog product, or null for a free-text line.</param>
/// <param name="PriceListId">Explicit price list for this line — the highest-precedence source.</param>
/// <param name="UnitPrice">For a catalog line, a manual override of the resolved price (the line
/// then records no price version). Required for a free-text line.</param>
public sealed record AddLineRequest(
    Guid? ProductId, Guid? PriceListId, string? Description,
    decimal Quantity, decimal? UnitPrice, int? Periods,
    decimal? DiscountPercent, decimal? TaxRatePercent,
    string? ChargeType, string? BillingFrequency);

public sealed record UpdateLineRequest(
    string? Description, decimal Quantity, decimal UnitPrice, int? Periods,
    decimal DiscountPercent, decimal TaxRatePercent);

public sealed record SetDealEconomicsRequest(
    string? CurrencyCode, decimal? ExchangeRate, DateOnly? ExchangeRateDate, Guid? PriceListId);

internal static class DealEconomics
{
    public static async Task<Opportunity?> LoadAsync(TenantDbContext db, Guid opportunityId, CancellationToken ct) =>
        // LineItems MUST be loaded: RecalculateTotals sums the in-memory collection, so a deal
        // loaded without it would recompute to zero and overwrite its stored value.
        await db.Opportunities
            .Include(o => o.LineItems)
            .SingleOrDefaultAsync(o => o.Id == opportunityId, ct);

    public static LineItemResponse ToResponse(OpportunityLineItem l) => new(
        l.Id, l.Position, l.ProductId, l.ProductPriceId, l.PriceListId,
        l.Description, l.ProductCode, l.Category,
        l.Quantity, l.UnitPrice, l.ChargeType.ToString(), l.BillingFrequency.ToString(), l.Periods,
        l.DiscountPercent, l.TaxRatePercent,
        l.GrossAmount, l.DiscountAmount, l.NetAmount, l.TaxAmount, l.TotalAmount, l.IsMigrated);

    public static DealEconomicsResponse ToResponse(Opportunity o, string? baseCurrency)
    {
        var lines = o.LineItems.OrderBy(l => l.Position).ToList();
        var totals = LineCalculator.Sum(lines.Select(l => (l.Amounts, l.ChargeType)));
        var effective = o.CurrencyCode ?? baseCurrency;

        return new DealEconomicsResponse(
            o.Id, o.CurrencyCode, effective, MoneyMath.MinorUnits(effective),
            o.ExchangeRate, o.ExchangeRateDate, o.PriceListId,
            lines.Select(ToResponse).ToList(), ToResponse(totals));
    }

    public static TotalsResponse ToResponse(DealTotals t) =>
        new(t.Subtotal, t.Discount, t.Net, t.Tax, t.Total, t.OneOffTotal, t.RecurringTotal);
}

public class GetDealEconomicsEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapGet("/api/opportunities/{id:guid}/lines", Handle)
        .WithSummary("Get an opportunity's line items, currency and computed totals")
        .WithTags("Opportunities")
        .RequireAuthorization();

    internal static async Task<Results<Ok<DealEconomicsResponse>, NotFound>> Handle(
        Guid id,
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        ITenantCommerceContextResolver commerce,
        CancellationToken cancellationToken)
    {
        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);
        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        var opportunity = await DealEconomics.LoadAsync(db, id, cancellationToken);
        if (opportunity is null) return TypedResults.NotFound();

        var context = await commerce.ResolveAsync(tenantId, cancellationToken);
        return TypedResults.Ok(DealEconomics.ToResponse(opportunity, context.BaseCurrency));
    }
}

public class AddLineItemEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapPost("/api/opportunities/{id:guid}/lines", Handle)
        .WithSummary("Add a catalog or free-text line to an opportunity")
        .WithTags("Opportunities")
        .RequireAuthorization();

    internal static async Task<Results<Ok<DealEconomicsResponse>, ValidationError, NotFound>> Handle(
        Guid id,
        AddLineRequest request,
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        ITenantCommerceContextResolver commerce,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);
        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        var opportunity = await DealEconomics.LoadAsync(db, id, cancellationToken);
        if (opportunity is null) return TypedResults.NotFound();

        var context = await commerce.ResolveAsync(tenantId, cancellationToken);
        var now = timeProvider.GetUtcNow().UtcDateTime;

        LineDetails details;
        if (request.ProductId is { } productId)
        {
            var product = await db.Products.SingleOrDefaultAsync(p => p.Id == productId, cancellationToken);
            if (product is null) return new ValidationError("That product does not exist.");
            if (!product.IsActive) return new ValidationError($"'{product.Name}' is no longer sold.");

            decimal unitPrice;
            decimal? unitCost = null;
            Guid? priceId = null, listId = request.PriceListId;

            var resolved = await PriceResolver.ResolveAsync(
                db, product.Id, request.PriceListId, opportunity.PriceListId, now, cancellationToken);

            if (resolved is not null)
            {
                // Pricing in another currency would put EUR prices on a GBP deal and every
                // total downstream would be wrong in a way no one notices.
                if (!MoneyMath.SameCurrency(resolved.PriceList.CurrencyCode, opportunity.CurrencyCode, context.BaseCurrency))
                    return new ValidationError(
                        $"'{resolved.PriceList.Name}' prices in {resolved.PriceList.CurrencyCode ?? context.BaseCurrency ?? "the tenant currency"}, " +
                        $"but this deal is in {opportunity.CurrencyCode ?? context.BaseCurrency ?? "the tenant currency"}. " +
                        "Choose a price list in the deal's currency.");

                listId = resolved.PriceList.Id;
                unitCost = resolved.Price.UnitCost;
            }

            if (request.UnitPrice is { } manual)
            {
                // A typed price is an override: the line keeps the product link for reporting
                // but records no price version, because no version produced this number.
                unitPrice = manual;
            }
            else if (resolved is not null)
            {
                unitPrice = resolved.Price.UnitPrice;
                priceId = resolved.Price.Id;
            }
            else
            {
                return new ValidationError(
                    $"'{product.Name}' has no price in effect on this deal's price lists. Enter a unit price.");
            }

            details = new LineDetails(
                product.Id, priceId, listId,
                string.IsNullOrWhiteSpace(request.Description) ? product.Name : request.Description,
                product.Code, product.Category,
                request.Quantity, unitPrice, unitCost,
                product.ChargeType, product.BillingFrequency,
                request.Periods ?? product.DefaultPeriods,
                request.DiscountPercent ?? 0m,
                request.TaxRatePercent ?? product.DefaultTaxRatePercent);
        }
        else
        {
            // Free text: the deal must never be blocked by a catalog that has not caught up.
            if (string.IsNullOrWhiteSpace(request.Description))
                return new ValidationError("A line without a product needs a description.");
            if (request.UnitPrice is null)
                return new ValidationError("A line without a product needs a unit price.");

            var chargeType = ChargeType.OneOff;
            var frequency = BillingFrequency.None;
            if (!string.IsNullOrWhiteSpace(request.ChargeType) && !Enum.TryParse(request.ChargeType, true, out chargeType))
                return new ValidationError("Charge type must be OneOff or Recurring.");
            if (!string.IsNullOrWhiteSpace(request.BillingFrequency) && !Enum.TryParse(request.BillingFrequency, true, out frequency))
                return new ValidationError($"Billing frequency must be one of: {string.Join(", ", Enum.GetNames<BillingFrequency>())}.");
            if (chargeType == ChargeType.Recurring && frequency == BillingFrequency.None)
                return new ValidationError("A recurring line needs a billing frequency.");

            details = new LineDetails(
                null, null, null, request.Description, null, null,
                request.Quantity, request.UnitPrice.Value, null,
                chargeType, frequency, request.Periods ?? 1,
                request.DiscountPercent ?? 0m, request.TaxRatePercent ?? 0m);
        }

        if (details.Description.Trim().Length > 500)
            return new ValidationError("Description must be 500 characters or fewer.");

        if (LineCalculator.Validate(details.Quantity, details.UnitPrice, details.Periods,
                details.DiscountPercent, details.TaxRatePercent) is { } lineError)
            return new ValidationError(lineError);

        opportunity.AddLine(details);
        opportunity.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(DealEconomics.ToResponse(opportunity, context.BaseCurrency));
    }
}

public class UpdateLineItemEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapPut("/api/opportunities/{id:guid}/lines/{lineId:guid}", Handle)
        .WithSummary("Change a line's quantity, price, discount or tax")
        .WithTags("Opportunities")
        .RequireAuthorization();

    internal static async Task<Results<Ok<DealEconomicsResponse>, ValidationError, NotFound>> Handle(
        Guid id,
        Guid lineId,
        UpdateLineRequest request,
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        ITenantCommerceContextResolver commerce,
        CancellationToken cancellationToken)
    {
        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);
        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        var opportunity = await DealEconomics.LoadAsync(db, id, cancellationToken);
        if (opportunity is null) return TypedResults.NotFound();

        var line = opportunity.LineItems.SingleOrDefault(l => l.Id == lineId);
        if (line is null) return TypedResults.NotFound();

        var periods = request.Periods ?? line.Periods;
        if (LineCalculator.Validate(request.Quantity, request.UnitPrice, periods,
                request.DiscountPercent, request.TaxRatePercent) is { } lineError)
            return new ValidationError(lineError);

        var description = string.IsNullOrWhiteSpace(request.Description) ? line.Description : request.Description;
        if (description.Trim().Length > 500)
            return new ValidationError("Description must be 500 characters or fewer.");

        // Changing the unit price detaches the line from its price version: the version no
        // longer explains the number, and keeping the link would make price-history reports lie.
        var priceVersion = request.UnitPrice == line.UnitPrice ? line.ProductPriceId : null;

        line.Apply(new LineDetails(
            line.ProductId, priceVersion, line.PriceListId, description, line.ProductCode, line.Category,
            request.Quantity, request.UnitPrice, line.UnitCost,
            line.ChargeType, line.BillingFrequency, periods,
            request.DiscountPercent, request.TaxRatePercent));

        opportunity.RecalculateTotals();
        opportunity.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        var context = await commerce.ResolveAsync(tenantId, cancellationToken);
        return TypedResults.Ok(DealEconomics.ToResponse(opportunity, context.BaseCurrency));
    }
}

public class RemoveLineItemEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapDelete("/api/opportunities/{id:guid}/lines/{lineId:guid}", Handle)
        .WithSummary("Remove a line from an opportunity")
        .WithTags("Opportunities")
        .RequireAuthorization();

    internal static async Task<Results<Ok<DealEconomicsResponse>, NotFound>> Handle(
        Guid id,
        Guid lineId,
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        ITenantCommerceContextResolver commerce,
        CancellationToken cancellationToken)
    {
        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);
        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        var opportunity = await DealEconomics.LoadAsync(db, id, cancellationToken);
        if (opportunity is null) return TypedResults.NotFound();

        if (!opportunity.RemoveLine(lineId)) return TypedResults.NotFound();

        opportunity.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        var context = await commerce.ResolveAsync(tenantId, cancellationToken);
        return TypedResults.Ok(DealEconomics.ToResponse(opportunity, context.BaseCurrency));
    }
}

public class SetDealEconomicsEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapPut("/api/opportunities/{id:guid}/economics", Handle)
        .WithSummary("Set an opportunity's currency, recorded exchange rate and price list")
        .WithTags("Opportunities")
        .RequireAuthorization();

    internal static async Task<Results<Ok<DealEconomicsResponse>, ValidationError, NotFound>> Handle(
        Guid id,
        SetDealEconomicsRequest request,
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        ITenantCommerceContextResolver commerce,
        CancellationToken cancellationToken)
    {
        if (!MoneyMath.TryNormalizeCurrency(request.CurrencyCode, out var currency))
            return new ValidationError("Currency must be a three-letter ISO 4217 code, e.g. EUR.");
        if (request.ExchangeRate is <= 0)
            return new ValidationError("An exchange rate must be greater than zero.");

        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);
        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        var opportunity = await DealEconomics.LoadAsync(db, id, cancellationToken);
        if (opportunity is null) return TypedResults.NotFound();

        var context = await commerce.ResolveAsync(tenantId, cancellationToken);

        // The tenant's own currency is stored as null, so "GBP" on a GBP tenant and an
        // unset currency are one state, not two that aggregate differently.
        if (currency is not null && string.Equals(currency, context.BaseCurrency, StringComparison.OrdinalIgnoreCase))
            currency = null;

        var isForeign = !MoneyMath.SameCurrency(currency, null, context.BaseCurrency);

        if (!MoneyMath.SameCurrency(currency, opportunity.CurrencyCode, context.BaseCurrency)
            && opportunity.LineItems.Any(l => l.PriceListId is not null))
        {
            return new ValidationError(
                "This deal has lines priced from a price list in its current currency. " +
                "Remove them before changing currency — relabelling those prices would misstate them.");
        }

        if (!isForeign && request.ExchangeRate is not null)
            return new ValidationError("A deal in the tenant's own currency needs no exchange rate.");

        if (request.PriceListId is { } listId)
        {
            var list = await db.PriceLists.SingleOrDefaultAsync(p => p.Id == listId, cancellationToken);
            if (list is null) return new ValidationError("That price list does not exist.");
            if (!MoneyMath.SameCurrency(list.CurrencyCode, currency, context.BaseCurrency))
                return new ValidationError($"'{list.Name}' is not in this deal's currency.");
        }

        opportunity.SetCurrency(currency);
        opportunity.SetExchangeRate(isForeign ? request.ExchangeRate : null,
            request.ExchangeRate is null ? null : request.ExchangeRateDate ?? context.Today);
        opportunity.SetPriceList(request.PriceListId);
        opportunity.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(DealEconomics.ToResponse(opportunity, context.BaseCurrency));
    }
}
