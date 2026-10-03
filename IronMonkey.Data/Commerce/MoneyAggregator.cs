namespace IronMonkey.Data.Commerce;

/// <summary>One monetary value to aggregate, with the conversion recorded on its record.</summary>
/// <param name="CurrencyCode">The value's currency; null means the tenant's own currency.</param>
/// <param name="ExchangeRate">
/// Units of the tenant base currency per one unit of <paramref name="CurrencyCode"/>, as
/// recorded on the record. Null means no conversion was recorded.
/// </param>
public readonly record struct MoneyValue(decimal Amount, string? CurrencyCode, decimal? ExchangeRate);

/// <summary>The result of summing values that may be in more than one currency.</summary>
/// <param name="Total">The sum in <paramref name="CurrencyCode"/> of every value that could be
/// counted.</param>
/// <param name="CurrencyCode">The tenant base currency the total is in (null = unconfigured).</param>
/// <param name="ConvertedCount">Values in a foreign currency counted through their recorded rate.</param>
/// <param name="ExcludedCount">Values in a foreign currency with no recorded rate — NOT counted.</param>
/// <param name="ExcludedAmounts">The excluded values, summed per currency, so a UI can say
/// exactly what was left out.</param>
public sealed record MoneyTotal(
    decimal Total,
    string? CurrencyCode,
    int ConvertedCount,
    int ExcludedCount,
    IReadOnlyDictionary<string, decimal> ExcludedAmounts)
{
    /// <summary>True when every value was counted. A partial total must be shown as one.</summary>
    public bool IsComplete => ExcludedCount == 0;
}

/// <summary>
/// Sums money without ever silently adding two currencies together.
///
/// <para>A value in the tenant's base currency is counted as is. A value in another currency
/// is counted only through the exchange rate recorded on its own record, rounded per value
/// with <see cref="MoneyMath.Round"/>. A value in another currency with no recorded rate is
/// <b>excluded and reported</b>, never added at face value: a report that sums pounds and
/// euros as though they were the same unit is worse than one that declines to.</para>
///
/// <para>The rate is the record's, not today's market rate. A deal priced in euros was won at
/// the rate its owner recorded; re-pricing history at a live rate would move closed revenue
/// every time the report is opened.</para>
/// </summary>
public static class MoneyAggregator
{
    public static MoneyTotal Sum(IEnumerable<MoneyValue> values, string? baseCurrency)
    {
        decimal total = 0;
        int converted = 0, excluded = 0;
        var excludedAmounts = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);

        foreach (var value in values)
        {
            if (MoneyMath.SameCurrency(value.CurrencyCode, null, baseCurrency))
            {
                total += value.Amount;
                continue;
            }

            if (value.ExchangeRate is { } rate and > 0)
            {
                total += MoneyMath.Round(value.Amount * rate, baseCurrency);
                converted++;
                continue;
            }

            excluded++;
            var key = value.CurrencyCode ?? "(unspecified)";
            excludedAmounts[key] = excludedAmounts.GetValueOrDefault(key) + value.Amount;
        }

        return new MoneyTotal(total, baseCurrency, converted, excluded, excludedAmounts);
    }
}
