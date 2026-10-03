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

    /// <summary>
    /// The vertical's opportunity stages, seeded alongside the lead stages in
    /// <see cref="PipelineStages"/> — a dealership's deal stages are not a university's.
    ///
    /// Null on every recipe stored before this existed, following the same precedent as
    /// <see cref="Presentation"/>: those documents must keep deserializing and provisioning
    /// unchanged. A null here is NOT the same as an empty list — null means "this recipe has
    /// no opinion, use the product defaults", while an explicitly empty list would mean "seed
    /// no opportunity stages at all" and leave the tenant unable to create a deal.
    /// Provisioning applies the built-in default set for null.
    ///
    /// Reuses <see cref="PipelineStageDefinition"/> rather than declaring a parallel shape:
    /// the two stage kinds are the same concept over different records, and the entity they
    /// seed is literally the same one.
    /// </summary>
    public List<PipelineStageDefinition>? OpportunityStages { get; set; }

    /// <summary>
    /// Named pipelines the recipe defines, each with its own stages — a dealership template
    /// that seeds "New car sales" and "Service bookings" as separate funnels on day one.
    ///
    /// <para>
    /// Null on every recipe stored before this existed, following the same precedent as
    /// <see cref="Presentation"/> and <see cref="OpportunityStages"/>, and it means the same
    /// thing: "this recipe has no opinion about pipelines". Provisioning then treats the flat
    /// <see cref="PipelineStages"/> list as the default lead pipeline and
    /// <see cref="OpportunityStages"/> as the default opportunity pipeline, which is exactly
    /// what those recipes meant before pipelines existed. That is the backward-compatibility
    /// guarantee: an old recipe provisions to a single-pipeline tenant, unchanged.
    /// </para>
    ///
    /// <para>
    /// An explicitly empty list means the same as null here rather than "seed no pipelines":
    /// a tenant with no pipelines can hold no leads and no deals at all, so unlike
    /// <see cref="OpportunityStages"/> there is no coherent authoring intent for zero. The
    /// asymmetry is deliberate — "seed no opportunity stages" is a choice a vertical might
    /// make, "seed nowhere to put a record" is not.
    /// </para>
    ///
    /// <para>
    /// When this IS populated it is authoritative and the flat lists are ignored, so a recipe
    /// cannot half-specify and get a surprising merge of the two.
    /// </para>
    /// </summary>
    public List<PipelineDefinition>? Pipelines { get; set; }
}

/// <summary>
/// One named pipeline in a recipe, with the stages it owns.
/// </summary>
public sealed class PipelineDefinition
{
    public string Name { get; set; } = string.Empty;

    /// <summary>"Lead" or "Opportunity". Defaults to Lead, matching
    /// <c>PipelineRecordType</c>'s own default.</summary>
    public string RecordType { get; set; } = "Lead";

    public string? Description { get; set; }

    /// <summary>
    /// Whether this is the record type's default pipeline. Provisioning promotes the first
    /// pipeline of each record type if the recipe marks none — a record type with no default
    /// has no answer for "no pipeline specified", which every caller relies on.
    /// </summary>
    public bool IsDefault { get; set; }

    public int Order { get; set; }

    /// <summary>The pipeline's stages. An empty list seeds the product defaults for the
    /// record type rather than an unusable pipeline.</summary>
    public List<PipelineStageDefinition> Stages { get; set; } = [];
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
