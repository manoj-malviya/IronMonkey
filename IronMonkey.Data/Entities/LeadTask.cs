using IronMonkey.Data.Abstractions;

namespace IronMonkey.Data.Entities;

public enum TaskPriority { Low = 0, Medium = 1, High = 2, Urgent = 3 }
public enum TaskStatus { Pending = 0, InProgress = 1, Completed = 2, Cancelled = 3 }

public sealed class LeadTask : BaseTenantEntity
{
    private LeadTask() { }

    public Guid LeadId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public DateTime? DueDate { get; private set; }
    public TaskPriority Priority { get; private set; } = TaskPriority.Medium;
    public TaskStatus Status { get; private set; } = TaskStatus.Pending;
    public Guid? AssignedToUserId { get; private set; }

    public Lead Lead { get; private set; } = null!;

    public static LeadTask Create(Guid tenantId, Guid leadId, string title, DateTime? dueDate,
        TaskPriority priority, Guid? assignedToUserId)
        => new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            LeadId = leadId,
            Title = title,
            DueDate = dueDate,
            Priority = priority,
            AssignedToUserId = assignedToUserId
        };

    public void Update(string title, string? description, DateTime? dueDate, TaskPriority priority,
        TaskStatus status, Guid? assignedToUserId)
    {
        Title = title;
        Description = description;
        DueDate = dueDate;
        Priority = priority;
        Status = status;
        AssignedToUserId = assignedToUserId;
    }
}
