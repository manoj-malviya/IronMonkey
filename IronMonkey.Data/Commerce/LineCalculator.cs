namespace IronMonkey.Data.Commerce;

/// <summary>One-off or repeating. Recurring value is reported separately from one-off value
/// so forecasting never has to infer it.</summary>
public enum ChargeType { OneOff = 0, Recurring = 1 }

/// <summary>
/// How often a recurring charge repeats. Subscription-per-month, tuition-per-semester and
/// premium-per-year are the same shape: a unit price, a number of periods, and a label.
/// </summary>
public enum BillingFrequency { None = 0, Monthly = 1, Quarterly = 2, PerTerm = 3, Annually = 4 }

/// <summary>The inputs that determine a line's value, in the order the rule applies them.</summary>
public readonly record struct LineInput(
    decimal Quantity,
    decimal UnitPrice,
    int Periods,
    decimal DiscountPercent,
    decimal TaxRatePercent,
    ChargeType ChargeType);

/// <summary>A line's computed money, every figure already rounded.</summary>
public readonly record struct LineAmounts(
    decimal Gross,
    decimal Discount,
    decimal Net,
    decimal Tax,
    decimal Total);

/// <summary>A document's or a deal's totals: the sums of its rounded lines.</summary>
public sealed record DealTotals(
    decimal Subtotal,
    decimal Discount,
    decimal Net,
    decimal Tax,
    decimal Total,
    decimal OneOffTotal,
    decimal RecurringTotal)
{
    public static DealTotals Zero { get; } = new(0, 0, 0, 0, 0, 0, 0);
}

/// <summary>
/// Computes line and deal totals. The one place a monetary total is produced: opportunity
/// line items, quote lines, the quote document and the UI all read figures produced here
/// rather than multiplying for themselves.
///
/// <para><b>The rule, per line:</b></para>
/// <list type="number">
/// <item><c>gross = round(unitPrice × quantity × periods)</c> — periods is 1 for a one-off
///   charge, and the contract length (12 months, 2 terms) for a recurring one.</item>
/// <item><c>discount = round(gross × discount% / 100)</c></item>
/// <item><c>net = gross − discount</c></item>
/// <item><c>tax = round(net × tax% / 100)</c> — tax on the discounted amount.</item>
/// <item><c>total = net + tax</c></item>
/// </list>
/// <para>Each rounding uses <see cref="MoneyMath.Round"/>. Deal totals are plain sums of the
/// already-rounded line figures and are never rounded again — that is the property that makes
/// a printed quote add up exactly when the customer checks it line by line.</para>
/// </summary>
public static class LineCalculator
{
    public static LineAmounts Compute(LineInput line, string? currencyCode)
    {
        var periods = line.ChargeType == ChargeType.Recurring ? Math.Max(1, line.Periods) : 1;

        var gross = MoneyMath.Round(line.UnitPrice * line.Quantity * periods, currencyCode);
        var discount = MoneyMath.Round(gross * line.DiscountPercent / 100m, currencyCode);
        var net = gross - discount;
        var tax = MoneyMath.Round(net * line.TaxRatePercent / 100m, currencyCode);

        return new LineAmounts(gross, discount, net, tax, net + tax);
    }

    public static DealTotals Sum(IEnumerable<(LineAmounts Amounts, ChargeType ChargeType)> lines)
    {
        decimal subtotal = 0, discount = 0, net = 0, tax = 0, total = 0, oneOff = 0, recurring = 0;

        foreach (var (amounts, chargeType) in lines)
        {
            subtotal += amounts.Gross;
            discount += amounts.Discount;
            net += amounts.Net;
            tax += amounts.Tax;
            total += amounts.Total;

            if (chargeType == ChargeType.Recurring) recurring += amounts.Total;
            else oneOff += amounts.Total;
        }

        return new DealTotals(subtotal, discount, net, tax, total, oneOff, recurring);
    }

    /// <summary>
    /// Validates a line's inputs. Returns an error message or null. Shared by every endpoint
    /// that accepts a line so the limits cannot drift between them.
    /// </summary>
    public static string? Validate(decimal quantity, decimal unitPrice, int periods,
        decimal discountPercent, decimal taxRatePercent)
    {
        if (quantity <= 0) return "Quantity must be greater than zero.";
        if (unitPrice < 0) return "Unit price cannot be negative.";
        if (periods < 1) return "A line must cover at least one period.";
        if (periods > 1200) return "A line cannot cover more than 1200 periods.";
        if (discountPercent is < 0 or > 100) return "Discount must be between 0 and 100 percent.";
        if (taxRatePercent is < 0 or > 100) return "Tax rate must be between 0 and 100 percent.";
        return null;
    }
}
