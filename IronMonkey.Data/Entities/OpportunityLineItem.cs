using IronMonkey.Data.Abstractions;
using IronMonkey.Data.Commerce;

namespace IronMonkey.Data.Entities;

/// <summary>
/// One line of a deal. Either references a catalog <see cref="Product"/> or is free text —
/// the free-text case exists so a deal is never blocked by a catalog that has not caught up
/// with what is being sold.
///
/// <para>Product name, code and category are <b>copied</b> onto the line when it is created.
/// Renaming a product, recategorising it, or changing its price must not rewrite what an
/// existing deal says it contains — and the revenue-by-category report reports what was
/// sold, under the category it was sold as.</para>
///
/// <para>The computed money columns are persisted so reports can aggregate in SQL. They are
/// written only by <see cref="Recalculate"/>, which goes through
/// <see cref="LineCalculator"/>, so the stored figures are by construction the figures every
/// other surface would compute.</para>
/// </summary>
public sealed class OpportunityLineItem : BaseTenantEntity
{
    /// <summary>Description given to the line that carries a pre-line-item <c>Amount</c>.</summary>
    public const string MigratedDescription = "Deal value (carried over from before line items)";

    private OpportunityLineItem() { }

    public Guid OpportunityId { get; private set; }
    public int Position { get; private set; }

    public Guid? ProductId { get; private set; }

    /// <summary>The exact price version this line was priced from, when it came from the
    /// catalog. Null for free-text lines and for lines whose price was typed by hand.</summary>
    public Guid? ProductPriceId { get; private set; }
    public Guid? PriceListId { get; private set; }

    public string Description { get; private set; } = string.Empty;
    public string? ProductCode { get; private set; }
    public string? Category { get; private set; }

    public decimal Quantity { get; private set; } = 1m;
    public decimal UnitPrice { get; private set; }
    public decimal? UnitCost { get; private set; }

    public ChargeType ChargeType { get; private set; }
    public BillingFrequency BillingFrequency { get; private set; }
    public int Periods { get; private set; } = 1;

    public decimal DiscountPercent { get; private set; }
    public decimal TaxRatePercent { get; private set; }

    public decimal GrossAmount { get; private set; }
    public decimal DiscountAmount { get; private set; }
    public decimal NetAmount { get; private set; }
    public decimal TaxAmount { get; private set; }
    public decimal TotalAmount { get; private set; }

    /// <summary>True for the line the data migration created from a former <c>Amount</c>.</summary>
    public bool IsMigrated { get; private set; }

    public static OpportunityLineItem Create(Guid tenantId, Guid opportunityId, int position, LineDetails details)
    {
        var line = new OpportunityLineItem
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            OpportunityId = opportunityId,
            Position = position
        };
        line.Apply(details);
        return line;
    }

    /// <summary>A single free-text one-off line carrying a lump-sum deal value.</summary>
    public static OpportunityLineItem LumpSum(Guid tenantId, Guid opportunityId, decimal amount, string description) =>
        Create(tenantId, opportunityId, 1, new LineDetails(
            ProductId: null, ProductPriceId: null, PriceListId: null,
            Description: description, ProductCode: null, Category: null,
            Quantity: 1m, UnitPrice: amount, UnitCost: null,
            ChargeType: ChargeType.OneOff, BillingFrequency: BillingFrequency.None, Periods: 1,
            DiscountPercent: 0m, TaxRatePercent: 0m));

    public void Apply(LineDetails details)
    {
        ProductId = details.ProductId;
        ProductPriceId = details.ProductPriceId;
        PriceListId = details.PriceListId;
        Description = details.Description.Trim();
        ProductCode = details.ProductCode;
        Category = details.Category;
        Quantity = details.Quantity;
        UnitPrice = details.UnitPrice;
        UnitCost = details.UnitCost;
        ChargeType = details.ChargeType;
        BillingFrequency = details.ChargeType == ChargeType.Recurring ? details.BillingFrequency : BillingFrequency.None;
        Periods = details.ChargeType == ChargeType.Recurring ? Math.Max(1, details.Periods) : 1;
        DiscountPercent = details.DiscountPercent;
        TaxRatePercent = details.TaxRatePercent;
        IsMigrated = false;
    }

    public void MoveTo(int position) => Position = position;

    public LineInput ToInput() => new(Quantity, UnitPrice, Periods, DiscountPercent, TaxRatePercent, ChargeType);

    public LineAmounts Recalculate(string? currencyCode)
    {
        var amounts = LineCalculator.Compute(ToInput(), currencyCode);
        GrossAmount = amounts.Gross;
        DiscountAmount = amounts.Discount;
        NetAmount = amounts.Net;
        TaxAmount = amounts.Tax;
        TotalAmount = amounts.Total;
        return amounts;
    }

    public LineAmounts Amounts => new(GrossAmount, DiscountAmount, NetAmount, TaxAmount, TotalAmount);
}

/// <summary>Everything that defines a line, as resolved by the caller (catalog or free text).</summary>
public sealed record LineDetails(
    Guid? ProductId,
    Guid? ProductPriceId,
    Guid? PriceListId,
    string Description,
    string? ProductCode,
    string? Category,
    decimal Quantity,
    decimal UnitPrice,
    decimal? UnitCost,
    ChargeType ChargeType,
    BillingFrequency BillingFrequency,
    int Periods,
    decimal DiscountPercent,
    decimal TaxRatePercent);
