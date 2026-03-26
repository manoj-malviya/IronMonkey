namespace IronMonkey.Data.RecipeContent;

public sealed class RecipeContentModel
{
    public List<PipelineStageDefinition> PipelineStages { get; set; } = [];
    public List<CustomFieldDefinitionDto> CustomFields { get; set; } = [];
    public List<WorkflowRuleDefinition> WorkflowRules { get; set; } = [];
    public List<RoleDefinition> Roles { get; set; } = [];
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
