using IronMonkey.Data.Abstractions;

namespace IronMonkey.Data.Entities;

/// <summary>
/// One tenant's quoting defaults and approval rule. A single row per tenant, in the tenant
/// database, like <see cref="MessagingPolicy"/>; absent means the built-in defaults below.
///
/// <para><b>Approval is a rule plus a permission, not hardcoded.</b>
/// <see cref="ApprovalDiscountThresholdPercent"/> says when approval is needed; who may give it
/// is whoever holds <c>quotes:approve</c> through the role model. The send endpoint enforces
/// it server-side, so a client that skips the approval step is refused rather than trusted.</para>
/// </summary>
public sealed class QuoteSettings : BaseTenantEntity
{
    public const string DefaultPrefix = "Q-";
    public const int DefaultValidityDaysValue = 30;
    public const int DefaultShareLinkDaysValue = 14;

    private QuoteSettings() { }

    public string NumberPrefix { get; private set; } = DefaultPrefix;
    public int DefaultValidityDays { get; private set; } = DefaultValidityDaysValue;
    public string? DefaultTerms { get; private set; }

    /// <summary>
    /// A quote whose largest line discount exceeds this percentage must be approved before it
    /// can be sent. Null = no approval rule.
    /// </summary>
    public decimal? ApprovalDiscountThresholdPercent { get; private set; }

    /// <summary>How long a customer share link stays valid.</summary>
    public int ShareLinkLifetimeDays { get; private set; } = DefaultShareLinkDaysValue;

    public static QuoteSettings CreateDefault(Guid tenantId) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId
    };

    public void Update(string numberPrefix, int defaultValidityDays, string? defaultTerms,
        decimal? approvalThresholdPercent, int shareLinkLifetimeDays)
    {
        NumberPrefix = string.IsNullOrWhiteSpace(numberPrefix) ? DefaultPrefix : numberPrefix.Trim();
        DefaultValidityDays = defaultValidityDays;
        DefaultTerms = string.IsNullOrWhiteSpace(defaultTerms) ? null : defaultTerms.Trim();
        ApprovalDiscountThresholdPercent = approvalThresholdPercent;
        ShareLinkLifetimeDays = shareLinkLifetimeDays;
    }
}
