using IronMonkey.Data.Abstractions;
using IronMonkey.Data.Commerce;

namespace IronMonkey.Data.Entities;

/// <summary>
/// Something a tenant sells: a vehicle, a programme, a policy, a retainer.
///
/// <para>Prices are NOT stored here. They live in <see cref="ProductPrice"/> rows, versioned
/// by effective date and grouped by <see cref="PriceList"/>, because a price is a fact about
/// a moment and a market, not about the product. The product carries only how it is charged
/// — one-off or recurring, and how often — which is what lets a dealership's vehicle and a
/// university's per-term tuition share one model.</para>
///
/// <para>Vertical attributes (engine size, intake month, cover type) are custom fields with
/// <see cref="CustomFieldEntity.Product"/> scope, bound through the same
/// <c>CustomFieldValueBinder</c> as leads and contacts — not a second field system.</para>
/// </summary>
public sealed class Product : BaseTenantEntity
{
    private Product() { }

    /// <summary>Tenant-chosen SKU or code, unique per tenant case-insensitively.</summary>
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public string? Category { get; private set; }
    public bool IsActive { get; private set; } = true;

    public ChargeType ChargeType { get; private set; }
    public BillingFrequency BillingFrequency { get; private set; }

    /// <summary>
    /// Default number of periods a recurring line covers — 12 for a yearly contract billed
    /// monthly, 2 for an academic year of semesters. Always 1 for a one-off product.
    /// </summary>
    public int DefaultPeriods { get; private set; } = 1;

    /// <summary>What one unit is called on a document: "unit", "seat", "vehicle", "hour".</summary>
    public string? UnitOfMeasure { get; private set; }

    public decimal DefaultTaxRatePercent { get; private set; }

    public CustomFieldValues CustomFields { get; private set; } = new();

    public static Product Create(Guid tenantId, string code, string name, ChargeType chargeType,
        BillingFrequency frequency, int defaultPeriods = 1)
    {
        var product = new Product { Id = Guid.NewGuid(), TenantId = tenantId };
        product.Update(code, name, null, null, chargeType, frequency, defaultPeriods, null, 0m);
        return product;
    }

    public void Update(string code, string name, string? description, string? category,
        ChargeType chargeType, BillingFrequency frequency, int defaultPeriods,
        string? unitOfMeasure, decimal defaultTaxRatePercent)
    {
        Code = code.Trim();
        Name = name.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        Category = string.IsNullOrWhiteSpace(category) ? null : category.Trim();
        ChargeType = chargeType;

        // A one-off product has no frequency and exactly one period; storing anything else
        // would let a stale "Monthly" leak into a recurring-revenue report.
        BillingFrequency = chargeType == ChargeType.Recurring
            ? (frequency == BillingFrequency.None ? BillingFrequency.Monthly : frequency)
            : BillingFrequency.None;
        DefaultPeriods = chargeType == ChargeType.Recurring ? Math.Max(1, defaultPeriods) : 1;

        UnitOfMeasure = string.IsNullOrWhiteSpace(unitOfMeasure) ? null : unitOfMeasure.Trim();
        DefaultTaxRatePercent = defaultTaxRatePercent;
    }

    public void SetCustomFields(Dictionary<string, object?> values) =>
        CustomFields = new CustomFieldValues { Values = values };

    public void Deactivate() => IsActive = false;
    public void Activate() => IsActive = true;
}
