namespace IronMonkey.Data.Presentation;

/// <summary>
/// The core nouns a tenant may rename. This is a closed set on purpose: every term has a
/// built-in default, so there is no way for a lookup to find nothing and render blank.
/// </summary>
public enum TerminologyTerm
{
    Lead,
    Contact,
    Opportunity,
    Task,
    Pipeline,
    Stage,
    Activity
}
