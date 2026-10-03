namespace IronMonkey.Common;

/// <summary>
/// The default opportunity stage set.
///
/// This used to be the authoritative list of allowed values for a free-text
/// <c>Opportunity.Stage</c> column, validated on write and matched by name for won/lost.
/// Opportunity stages are now tenant-configured rows in <c>pipeline_stages</c> like lead
/// stages, so nothing validates against this list any more and nothing decides terminality
/// from a name here.
///
/// What remains is the seed: the stage set a tenant starts with when a recipe does not
/// define one, and the set the migration seeds for existing tenants so their rows have
/// somewhere to land. A tenant is free to rename, reorder, add to or deactivate every one of
/// these immediately afterwards — so no code outside seeding and migration may reference
/// these names.
/// </summary>
public static class OpportunityStages
{
    public const string Qualification = "Qualification";
    public const string Proposal = "Proposal";
    public const string Negotiation = "Negotiation";
    public const string Won = "Won";
    public const string Lost = "Lost";

    /// <param name="Name">The seeded display name, which the tenant may change.</param>
    /// <param name="Order">Position in the default sequence, 1-based.</param>
    /// <param name="StageType">
    /// Mirrors <c>IronMonkey.Data.Entities.StageType</c> by name. Common cannot reference
    /// Data, so it is carried as a string and parsed at the seeding site — the same way
    /// recipe stage definitions already travel.
    /// </param>
    public sealed record Definition(string Name, int Order, string StageType);

    /// <summary>
    /// The default sequence, with the stage types that make won/lost explicit. These types —
    /// not the names — are what close logic, reporting and the dashboard read.
    /// </summary>
    public static readonly IReadOnlyList<Definition> Defaults =
    [
        new(Qualification, 1, "Entry"),
        new(Proposal, 2, "Active"),
        new(Negotiation, 3, "Active"),
        new(Won, 4, "ClosedWon"),
        new(Lost, 5, "ClosedLost")
    ];

    /// <summary>Default stage names in order, for the migration's backfill mapping.</summary>
    public static readonly IReadOnlyList<string> All =
        [Qualification, Proposal, Negotiation, Won, Lost];
}
