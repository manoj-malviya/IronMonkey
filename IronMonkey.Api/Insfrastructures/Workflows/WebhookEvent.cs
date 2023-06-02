namespace IronMonkey.Api.Infrastructures.Workflows;

public record WebhookEvent(string EventType, RunTaskWebhook Payload, DateTimeOffset Timestamp);