namespace IronMonkey.Data.Presentation;

/// <summary>
/// One tenant's fully-resolved presentation context: what to call things, how to format
/// them, and how the shell should look. Built once per request or circuit.
/// </summary>
public sealed class TenantPresentation
{
    private TenantPresentation(ResolvedTerminology terminology, TenantFormatting formatting, TenantBranding branding)
    {
        Terminology = terminology;
        Formatting = formatting;
        Branding = branding;
    }

    public ResolvedTerminology Terminology { get; }
    public TenantFormatting Formatting { get; }
    public TenantBranding Branding { get; }

    /// <summary>Used before a tenant is known, and for platform-admin surfaces.</summary>
    public static TenantPresentation Default { get; } =
        new(ResolvedTerminology.Default, TenantFormatting.Default, new TenantBranding());

    public static TenantPresentation From(TenantPresentationSettings? settings) =>
        new(ResolvedTerminology.From(settings?.Terminology),
            TenantFormatting.From(settings?.Locale),
            settings?.Branding ?? new TenantBranding());
}
