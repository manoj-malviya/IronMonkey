namespace IronMonkey.Web.Commerce;

// Wire shapes for the catalog, line-item, quote and revenue endpoints. Mirrors the API
// contracts. Every monetary figure here was computed and rounded by the API's
// LineCalculator — the UI formats it, and never re-computes or re-rounds it.

public sealed record PriceVersion(
    Guid Id, Guid PriceListId, string PriceListName, string? CurrencyCode,
    decimal UnitPrice, decimal? UnitCost, DateTime EffectiveFrom, DateTime? EffectiveTo, bool IsCurrent);

public sealed record ProductItem(
    Guid Id, string Code, string Name, string? Description, string? Category, bool IsActive,
    string ChargeType, string BillingFrequency, int DefaultPeriods, string? UnitOfMeasure,
    decimal DefaultTaxRatePercent, decimal? ListPrice, decimal? ListCost, string? ListCurrencyCode,
    Dictionary<string, object?> CustomFields, List<PriceVersion> Prices);

public sealed record SaveProductRequest(
    string Code, string Name, string? Description, string? Category,
    string ChargeType, string? BillingFrequency, int? DefaultPeriods, string? UnitOfMeasure,
    decimal? DefaultTaxRatePercent, decimal? ListPrice, decimal? ListCost, bool? IsActive,
    Dictionary<string, object?>? CustomFields);

public sealed record AddPriceRequest(Guid? PriceListId, decimal UnitPrice, decimal? UnitCost, DateTime? EffectiveFrom);

public sealed record PriceListItem(Guid Id, string Name, string? Description, string? CurrencyCode, bool IsDefault, bool IsActive);

public sealed record SavePriceListRequest(string Name, string? Description, string? CurrencyCode, bool? IsDefault, bool? IsActive);

public sealed record LineItem(
    Guid Id, int Position, Guid? ProductId, Guid? ProductPriceId, Guid? PriceListId,
    string Description, string? ProductCode, string? Category,
    decimal Quantity, decimal UnitPrice, string ChargeType, string BillingFrequency, int Periods,
    decimal DiscountPercent, decimal TaxRatePercent,
    decimal GrossAmount, decimal DiscountAmount, decimal NetAmount, decimal TaxAmount, decimal TotalAmount,
    bool IsMigrated);

public sealed record Totals(
    decimal Subtotal, decimal Discount, decimal Net, decimal Tax, decimal Total,
    decimal OneOffTotal, decimal RecurringTotal);

public sealed record DealEconomics(
    Guid OpportunityId, string? CurrencyCode, string? EffectiveCurrencyCode, int MinorUnits,
    decimal? ExchangeRate, DateOnly? ExchangeRateDate, Guid? PriceListId,
    List<LineItem> Lines, Totals Totals);

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

public sealed record QuoteLine(
    int Position, Guid? ProductId, string Description, string? ProductCode, string? Category,
    decimal Quantity, decimal UnitPrice, string ChargeType, string BillingFrequency, int Periods,
    decimal DiscountPercent, decimal TaxRatePercent,
    decimal GrossAmount, decimal DiscountAmount, decimal NetAmount, decimal TaxAmount, decimal TotalAmount);

public sealed record QuoteHistory(
    string? FromStatus, string ToStatus, Guid? ChangedByUserId, string? ChangedByName, string? Note, DateTime ChangedAt);

public sealed record QuoteShareLink(Guid Id, DateTime CreatedAt, DateTime ExpiresAt, DateTime? RevokedAt,
    DateTime? LastViewedAt, int ViewCount, bool IsUsable);

public sealed record Quote(
    Guid Id, Guid OpportunityId, string Number, int Version, string Status, bool IsEditable,
    string Title, string? CurrencyCode, string? EffectiveCurrencyCode, int MinorUnits,
    DateOnly ValidUntil, string? Terms, string RecipientName, string? RecipientEmail,
    decimal Subtotal, decimal DiscountTotal, decimal TaxTotal, decimal Total, decimal OneOffTotal, decimal RecurringTotal,
    decimal MaxDiscountPercent, bool RequiresApproval, bool IsAwaitingApproval, Guid? ApprovedByUserId, DateTime? ApprovedAt,
    Guid? SupersedesQuoteId, Guid? SupersededByQuoteId,
    DateTime CreatedAt, DateTime? SentAt, DateTime? RespondedAt, string? RespondedByName, string? ResponseNote,
    List<QuoteLine> Lines, List<QuoteHistory> History, List<QuoteShareLink> ShareLinks);

public sealed record QuoteSummary(
    Guid Id, string Number, int Version, string Status, string Title, string? CurrencyCode,
    decimal Total, DateOnly ValidUntil, bool IsAwaitingApproval, DateTime CreatedAt, DateTime? SentAt);

public sealed record CreateQuoteRequest(Guid OpportunityId, string? Title, DateOnly? ValidUntil, string? Terms);
public sealed record UpdateQuoteRequest(string Title, DateOnly ValidUntil, string? Terms);
public sealed record SendQuoteRequest(bool? DeliverByEmail);
public sealed record SendQuoteResponse(Quote Quote, string ShareUrl, string? DeliveryStatus, string? DeliveryError);
public sealed record RespondQuoteRequest(bool Accept, string RespondedByName, string? Note);
public sealed record ShareLinkCreated(QuoteShareLink Link, string Url);

public sealed record QuoteSettings(string NumberPrefix, int DefaultValidityDays, string? DefaultTerms,
    decimal? ApprovalDiscountThresholdPercent, int ShareLinkLifetimeDays);

public sealed record ProductRevenue(
    Guid? ProductId, string Name, string? Code, string Category,
    decimal Quantity, int DealCount, decimal Revenue, decimal OneOffRevenue, decimal RecurringRevenue,
    decimal ListValue, decimal Discount, decimal DiscountRatePercent,
    int WonDeals, int LostDeals, decimal? WinRatePercent);

public sealed record CategoryRevenue(string Category, decimal Revenue, decimal OneOffRevenue,
    decimal RecurringRevenue, decimal Discount, int DealCount);

public sealed record RecurrenceRevenue(string BillingFrequency, decimal Revenue, int LineCount);

public sealed record RevenueReport(
    DateTime RangeFrom, DateTime RangeTo, string Preset, string ScopeLabel,
    string? CurrencyCode,
    int WonDealCount, decimal WonRevenue, decimal WonOneOffRevenue, decimal WonRecurringRevenue,
    int OpenDealCount, decimal OpenPipelineValue, decimal OpenRecurringValue,
    decimal WonListValue, decimal WonDiscount, decimal WonDiscountRatePercent,
    List<ProductRevenue> ByProduct,
    List<CategoryRevenue> ByCategory,
    List<RecurrenceRevenue> RecurringByFrequency,
    int UnconvertedDealCount,
    Dictionary<string, decimal> Unconverted,
    DateTime GeneratedAt);

public static class CommerceLabels
{
    public static string Recurrence(string chargeType, string frequency, int periods)
    {
        if (chargeType != "Recurring") return "One-off";
        var unit = frequency switch
        {
            "Monthly" => "month",
            "Quarterly" => "quarter",
            "PerTerm" => "term",
            "Annually" => "year",
            _ => "period"
        };
        return periods == 1 ? $"Per {unit} × 1" : $"Per {unit} × {periods}";
    }

    public static string StatusClass(string status) => status switch
    {
        "Draft" => "bg-slate-100 text-slate-700",
        "Sent" => "bg-indigo-100 text-indigo-800",
        "Accepted" => "bg-emerald-100 text-emerald-800",
        "Rejected" => "bg-red-100 text-red-800",
        "Expired" => "bg-amber-100 text-amber-800",
        "Superseded" => "bg-slate-200 text-slate-600",
        _ => "bg-slate-100 text-slate-700"
    };

    /// <summary>Reads a ProblemDetails "detail" or a plain-string error body.</summary>
    public static async Task<string> ErrorAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(body)) return $"Request failed ({(int)response.StatusCode}).";
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.String) return doc.RootElement.GetString()!;
            if (doc.RootElement.TryGetProperty("detail", out var detail) && detail.GetString() is { Length: > 0 } d) return d;
            if (doc.RootElement.TryGetProperty("title", out var title) && title.GetString() is { Length: > 0 } t) return t;
        }
        catch (System.Text.Json.JsonException) { }
        return body.Length > 300 ? body[..300] : body;
    }
}
