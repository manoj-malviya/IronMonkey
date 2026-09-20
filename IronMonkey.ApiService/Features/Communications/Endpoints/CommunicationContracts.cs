using IronMonkey.Data.Communications;
using IronMonkey.Data.Entities;

namespace IronMonkey.ApiService.Features.Communications.Endpoints;

/// <summary>
/// One message as the timeline shows it.
///
/// Deliberately carries no provider credential, no raw provider payload and no delivery
/// receipt internals — only what a tenant user needs to read the conversation and understand
/// its delivery state.
/// </summary>
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

public sealed record SendMessageRequest(
    string Channel,
    string To,
    string? Subject,
    string? Body,
    /// <summary>Optional: render this template instead of supplying a body directly.</summary>
    Guid? TemplateId,
    Guid? LeadId,
    Guid? ContactId,
    /// <summary>
    /// Supplied by the client so a retried HTTP request cannot produce two messages. Absent,
    /// the server generates one — which still protects against Hangfire retries, just not
    /// against a double-clicked Send button.
    /// </summary>
    string? IdempotencyKey);

public sealed record SendMessageResponse(
    Guid MessageId,
    string Status,
    string ErrorCategory,
    string? ErrorMessage);

public sealed record ChannelAvailability(string Channel, bool IsAvailable);

public static class MessageMapping
{
    public static MessageItem ToItem(Message message) => new(
        message.Id,
        message.Channel.ToString(),
        message.Direction.ToString(),
        message.Provider,
        message.FromAddress,
        message.ToAddress,
        message.Subject,
        message.Body,
        message.IsBodyHtml,
        message.Status.ToString(),
        message.ErrorCategory.ToString(),
        message.ErrorMessage,
        message.QueuedAt,
        message.SentAt,
        message.DeliveredAt,
        message.LeadId,
        message.ContactId,
        message.SentByUserId,
        message.WorkflowRuleId);

    public static bool TryParseChannel(string? value, out MessageChannel channel) =>
        Enum.TryParse(value, ignoreCase: true, out channel) && Enum.IsDefined(channel);
}
