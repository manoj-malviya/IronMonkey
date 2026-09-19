namespace IronMonkey.Data.Presentation;

/// <summary>
/// The built-in English names, used whenever a tenant has not overridden a term.
///
/// These are also the words the API, routes and permissions use, so a developer reading a
/// stack trace and a tenant reading the screen are talking about the same thing by default.
/// </summary>
public static class TerminologyDefaults
{
    private static readonly IReadOnlyDictionary<TerminologyTerm, (string Singular, string Plural)> Terms =
        new Dictionary<TerminologyTerm, (string, string)>
        {
            [TerminologyTerm.Lead] = ("Lead", "Leads"),
            [TerminologyTerm.Contact] = ("Contact", "Contacts"),
            [TerminologyTerm.Opportunity] = ("Opportunity", "Opportunities"),
            [TerminologyTerm.Task] = ("Task", "Tasks"),
            [TerminologyTerm.Pipeline] = ("Pipeline", "Pipelines"),
            [TerminologyTerm.Stage] = ("Stage", "Stages"),
            [TerminologyTerm.Activity] = ("Activity", "Activities")
        };

    public static string Singular(TerminologyTerm term) => Terms[term].Singular;

    public static string Plural(TerminologyTerm term) => Terms[term].Plural;

    public static IEnumerable<TerminologyTerm> All => Terms.Keys;
}
