namespace IronMonkey.Data.Presentation;

/// <summary>
/// The tenant's own words for the core CRM nouns.
///
/// One deployment serves many verticals: a dealership calls a Lead an "Enquiry", a university
/// calls it an "Applicant", a clinic a "Referral". Only the presentation layer changes — routes,
/// permission strings, API contracts and database columns keep the built-in names, because a
/// per-tenant wire contract would be untestable and would break every integration.
///
/// Singular and plural are stored separately and never derived. Appending "s" produces
/// "Enquirys" and "Opportunitys", and plenty of terms a tenant will choose do not pluralize
/// regularly at all.
///
/// A term the tenant has not overridden is null here and resolves to the built-in default;
/// see <see cref="TerminologyDefaults"/>. A blank override is treated as absent rather than
/// rendering an empty label.
/// </summary>
public sealed class TenantTerminology
{
    public TermOverride? Lead { get; set; }
    public TermOverride? Contact { get; set; }
    public TermOverride? Opportunity { get; set; }
    public TermOverride? Task { get; set; }
    public TermOverride? Pipeline { get; set; }
    public TermOverride? Stage { get; set; }
    public TermOverride? Activity { get; set; }
}

/// <summary>
/// One overridden noun. Both forms are optional: a tenant may rename only the singular and
/// keep the default plural, so each side falls back independently.
/// </summary>
public sealed class TermOverride
{
    public string? Singular { get; set; }
    public string? Plural { get; set; }
}
