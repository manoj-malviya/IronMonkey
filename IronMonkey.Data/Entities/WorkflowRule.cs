using IronMonkey.Data.Abstractions;

namespace IronMonkey.Data.Entities;

public enum WorkflowTrigger { FieldChange = 0, StatusChange = 1, TimeElapsed = 2 }

public sealed class WorkflowRule : BaseTenantEntity
{
    private WorkflowRule() { }

    public string Name { get; private set; } = string.Empty;
    public WorkflowTrigger Trigger { get; private set; }
    public string ConditionJson { get; private set; } = "{}";
    public string ActionJson { get; private set; } = "{}";
    public bool IsActive { get; private set; } = true;

    public static WorkflowRule Create(Guid tenantId, string name, WorkflowTrigger trigger,
        string conditionJson, string actionJson)
        => new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = name,
            Trigger = trigger,
            ConditionJson = conditionJson,
            ActionJson = actionJson
        };

    public void Update(string name, WorkflowTrigger trigger, string conditionJson, string actionJson)
    {
        Name = name;
        Trigger = trigger;
        ConditionJson = conditionJson;
        ActionJson = actionJson;
    }

    public void SetActive(bool isActive) => IsActive = isActive;
}
