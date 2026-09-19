using IronMonkey.Data.Presentation;

namespace IronMonkey.Data.RecipeContent;

public sealed class RecipeContentModel
{
    public List<PipelineStageDefinition> PipelineStages { get; set; } = [];
    public List<CustomFieldDefinitionDto> CustomFields { get; set; } = [];
    public List<WorkflowRuleDefinition> WorkflowRules { get; set; } = [];
    public List<RoleDefinition> Roles { get; set; } = [];
    public List<SampleLeadDefinition> SampleLeads { get; set; } = [];

    /// <summary>
    /// The vertical's own vocabulary and formatting defaults, copied into the tenant at
    /// provisioning like every other recipe artifact — so an Automobile tenant arrives saying
    /// "Enquiry" with no manual setup, and is then free to change it.
    ///
    /// Null on every recipe stored before this existed, which is why it is optional: those
    /// documents must keep deserializing and provisioning unchanged.
    /// </summary>
    public RecipePresentationDefinition? Presentation { get; set; }
}

/// <summary>
/// Presentation defaults a recipe seeds into a new tenant. Deliberately a mirror of
/// <see cref="TenantPresentationSettings"/> minus branding: a logo and colours belong to the
/// tenant, not to the vertical template.
/// </summary>
public sealed class RecipePresentationDefinition
{
    public TenantTerminology? Terminology { get; set; }
    public TenantLocale? Locale { get; set; }
}

public sealed class PipelineStageDefinition
{
    public string Name { get; set; } = string.Empty;
    public int Order { get; set; }
    public string StageType { get; set; } = "Active"; // "Entry", "Active", "ClosedWon", "ClosedLost"
}

public sealed class CustomFieldDefinitionDto
{
    public string FieldName { get; set; } = string.Empty;
    public string FieldType { get; set; } = "Text"; // mirrors CustomFieldType enum: Text, Number, Date, Dropdown, MultiSelect, Currency, Boolean
    public bool IsRequired { get; set; }
    public List<string> Options { get; set; } = [];
}

public sealed class WorkflowRuleDefinition
{
    public string Name { get; set; } = string.Empty;
    public string Trigger { get; set; } = "FieldChange"; // mirrors WorkflowTrigger enum: FieldChange, StatusChange, TimeElapsed
    public string ConditionJson { get; set; } = "{}";
    public string ActionJson { get; set; } = "{}";
}

public sealed class RoleDefinition
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public sealed class SampleLeadDefinition
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Mobile { get; set; } = string.Empty;
    public string Source { get; set; } = "WebForm";
    public string StageName { get; set; } = string.Empty;
    public Dictionary<string, object?> CustomFieldValues { get; set; } = [];
}
