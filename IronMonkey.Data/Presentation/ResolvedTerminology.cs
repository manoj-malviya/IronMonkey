namespace IronMonkey.Data.Presentation;

/// <summary>
/// A tenant's terminology with every fallback already applied, so callers never branch on
/// "is this overridden?" and can never render an empty label.
///
/// Built once per request or per circuit and then read freely — resolution is pure and the
/// result is immutable.
/// </summary>
public sealed class ResolvedTerminology
{
    private readonly IReadOnlyDictionary<TerminologyTerm, (string Singular, string Plural)> _terms;

    private ResolvedTerminology(IReadOnlyDictionary<TerminologyTerm, (string, string)> terms)
        => _terms = terms;

    /// <summary>The built-in terminology, used when a tenant has configured none.</summary>
    public static ResolvedTerminology Default { get; } = From(null);

    /// <summary>
    /// Applies <paramref name="overrides"/> over the built-in defaults. A null section, a
    /// null form, or a blank/whitespace string all mean "not overridden" — a tenant that
    /// saves an empty box gets the default back rather than a blank label.
    /// </summary>
    public static ResolvedTerminology From(TenantTerminology? overrides)
    {
        var resolved = new Dictionary<TerminologyTerm, (string, string)>();

        foreach (var term in TerminologyDefaults.All)
        {
            var over = Lookup(overrides, term);

            var singular = Coalesce(over?.Singular, TerminologyDefaults.Singular(term));
            var plural = Coalesce(over?.Plural, TerminologyDefaults.Plural(term));

            resolved[term] = (singular, plural);
        }

        return new ResolvedTerminology(resolved);
    }

    public string Singular(TerminologyTerm term) => _terms[term].Singular;

    public string Plural(TerminologyTerm term) => _terms[term].Plural;

    /// <summary>Singular or plural by count, for "1 Enquiry" / "4 Enquiries".</summary>
    public string ForCount(TerminologyTerm term, int count) =>
        count == 1 ? Singular(term) : Plural(term);

    private static string Coalesce(string? candidate, string fallback) =>
        string.IsNullOrWhiteSpace(candidate) ? fallback : candidate.Trim();

    private static TermOverride? Lookup(TenantTerminology? t, TerminologyTerm term) => term switch
    {
        TerminologyTerm.Lead => t?.Lead,
        TerminologyTerm.Contact => t?.Contact,
        TerminologyTerm.Opportunity => t?.Opportunity,
        TerminologyTerm.Task => t?.Task,
        TerminologyTerm.Pipeline => t?.Pipeline,
        TerminologyTerm.Stage => t?.Stage,
        TerminologyTerm.Activity => t?.Activity,
        _ => null
    };
}
