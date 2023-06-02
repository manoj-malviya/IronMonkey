namespace IronMonkey.Api.Infrastructures.Workflows;

public record RunTaskWebhook(string WorkflowInstanceId, string TaskId, string TaskName, TaskPayload TaskPayload);