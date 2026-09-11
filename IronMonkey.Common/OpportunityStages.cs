namespace IronMonkey.Common;

/// <summary>
/// Opportunity.Stage is a free-text column rather than an enum, so the allowed values are
/// pinned here — the API validates against this list and the UI renders the same order.
/// "Won" and "Lost" are terminal; MarkAsLost sets "Lost" and requires a loss reason.
/// </summary>
public static class OpportunityStages
{
    public const string Qualification = "Qualification";
    public const string Proposal = "Proposal";
    public const string Negotiation = "Negotiation";
    public const string Won = "Won";
    public const string Lost = "Lost";

    public static readonly IReadOnlyList<string> All =
        [Qualification, Proposal, Negotiation, Won, Lost];

    public static bool IsValid(string stage) => All.Contains(stage);

    public static bool IsTerminal(string stage) => stage is Won or Lost;
}
