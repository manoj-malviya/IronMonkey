namespace IronMonkey.Web.Communications;

/// <summary>Wire shapes for the communications endpoints. Mirrors the API contracts.</summary>
public sealed record MessageItem(
    Guid Id,
    string Channel,
    string Direction,
    string Provider,
    string From,
    string To,
    string? Subject,
    string Body,
    bool IsBodyHtml,
    string Status,
    string ErrorCategory,
    string? ErrorMessage,
    DateTime QueuedAt,
    DateTime? SentAt,
    DateTime? DeliveredAt,
    Guid? LeadId,
    Guid? ContactId,
    Guid? SentByUserId,
    Guid? WorkflowRuleId);

public sealed record MessagePage(List<MessageItem> Items, int TotalCount, int Page, int PageSize, int TotalPages);

public sealed record SendMessageRequest(
    string Channel,
    string To,
    string? Subject,
    string? Body,
    Guid? TemplateId,
    Guid? LeadId,
    Guid? ContactId,
    string? IdempotencyKey);

public sealed record SendMessageResponse(Guid MessageId, string Status, string ErrorCategory, string? ErrorMessage);

public sealed record ChannelAvailability(string Channel, bool IsAvailable);

public sealed record MessageTemplateItem(
    Guid Id, string Name, string Channel, string? Subject, string Body,
    string? ProviderTemplateName, bool IsActive);
