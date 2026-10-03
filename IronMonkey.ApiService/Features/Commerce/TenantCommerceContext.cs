using Microsoft.EntityFrameworkCore;
using IronMonkey.Data;
using IronMonkey.Data.Commerce;
using IronMonkey.Data.Presentation;

namespace IronMonkey.ApiService.Features.Commerce;

/// <summary>The tenant facts money handling depends on: base currency and "today".</summary>
/// <param name="BaseCurrency">The tenant's configured ISO 4217 code, or null if unconfigured.
/// Null is a real answer, not an error — it means "the tenant's own unnamed currency", and
/// values with no currency of their own aggregate with it.</param>
public sealed record TenantCommerceContext(string? BaseCurrency, TenantFormatting Formatting)
{
    public DateOnly Today => Formatting.Today();

    /// <summary>
    /// The tenant's calendar date at an instant. Callers holding a <see cref="TimeProvider"/>
    /// use this rather than <see cref="Today"/>, so validity and expiry follow the injected
    /// clock — a quote's expiry must be testable without waiting for it.
    /// </summary>
    public DateOnly TodayAt(DateTime utcNow) => DateOnly.FromDateTime(Formatting.ToTenantTime(utcNow));
}

public interface ITenantCommerceContextResolver
{
    Task<TenantCommerceContext> ResolveAsync(Guid tenantId, CancellationToken cancellationToken);
}

/// <summary>
/// Reads the tenant's locale from the central tenant row — the same source
/// <see cref="TenantFormatting"/> uses for the UI, so the API and the screen agree on which
/// currency is "ours".
/// </summary>
public sealed class TenantCommerceContextResolver(CentralDbContext centralDb) : ITenantCommerceContextResolver
{
    public async Task<TenantCommerceContext> ResolveAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var tenant = await centralDb.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken);

        var locale = tenant?.Presentation?.Locale;
        MoneyMath.TryNormalizeCurrency(locale?.CurrencyCode, out var currency);

        return new TenantCommerceContext(currency, TenantFormatting.From(locale));
    }
}

/// <summary>A fixed context, for tests and for callers that already know the answer.</summary>
public sealed class FixedCommerceContextResolver(string? baseCurrency) : ITenantCommerceContextResolver
{
    public Task<TenantCommerceContext> ResolveAsync(Guid tenantId, CancellationToken cancellationToken) =>
        Task.FromResult(new TenantCommerceContext(baseCurrency,
            TenantFormatting.From(new TenantLocale { CurrencyCode = baseCurrency })));
}
