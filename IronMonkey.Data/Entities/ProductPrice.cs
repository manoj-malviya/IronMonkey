using IronMonkey.Data.Abstractions;

namespace IronMonkey.Data.Entities;

/// <summary>
/// One version of a product's price in one price list, effective over
/// <c>[EffectiveFrom, EffectiveTo)</c>.
///
/// <para><b>Append-only.</b> A price change inserts a new row and closes the previous one by
/// setting its <see cref="EffectiveTo"/>; <see cref="UnitPrice"/> and <see cref="UnitCost"/>
/// are never rewritten. A line item and a quote line record the <see cref="ProductPrice"/>
/// they were priced from and copy its unit price, so a later price change cannot alter the
/// value of anything already priced — least of all a quote a customer has in hand.</para>
/// </summary>
public sealed class ProductPrice : BaseTenantEntity
{
    private ProductPrice() { }

    public Guid ProductId { get; private set; }
    public Guid PriceListId { get; private set; }
    public decimal UnitPrice { get; private set; }

    /// <summary>Optional cost, for margin. Never shown on a customer document.</summary>
    public decimal? UnitCost { get; private set; }

    public DateTime EffectiveFrom { get; private set; }

    /// <summary>Exclusive end, set when a newer version takes over. Null = still current.</summary>
    public DateTime? EffectiveTo { get; private set; }

    public Product Product { get; private set; } = null!;
    public PriceList PriceList { get; private set; } = null!;

    public static ProductPrice Create(Guid tenantId, Guid productId, Guid priceListId,
        decimal unitPrice, decimal? unitCost, DateTime effectiveFromUtc) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        ProductId = productId,
        PriceListId = priceListId,
        UnitPrice = unitPrice,
        UnitCost = unitCost,
        EffectiveFrom = DateTime.SpecifyKind(effectiveFromUtc, DateTimeKind.Utc)
    };

    /// <summary>Closes this version at the instant its successor becomes effective.</summary>
    public void SupersedeAt(DateTime successorEffectiveFromUtc) =>
        EffectiveTo = DateTime.SpecifyKind(successorEffectiveFromUtc, DateTimeKind.Utc);

    public bool IsEffectiveAt(DateTime instantUtc) =>
        EffectiveFrom <= instantUtc && (EffectiveTo is null || instantUtc < EffectiveTo);
}
