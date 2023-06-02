namespace IronMonkey.Api.Infrastructures.Workflows;

public class OnboardingTask
{
    public long Id { get; set; }

    public string ExternalId { get; set; } = default!;

    public string ProcessId { get; set; } = default!;

    public string Name { get; set; } = default!;
    
    public string Description { get; set; } = default!;

    public string EmployeeName { get; set; } = default!;

    public string EmployeeEmail { get; set; } = default!;

    public bool IsCompleted { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }
}