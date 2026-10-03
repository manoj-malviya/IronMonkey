using IronMonkey.Data.Abstractions;
using IronMonkey.Data.Commerce;

namespace IronMonkey.Data.Entities;

public sealed class Opportunity : BaseTenantEntity
{
    private Opportunity(Guid id, Guid tenantId, string title, Guid contactId, DateTime expectedCloseDate, Guid pipelineStageId)
        : base(id, tenantId)
    {
        Title = title;
        ContactId = contactId;
        ExpectedCloseDate = expectedCloseDate;
        PipelineStageId = pipelineStageId;
    }

    private Opportunity()
    {
    }

    public string Title { get; private set; } = string.Empty;

    /// <summary>
    /// The user who owns this record, for record-level visibility. Null means unowned, which
    /// only an All scope can see. Backfilled from the converting lead's assignee by the
    /// <c>RecordVisibility</c> migration; set to the creator on create.
    /// </summary>
    public Guid? OwnerUserId { get; private set; }

    public void AssignOwner(Guid? ownerUserId) => OwnerUserId = ownerUserId;
    public Guid ContactId { get; private set; }
    public DateTime ExpectedCloseDate { get; private set; }

    /// <summary>
    /// The tenant-configured stage this opportunity sits in. Replaces the former free-text
    /// <c>Stage</c> column: a string could hold anything, could not be renamed without
    /// rewriting every row, and forced won/lost to be decided by matching a magic name.
    /// </summary>
    public Guid PipelineStageId { get; private set; }

    /// <summary>
    /// The pipeline this deal runs in. Non-nullable and denormalised beside the stage for
    /// the same reasons as <see cref="Lead.PipelineId"/>: a deal outside every pipeline is
    /// not a state worth supporting, and a pipeline-scoped dashboard aggregate must not have
    /// to join the stage table for every row it sums.
    /// </summary>
    public Guid PipelineId { get; private set; }

    public string? LossReason { get; private set; }

    /// <summary>
    /// The deal's total value: the sum of its line totals, computed by
    /// <see cref="LineCalculator"/>. Persisted so dashboards can aggregate in SQL, but never
    /// written directly — only <see cref="RecalculateTotals"/> sets it. Before line items this
    /// was a free decimal; the <c>ProductsAndQuotes</c> migration moved every such value onto
    /// a single line so no deal lost its value.
    /// </summary>
    public decimal Amount { get; private set; } = 0m;

    /// <summary>Total of one-off lines. With <see cref="RecurringAmount"/>, sums to <see cref="Amount"/>.</summary>
    public decimal OneOffAmount { get; private set; }

    /// <summary>Total contract value of recurring lines (unit price × quantity × periods).</summary>
    public decimal RecurringAmount { get; private set; }

    public decimal DiscountAmount { get; private set; }
    public decimal TaxAmount { get; private set; }

    /// <summary>
    /// ISO 4217 code of every monetary value on this deal. <b>Null means the tenant's own
    /// currency</b> — which is what every deal created before currencies existed meant, and
    /// what lets them keep aggregating with each other unchanged. A deal in any other currency
    /// is only ever summed with the tenant's figures through <see cref="ExchangeRate"/>.
    /// </summary>
    public string? CurrencyCode { get; private set; }

    /// <summary>
    /// Units of the tenant base currency per one unit of <see cref="CurrencyCode"/>, as
    /// recorded by a user. Null = no conversion recorded, so reports exclude this deal from
    /// cross-currency totals and say so, rather than adding it at face value.
    /// </summary>
    public decimal? ExchangeRate { get; private set; }

    /// <summary>The date the recorded rate applies to — a rate without its date cannot be audited.</summary>
    public DateOnly? ExchangeRateDate { get; private set; }

    /// <summary>Price list chosen for this deal (an agreement or segment). Null = tenant default.</summary>
    public Guid? PriceListId { get; private set; }

    private readonly List<OpportunityLineItem> _lineItems = [];
    public IReadOnlyCollection<OpportunityLineItem> LineItems => _lineItems;

    // Navigation properties
    public Contact Contact { get; private set; } = null!;
    public PipelineStage Stage { get; private set; } = null!;
    public Pipeline Pipeline { get; private set; } = null!;

    public static Opportunity Create(Guid tenantId, string title, Guid contactId, DateTime expectedCloseDate, Guid pipelineStageId)
    {
        return new Opportunity(Guid.NewGuid(), tenantId, title, contactId, expectedCloseDate, pipelineStageId);
    }

    public void UpdateOpportunity(string title, Guid contactId, DateTime expectedCloseDate, Guid pipelineStageId)
    {
        Title = title;
        ContactId = contactId;
        ExpectedCloseDate = expectedCloseDate;
        PipelineStageId = pipelineStageId;
    }

    /// <summary>
    /// Closes the deal as lost against a stage the caller has already resolved by
    /// <see cref="StageType.ClosedLost"/>. The stage id is a parameter rather than a name
    /// looked up here because the entity has no database access — and because the whole
    /// point of the change is that "Lost" is no longer a name this code may assume exists.
    /// A tenant that renamed it to "Declined" closes deals through the same path.
    /// </summary>
    public void MarkAsLost(Guid lostStageId, string lossReason)
    {
        LossReason = lossReason;
        PipelineStageId = lostStageId;
    }

    /// <summary>Moves the deal to a stage within its current pipeline. Does not change
    /// <see cref="PipelineId"/> — see <see cref="MoveToPipeline"/>.</summary>
    public void MoveToPipelineStage(Guid stageId) => PipelineStageId = stageId;

    /// <summary>
    /// Moves the deal into a different pipeline, landing on an explicitly chosen stage of
    /// that pipeline. Set together for the reason given on <see cref="Lead.MoveToPipeline"/>:
    /// a deal left on its old stage after a pipeline change is counted in the wrong
    /// pipeline's value totals and shows on neither board.
    /// </summary>
    public void MoveToPipeline(Guid pipelineId, Guid stageId)
    {
        PipelineId = pipelineId;
        PipelineStageId = stageId;
    }

    /// <summary>Places the deal in a pipeline at creation or during seeding.</summary>
    public void SetPipeline(Guid pipelineId) => PipelineId = pipelineId;

    /// <summary>
    /// Clears a loss reason carried over from an earlier close. Reopening a deal that keeps
    /// the old reason attached reads, in every report, as though it were still lost.
    /// </summary>
    public void ClearLossReason() => LossReason = null;

    /// <summary>
    /// Replaces the deal's lines with one free-text one-off line worth <paramref name="amount"/>.
    /// This is the lump-sum path — a caller with a single figure (lead conversion, a quick
    /// create) still gets a deal whose value is computed from lines, never a bare decimal.
    /// </summary>
    public void SetAmount(decimal amount, string description = "Deal value")
    {
        _lineItems.Clear();
        _lineItems.Add(OpportunityLineItem.LumpSum(TenantId, Id, amount, description));
        RecalculateTotals();
    }

    public OpportunityLineItem AddLine(LineDetails details)
    {
        var position = _lineItems.Count == 0 ? 1 : _lineItems.Max(l => l.Position) + 1;
        var line = OpportunityLineItem.Create(TenantId, Id, position, details);
        _lineItems.Add(line);
        RecalculateTotals();
        return line;
    }

    public bool RemoveLine(Guid lineId)
    {
        var line = _lineItems.FirstOrDefault(l => l.Id == lineId);
        if (line is null) return false;

        _lineItems.Remove(line);
        var position = 1;
        foreach (var remaining in _lineItems.OrderBy(l => l.Position)) remaining.MoveTo(position++);
        RecalculateTotals();
        return true;
    }

    /// <summary>
    /// Recomputes every line and the deal totals through <see cref="LineCalculator"/>. Called
    /// by every method that changes a line, so the stored totals cannot drift from the lines.
    /// </summary>
    public DealTotals RecalculateTotals()
    {
        var totals = LineCalculator.Sum(_lineItems.Select(l => (l.Recalculate(CurrencyCode), l.ChargeType)));

        Amount = totals.Total;
        OneOffAmount = totals.OneOffTotal;
        RecurringAmount = totals.RecurringTotal;
        DiscountAmount = totals.Discount;
        TaxAmount = totals.Tax;
        return totals;
    }

    /// <summary>
    /// Sets the deal's currency and re-rounds the lines to its minor unit. The endpoint
    /// refuses the change while catalog-priced lines exist, because their prices came from
    /// a price list in the old currency; this method only records the code.
    /// </summary>
    public void SetCurrency(string? currencyCode)
    {
        CurrencyCode = currencyCode;
        RecalculateTotals();
    }

    public void SetExchangeRate(decimal? rate, DateOnly? rateDate)
    {
        ExchangeRate = rate;
        ExchangeRateDate = rate is null ? null : rateDate;
    }

    public void SetPriceList(Guid? priceListId) => PriceListId = priceListId;
}
