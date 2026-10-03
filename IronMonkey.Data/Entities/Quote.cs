using IronMonkey.Data.Abstractions;
using IronMonkey.Data.Commerce;

namespace IronMonkey.Data.Entities;

public enum QuoteStatus { Draft = 0, Sent = 1, Accepted = 2, Rejected = 3, Expired = 4, Superseded = 5 }

/// <summary>
/// A numbered, versioned offer generated from an opportunity.
///
/// <para><b>Immutable once sent.</b> A quote's lines and totals are a snapshot taken when the
/// draft is generated (or refreshed), copied into <see cref="QuoteLine"/> rows. Every mutator
/// on this type refuses anything but a <see cref="QuoteStatus.Draft"/>: editing a sent quote
/// in place destroys the record of what the customer was actually offered. A change after
/// sending is a new version (<see cref="Revise"/>), which supersedes this one explicitly when
/// it is itself sent — the history then shows both.</para>
///
/// <para>Quote number is shared across versions (Q-00012 v1, v2); <c>(TenantId, Number,
/// Version)</c> is uniquely indexed, so two concurrent revisions cannot both become v2.</para>
/// </summary>
public sealed class Quote : BaseTenantEntity
{
    private Quote() { }

    public Guid OpportunityId { get; private set; }
    public int Number { get; private set; }
    public int Version { get; private set; } = 1;

    /// <summary>The prefix in force when the quote was created, e.g. "Q-". Copied so that
    /// changing the tenant's prefix does not renumber documents already issued.</summary>
    public string NumberPrefix { get; private set; } = "Q-";

    public QuoteStatus Status { get; private set; } = QuoteStatus.Draft;

    public string Title { get; private set; } = string.Empty;
    public string? CurrencyCode { get; private set; }
    public DateOnly ValidUntil { get; private set; }
    public string? Terms { get; private set; }

    /// <summary>The recipient's name as it stood when the quote was generated.</summary>
    public string RecipientName { get; private set; } = string.Empty;
    public string? RecipientEmail { get; private set; }

    public decimal Subtotal { get; private set; }
    public decimal DiscountTotal { get; private set; }
    public decimal TaxTotal { get; private set; }
    public decimal Total { get; private set; }
    public decimal OneOffTotal { get; private set; }
    public decimal RecurringTotal { get; private set; }

    /// <summary>Largest line discount on the quote — what approval rules are judged against.</summary>
    public decimal MaxDiscountPercent { get; private set; }

    public bool RequiresApproval { get; private set; }
    public Guid? ApprovedByUserId { get; private set; }
    public DateTime? ApprovedAt { get; private set; }

    public Guid? SupersedesQuoteId { get; private set; }
    public Guid? SupersededByQuoteId { get; private set; }

    public Guid CreatedByUserId { get; private set; }
    public DateTime? SentAt { get; private set; }
    public DateTime? RespondedAt { get; private set; }

    /// <summary>The name typed by whoever accepted or rejected — a customer on the shared page
    /// has no user id, so their response is recorded by the name they gave.</summary>
    public string? RespondedByName { get; private set; }
    public string? ResponseNote { get; private set; }

    private readonly List<QuoteLine> _lines = [];
    public IReadOnlyCollection<QuoteLine> Lines => _lines;

    private readonly List<QuoteStatusChange> _statusChanges = [];
    public IReadOnlyCollection<QuoteStatusChange> StatusChanges => _statusChanges;

    public string DisplayNumber => $"{NumberPrefix}{Number:D5}";
    public string DisplayNumberWithVersion => $"{DisplayNumber} v{Version}";

    public bool IsEditable => Status == QuoteStatus.Draft;

    public static Quote CreateDraft(Opportunity opportunity, int number, string prefix, string title,
        string recipientName, string? recipientEmail, DateOnly validUntil, string? terms,
        decimal? approvalThresholdPercent, Guid createdByUserId, DateTime nowUtc)
    {
        var quote = new Quote
        {
            Id = Guid.NewGuid(),
            TenantId = opportunity.TenantId,
            OpportunityId = opportunity.Id,
            Number = number,
            Version = 1,
            NumberPrefix = prefix,
            CreatedByUserId = createdByUserId
        };
        quote.SetHeader(title, recipientName, recipientEmail, validUntil, terms);
        quote.Snapshot(opportunity, approvalThresholdPercent);
        quote.RecordStatus(null, QuoteStatus.Draft, createdByUserId, null, "Draft created", nowUtc);
        return quote;
    }

    /// <summary>
    /// Creates the next version as a new draft that will supersede this one when sent.
    /// Lines are re-snapshotted from the opportunity as it stands now, so the revision offers
    /// the deal's current shape; this quote's own lines are left exactly as they were.
    /// </summary>
    public Quote Revise(Opportunity opportunity, int nextVersion, decimal? approvalThresholdPercent,
        Guid userId, DateTime nowUtc, DateOnly validUntil)
    {
        var next = new Quote
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            OpportunityId = OpportunityId,
            Number = Number,
            Version = nextVersion,
            NumberPrefix = NumberPrefix,
            CreatedByUserId = userId,
            SupersedesQuoteId = Id
        };
        next.SetHeader(Title, opportunity.Contact?.Name ?? RecipientName,
            opportunity.Contact?.Email ?? RecipientEmail, validUntil, Terms);
        next.Snapshot(opportunity, approvalThresholdPercent);
        next.RecordStatus(null, QuoteStatus.Draft, userId, null, $"Revision of v{Version}", nowUtc);
        return next;
    }

    public QuoteResult UpdateDraft(string title, DateOnly validUntil, string? terms)
    {
        if (!IsEditable) return QuoteResult.Fail(NotEditableMessage);
        SetHeader(title, RecipientName, RecipientEmail, validUntil, terms);
        return QuoteResult.Ok;
    }

    /// <summary>
    /// Re-copies lines and totals from the opportunity. Clears any approval: what was
    /// approved is no longer what would be sent.
    /// </summary>
    public QuoteResult RefreshFrom(Opportunity opportunity, decimal? approvalThresholdPercent)
    {
        if (!IsEditable) return QuoteResult.Fail(NotEditableMessage);
        RecipientName = opportunity.Contact?.Name ?? RecipientName;
        RecipientEmail = opportunity.Contact?.Email ?? RecipientEmail;
        Snapshot(opportunity, approvalThresholdPercent);
        return QuoteResult.Ok;
    }

    public QuoteResult Approve(Guid approverUserId, DateTime nowUtc)
    {
        if (!IsEditable) return QuoteResult.Fail("Only a draft quote can be approved.");
        if (!RequiresApproval) return QuoteResult.Fail("This quote does not require approval.");

        ApprovedByUserId = approverUserId;
        ApprovedAt = nowUtc;
        _statusChanges.Add(QuoteStatusChange.Create(this, Status, Status, approverUserId, null, "Approved", nowUtc));
        return QuoteResult.Ok;
    }

    public bool IsAwaitingApproval => RequiresApproval && ApprovedAt is null;

    public QuoteResult MarkSent(Guid userId, DateTime nowUtc)
    {
        if (Status != QuoteStatus.Draft) return QuoteResult.Fail("Only a draft quote can be sent.");
        if (IsAwaitingApproval)
            return QuoteResult.Fail("This quote's discount exceeds the approval threshold and must be approved before it is sent.");
        if (_lines.Count == 0) return QuoteResult.Fail("A quote with no lines cannot be sent.");

        SentAt = nowUtc;
        RecordStatus(Status, QuoteStatus.Sent, userId, null, null, nowUtc);
        return QuoteResult.Ok;
    }

    /// <summary>Marks this version replaced by a newer sent version.</summary>
    public void MarkSuperseded(Guid supersededById, Guid? userId, DateTime nowUtc)
    {
        SupersededByQuoteId = supersededById;
        if (Status is QuoteStatus.Draft or QuoteStatus.Sent)
            RecordStatus(Status, QuoteStatus.Superseded, userId, null, "Superseded by a newer version", nowUtc);
    }

    public QuoteResult Accept(Guid? userId, string respondedByName, string? note, DateTime nowUtc) =>
        Respond(QuoteStatus.Accepted, userId, respondedByName, note, nowUtc);

    public QuoteResult Reject(Guid? userId, string respondedByName, string? note, DateTime nowUtc) =>
        Respond(QuoteStatus.Rejected, userId, respondedByName, note, nowUtc);

    /// <summary>
    /// Marks a sent quote expired once its validity has passed. Returns true if it changed.
    /// Called lazily wherever a quote is read for a response, so an expired quote can never
    /// be accepted even if no background sweep has run.
    /// </summary>
    public bool ExpireIfPastValidity(DateOnly today, DateTime nowUtc)
    {
        if (Status != QuoteStatus.Sent || today <= ValidUntil) return false;
        RecordStatus(Status, QuoteStatus.Expired, null, null, "Validity date passed", nowUtc);
        return true;
    }

    private QuoteResult Respond(QuoteStatus target, Guid? userId, string respondedByName, string? note, DateTime nowUtc)
    {
        if (Status != QuoteStatus.Sent)
            return QuoteResult.Fail(Status switch
            {
                QuoteStatus.Superseded => "This quote has been replaced by a newer version.",
                QuoteStatus.Expired => "This quote has expired.",
                QuoteStatus.Draft => "This quote has not been sent.",
                _ => "This quote has already been responded to."
            });

        RespondedAt = nowUtc;
        RespondedByName = respondedByName.Trim();
        ResponseNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        RecordStatus(Status, target, userId, RespondedByName, ResponseNote, nowUtc);
        return QuoteResult.Ok;
    }

    private void SetHeader(string title, string recipientName, string? recipientEmail, DateOnly validUntil, string? terms)
    {
        Title = title.Trim();
        RecipientName = recipientName.Trim();
        RecipientEmail = string.IsNullOrWhiteSpace(recipientEmail) ? null : recipientEmail.Trim();
        ValidUntil = validUntil;
        Terms = string.IsNullOrWhiteSpace(terms) ? null : terms.Trim();
    }

    private void Snapshot(Opportunity opportunity, decimal? approvalThresholdPercent)
    {
        CurrencyCode = opportunity.CurrencyCode;
        _lines.Clear();

        foreach (var source in opportunity.LineItems.OrderBy(l => l.Position))
            _lines.Add(QuoteLine.CopyOf(this, source, CurrencyCode));

        var totals = LineCalculator.Sum(_lines.Select(l => (l.Amounts, l.ChargeType)));
        Subtotal = totals.Subtotal;
        DiscountTotal = totals.Discount;
        TaxTotal = totals.Tax;
        Total = totals.Total;
        OneOffTotal = totals.OneOffTotal;
        RecurringTotal = totals.RecurringTotal;

        MaxDiscountPercent = _lines.Count == 0 ? 0 : _lines.Max(l => l.DiscountPercent);
        RequiresApproval = approvalThresholdPercent is { } threshold && MaxDiscountPercent > threshold;
        ApprovedByUserId = null;
        ApprovedAt = null;
    }

    private void RecordStatus(QuoteStatus? from, QuoteStatus to, Guid? userId, string? actorName, string? note, DateTime nowUtc)
    {
        Status = to;
        _statusChanges.Add(QuoteStatusChange.Create(this, from, to, userId, actorName, note, nowUtc));
    }

    private const string NotEditableMessage =
        "A sent quote cannot be edited. Create a new version to change what is offered.";
}

/// <summary>Outcome of a quote state change: refused with a reason, or done.</summary>
public sealed record QuoteResult(bool Succeeded, string? Error)
{
    public static QuoteResult Ok { get; } = new(true, null);
    public static QuoteResult Fail(string error) => new(false, error);
}

/// <summary>
/// One frozen line of a quote. Copied from an <see cref="OpportunityLineItem"/>, including
/// its computed figures, and never recalculated afterwards — a later price change, catalog
/// rename or even a change to the rounding code cannot move a number on a sent document.
/// </summary>
public sealed class QuoteLine : BaseTenantEntity
{
    private QuoteLine() { }

    public Guid QuoteId { get; private set; }
    public int Position { get; private set; }
    public Guid? ProductId { get; private set; }
    public Guid? ProductPriceId { get; private set; }
    public string Description { get; private set; } = string.Empty;
    public string? ProductCode { get; private set; }
    public string? Category { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal UnitPrice { get; private set; }
    public ChargeType ChargeType { get; private set; }
    public BillingFrequency BillingFrequency { get; private set; }
    public int Periods { get; private set; }
    public decimal DiscountPercent { get; private set; }
    public decimal TaxRatePercent { get; private set; }
    public decimal GrossAmount { get; private set; }
    public decimal DiscountAmount { get; private set; }
    public decimal NetAmount { get; private set; }
    public decimal TaxAmount { get; private set; }
    public decimal TotalAmount { get; private set; }

    public LineAmounts Amounts => new(GrossAmount, DiscountAmount, NetAmount, TaxAmount, TotalAmount);

    internal static QuoteLine CopyOf(Quote quote, OpportunityLineItem source, string? currencyCode)
    {
        // Recomputed through the calculator rather than copying the source's stored figures,
        // so a line whose stored totals predate a currency change is still frozen correctly.
        var amounts = LineCalculator.Compute(source.ToInput(), currencyCode);

        return new QuoteLine
        {
            Id = Guid.NewGuid(),
            TenantId = quote.TenantId,
            QuoteId = quote.Id,
            Position = source.Position,
            ProductId = source.ProductId,
            ProductPriceId = source.ProductPriceId,
            Description = source.Description,
            ProductCode = source.ProductCode,
            Category = source.Category,
            Quantity = source.Quantity,
            UnitPrice = source.UnitPrice,
            ChargeType = source.ChargeType,
            BillingFrequency = source.BillingFrequency,
            Periods = source.Periods,
            DiscountPercent = source.DiscountPercent,
            TaxRatePercent = source.TaxRatePercent,
            GrossAmount = amounts.Gross,
            DiscountAmount = amounts.Discount,
            NetAmount = amounts.Net,
            TaxAmount = amounts.Tax,
            TotalAmount = amounts.Total
        };
    }
}

/// <summary>Who moved a quote between statuses, and when. Append-only.</summary>
public sealed class QuoteStatusChange : BaseTenantEntity
{
    private QuoteStatusChange() { }

    public Guid QuoteId { get; private set; }
    public QuoteStatus? FromStatus { get; private set; }
    public QuoteStatus ToStatus { get; private set; }

    /// <summary>The CRM user, or null for the customer on the shared page and for the system.</summary>
    public Guid? ChangedByUserId { get; private set; }

    /// <summary>The name a customer gave when responding through a shared link.</summary>
    public string? ChangedByName { get; private set; }
    public string? Note { get; private set; }
    public DateTime ChangedAt { get; private set; }

    internal static QuoteStatusChange Create(Quote quote, QuoteStatus? from, QuoteStatus to,
        Guid? userId, string? actorName, string? note, DateTime nowUtc) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = quote.TenantId,
        QuoteId = quote.Id,
        FromStatus = from,
        ToStatus = to,
        ChangedByUserId = userId,
        ChangedByName = actorName,
        Note = note,
        ChangedAt = nowUtc
    };
}
