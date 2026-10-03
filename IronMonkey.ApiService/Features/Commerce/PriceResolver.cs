using Microsoft.EntityFrameworkCore;
using IronMonkey.Data;
using IronMonkey.Data.Entities;

namespace IronMonkey.ApiService.Features.Commerce;

/// <summary>Where a resolved price came from, so the line can record it.</summary>
public sealed record ResolvedPrice(ProductPrice Price, PriceList PriceList, PriceSource Source);

public enum PriceSource { LineOverride, OpportunityPriceList, DefaultPriceList }

/// <summary>
/// Finds the price of a product for a deal at an instant.
///
/// <para><b>Precedence, first match wins:</b></para>
/// <list type="number">
/// <item>The price list named on the line itself (an explicit override).</item>
/// <item>The price list set on the opportunity (the customer's agreement or segment).</item>
/// <item>The tenant's default price list.</item>
/// </list>
/// <para>Within a list, the version effective at <c>atUtc</c> — <c>EffectiveFrom ≤ t &lt;
/// EffectiveTo</c>. A level with no effective price for the product falls through to the next
/// one; an inactive list is skipped. There is no search for the lowest price: a segment price
/// applies because it was chosen, never because it happened to be cheaper.</para>
/// </summary>
public static class PriceResolver
{
    public static async Task<ResolvedPrice?> ResolveAsync(
        TenantDbContext db, Guid productId, Guid? linePriceListId, Guid? opportunityPriceListId,
        DateTime atUtc, CancellationToken cancellationToken)
    {
        var defaultListId = await db.PriceLists
            .Where(p => p.IsDefault && p.IsActive)
            .Select(p => (Guid?)p.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var levels = new List<(Guid Id, PriceSource Source)>();
        if (linePriceListId is { } line) levels.Add((line, PriceSource.LineOverride));
        if (opportunityPriceListId is { } opp) levels.Add((opp, PriceSource.OpportunityPriceList));
        if (defaultListId is { } def) levels.Add((def, PriceSource.DefaultPriceList));

        foreach (var (listId, source) in levels)
        {
            var price = await db.ProductPrices
                .Include(p => p.PriceList)
                .Where(p => p.ProductId == productId && p.PriceListId == listId && p.PriceList.IsActive)
                .Where(p => p.EffectiveFrom <= atUtc && (p.EffectiveTo == null || atUtc < p.EffectiveTo))
                .OrderByDescending(p => p.EffectiveFrom)
                .FirstOrDefaultAsync(cancellationToken);

            if (price is not null)
                return new ResolvedPrice(price, price.PriceList, source);
        }

        return null;
    }

    /// <summary>
    /// Adds a new price version, closing the one it supersedes. Refuses a version that would
    /// take effect before the latest existing one: back-dating would rewrite a window some
    /// line may already have been priced in.
    /// </summary>
    public static async Task<(ProductPrice? Price, string? Error)> AddVersionAsync(
        TenantDbContext db, Guid tenantId, Guid productId, Guid priceListId,
        decimal unitPrice, decimal? unitCost, DateTime effectiveFromUtc, CancellationToken cancellationToken)
    {
        var latest = await db.ProductPrices
            .Where(p => p.ProductId == productId && p.PriceListId == priceListId)
            .OrderByDescending(p => p.EffectiveFrom)
            .FirstOrDefaultAsync(cancellationToken);

        if (latest is not null && effectiveFromUtc <= latest.EffectiveFrom)
            return (null, $"A price version must take effect after the current one ({latest.EffectiveFrom:u}). " +
                          "Earlier windows are history and cannot be rewritten.");

        latest?.SupersedeAt(effectiveFromUtc);

        var price = ProductPrice.Create(tenantId, productId, priceListId, unitPrice, unitCost, effectiveFromUtc);
        db.ProductPrices.Add(price);
        return (price, null);
    }
}
