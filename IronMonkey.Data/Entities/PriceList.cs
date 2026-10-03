using IronMonkey.Data.Abstractions;

namespace IronMonkey.Data.Entities;

/// <summary>
/// A named set of prices — "Standard", "Fleet", "International students", "Broker panel A".
///
/// <para><b>Precedence is explicit and fixed</b> (see <c>PriceResolver</c>): a price list
/// chosen on the line, then the price list set on the opportunity, then the tenant's default
/// list. The first of those that holds an effective price for the product wins. There is no
/// "best price" search across lists: a segment or agreement price applies because someone
/// chose it for the deal, never because it happened to be lower.</para>
///
/// <para>Every price in a list is in the list's currency. A list in EUR cannot price a GBP
/// deal; the line endpoint refuses rather than converting silently.</para>
/// </summary>
public sealed class PriceList : BaseTenantEntity
{
    private PriceList() { }

    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }

    /// <summary>ISO 4217 code; null means the tenant's own currency.</summary>
    public string? CurrencyCode { get; private set; }

    /// <summary>
    /// The fallback list. Exactly one per tenant, enforced by a filtered unique index — with
    /// none, a deal with no explicit list would have no price at all.
    /// </summary>
    public bool IsDefault { get; private set; }

    public bool IsActive { get; private set; } = true;

    public static PriceList Create(Guid tenantId, string name, string? currencyCode, bool isDefault, string? description = null) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        Name = name.Trim(),
        CurrencyCode = currencyCode,
        IsDefault = isDefault,
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim()
    };

    public void Update(string name, string? description)
    {
        Name = name.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
    }

    public void MarkDefault() => IsDefault = true;
    public void ClearDefault() => IsDefault = false;
    public void Deactivate() => IsActive = false;
    public void Activate() => IsActive = true;
}
