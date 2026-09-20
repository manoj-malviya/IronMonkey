using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.ApiService.Common.Results;
using IronMonkey.Common.Auth;
using IronMonkey.Data;
using IronMonkey.Data.Communications;
using IronMonkey.Data.Entities;

namespace IronMonkey.ApiService.Features.Communications.Endpoints;

/// <summary>
/// Sends a message to a lead or contact.
///
/// Returns as soon as the message is persisted and enqueued — it never waits on a provider,
/// so a slow or hung SMTP server cannot hold an HTTP request open. The response carries the
/// row's current status, which is <c>Queued</c> on the happy path and a terminal
/// <c>Rejected</c> when the send was refused up front (unconfigured channel, opted-out
/// recipient, channel rule violation).
///
/// Every send goes through <see cref="IMessageDispatcher"/> rather than talking to a provider
/// here, which is what guarantees this endpoint cannot bypass consent.
/// </summary>
public class SendMessageEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapPost("/api/messages", Handle)
        .WithSummary("Send a message to a lead or contact")
        .WithTags("Communications")
        .RequireAuthorization(PermissionConstants.MessagesSend);

    internal static async Task<Results<Ok<SendMessageResponse>, ValidationError, NotFound>> Handle(
        SendMessageRequest request,
        ITenantService tenantService,
        IUserContext userContext,
        ITenantDbContextFactory dbContextFactory,
        IMessageDispatcher dispatcher,
        IMergeFieldResolver mergeFields,
        CancellationToken cancellationToken)
    {
        if (!MessageMapping.TryParseChannel(request.Channel, out var channel))
            return new ValidationError($"\"{request.Channel}\" is not a supported channel.");

        if (request.LeadId is null && request.ContactId is null)
            return new ValidationError("A message must be attached to a lead or a contact.");

        if (request.LeadId is not null && request.ContactId is not null)
            return new ValidationError("A message belongs to either a lead or a contact, not both.");

        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);
        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        // The record is loaded through the tenant-filtered context, so an id belonging to
        // another tenant is simply absent and returns the same 404 as an unknown one. That is
        // what stops a caller sending "as" another tenant's lead.
        Lead? lead = null;
        Contact? contact = null;
        IReadOnlyDictionary<string, string?> values;

        if (request.LeadId is { } leadId)
        {
            lead = await db.Leads.Include(l => l.Stage).FirstOrDefaultAsync(l => l.Id == leadId, cancellationToken);
            if (lead is null) return TypedResults.NotFound();
            values = await mergeFields.ForLeadAsync(db, lead, cancellationToken);
        }
        else
        {
            contact = await db.Contacts.FirstOrDefaultAsync(c => c.Id == request.ContactId, cancellationToken);
            if (contact is null) return TypedResults.NotFound();
            values = await mergeFields.ForContactAsync(db, contact, cancellationToken);
        }

        var recipient = ResolveRecipient(channel, lead, contact, request.To);
        if (string.IsNullOrWhiteSpace(recipient))
            return new ValidationError("No recipient: the record has no address for this channel.");

        // Body and subject come either from a template or directly from the request. A
        // template is rendered here rather than in the job so that a broken placeholder is
        // reported to the person pressing Send, not discovered hours later in a failed row.
        string subject;
        string body;

        if (request.TemplateId is { } templateId)
        {
            var template = await db.MessageTemplates
                .FirstOrDefaultAsync(t => t.Id == templateId && t.IsActive, cancellationToken);

            if (template is null) return TypedResults.NotFound();

            if (template.Channel != channel)
                return new ValidationError($"That template is for {template.Channel}, not {channel}.");

            var isHtml = ChannelRules.IsHtmlBody(channel);

            var renderedBody = isHtml
                ? MessageTemplateRenderer.RenderHtml(template.Body, values)
                : MessageTemplateRenderer.RenderText(template.Body, values);

            if (!renderedBody.Succeeded)
                return new ValidationError(
                    $"The template refers to fields that do not exist: {string.Join(", ", renderedBody.MissingFields)}.");

            // The subject is always plain text — it is never interpreted as markup, so
            // HTML-escaping it would put &amp; in the customer's inbox.
            var renderedSubject = MessageTemplateRenderer.RenderText(template.Subject, values);

            if (!renderedSubject.Succeeded)
                return new ValidationError(
                    $"The template subject refers to fields that do not exist: {string.Join(", ", renderedSubject.MissingFields)}.");

            body = renderedBody.Text;
            subject = renderedSubject.Text;
        }
        else
        {
            if (string.IsNullOrWhiteSpace(request.Body))
                return new ValidationError("A message needs a body or a template.");

            body = request.Body;
            subject = request.Subject ?? string.Empty;
        }

        var ruleViolation = ChannelRules.Validate(channel, subject, body);
        if (ruleViolation is not null)
            return new ValidationError(ruleViolation);

        var command = new SendMessageCommand(
            tenantId,
            channel,
            recipient,
            ChannelRules.HasSubject(channel) ? subject : null,
            body,
            // A client-supplied key makes a double-clicked Send idempotent. Without one, a
            // fresh key per request still protects against Hangfire retries — it just cannot
            // know that two HTTP requests were the same intent.
            string.IsNullOrWhiteSpace(request.IdempotencyKey)
                ? $"manual:{Guid.NewGuid():N}"
                : $"manual:{request.IdempotencyKey.Trim()}",
            lead?.Id,
            contact?.Id,
            userContext.UserId);

        var outcome = await dispatcher.QueueAsync(db, command, cancellationToken);

        return TypedResults.Ok(new SendMessageResponse(
            outcome.MessageId ?? Guid.Empty,
            outcome.Status.ToString(),
            outcome.ErrorCategory.ToString(),
            outcome.ErrorMessage));
    }

    /// <summary>
    /// Picks the address for the channel, defaulting to the record's own.
    ///
    /// An explicit "to" is honoured so a user can reply to a secondary address, but it is
    /// still checked against suppression downstream like any other recipient.
    /// </summary>
    private static string? ResolveRecipient(MessageChannel channel, Lead? lead, Contact? contact, string? requested)
    {
        if (!string.IsNullOrWhiteSpace(requested)) return requested.Trim();

        return channel == MessageChannel.Email
            ? lead?.Email ?? contact?.Email
            : lead?.Mobile ?? contact?.Mobile;
    }
}
