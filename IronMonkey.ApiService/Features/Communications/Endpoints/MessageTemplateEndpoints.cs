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

public sealed record MessageTemplateItem(
    Guid Id, string Name, string Channel, string? Subject, string Body,
    string? ProviderTemplateName, bool IsActive);

public sealed record SaveMessageTemplateRequest(
    string Name, string Channel, string? Subject, string Body, string? ProviderTemplateName);

/// <summary>
/// Tenant-editable message templates.
///
/// Templates are validated against the tenant's real field definitions on save. That is the
/// point of validating here rather than at render time: a placeholder naming a field that does
/// not exist becomes a blank in a customer's inbox at 3am, discovered by the customer. Caught
/// at save, it is a red box next to the field the author just typed.
/// </summary>
public class ListMessageTemplatesEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapGet("/api/message-templates", Handle)
        .WithSummary("List message templates")
        .WithTags("Communications")
        .RequireAuthorization(PermissionConstants.MessagesRead);

    internal static async Task<Ok<List<MessageTemplateItem>>> Handle(
        string? channel,
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        CancellationToken cancellationToken)
    {
        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);
        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        var query = db.MessageTemplates.AsNoTracking().AsQueryable();

        if (MessageMapping.TryParseChannel(channel, out var parsed))
            query = query.Where(t => t.Channel == parsed);

        var templates = await query.OrderBy(t => t.Name).ThenBy(t => t.Id).ToListAsync(cancellationToken);

        return TypedResults.Ok(templates.Select(Map).ToList());
    }

    internal static MessageTemplateItem Map(MessageTemplate template) => new(
        template.Id, template.Name, template.Channel.ToString(), template.Subject,
        template.Body, template.ProviderTemplateName, template.IsActive);
}

public class CreateMessageTemplateEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapPost("/api/message-templates", Handle)
        .WithSummary("Create a message template")
        .WithTags("Communications")
        .RequireAuthorization(PermissionConstants.SettingsWrite);

    internal static async Task<Results<Ok<MessageTemplateItem>, ValidationError>> Handle(
        SaveMessageTemplateRequest request,
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        IMergeFieldResolver mergeFields,
        CancellationToken cancellationToken)
    {
        if (!MessageMapping.TryParseChannel(request.Channel, out var channel))
            return new ValidationError($"\"{request.Channel}\" is not a supported channel.");

        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);
        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        var problem = await MessageTemplateValidation.ValidateAsync(db, mergeFields, channel, request, cancellationToken);
        if (problem is not null) return new ValidationError(problem);

        var template = MessageTemplate.Create(
            tenantId, request.Name.Trim(), channel, request.Subject?.Trim(),
            request.Body, request.ProviderTemplateName?.Trim());

        db.MessageTemplates.Add(template);
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(ListMessageTemplatesEndpoint.Map(template));
    }
}

public class UpdateMessageTemplateEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapPut("/api/message-templates/{id:guid}", Handle)
        .WithSummary("Update a message template")
        .WithTags("Communications")
        .RequireAuthorization(PermissionConstants.SettingsWrite);

    internal static async Task<Results<Ok<MessageTemplateItem>, ValidationError, NotFound>> Handle(
        Guid id,
        SaveMessageTemplateRequest request,
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        IMergeFieldResolver mergeFields,
        CancellationToken cancellationToken)
    {
        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);
        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        var template = await db.MessageTemplates.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (template is null) return TypedResults.NotFound();

        // The channel is fixed after creation: changing it would silently invalidate the
        // subject rule (email has one, SMS does not) and any rule already referencing it.
        var problem = await MessageTemplateValidation.ValidateAsync(
            db, mergeFields, template.Channel, request, cancellationToken);

        if (problem is not null) return new ValidationError(problem);

        template.Update(request.Name.Trim(), request.Subject?.Trim(), request.Body,
            request.ProviderTemplateName?.Trim());

        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(ListMessageTemplatesEndpoint.Map(template));
    }
}

/// <summary>
/// Shared save-time checks, so create and update cannot drift apart and let an invalid
/// template in through the path that was not updated.
/// </summary>
internal static class MessageTemplateValidation
{
    private const int MaxNameLength = 200;

    public static async Task<string?> ValidateAsync(
        TenantDbContext db,
        IMergeFieldResolver mergeFields,
        MessageChannel channel,
        SaveMessageTemplateRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return "A template needs a name.";

        if (request.Name.Trim().Length > MaxNameLength)
            return $"The name is longer than {MaxNameLength} characters.";

        if (string.IsNullOrWhiteSpace(request.Body))
            return "A template needs a body.";

        if (channel == MessageChannel.Email && string.IsNullOrWhiteSpace(request.Subject))
            return "An email template needs a subject.";

        // Validated against the tenant's actual field definitions. A placeholder that names
        // nothing is a template that will render blank for every recipient.
        var available = await mergeFields.AvailableFieldsAsync(db, cancellationToken);
        var known = new HashSet<string>(available, StringComparer.OrdinalIgnoreCase);

        var used = MessageTemplateRenderer.ExtractPlaceholders(request.Body)
            .Concat(MessageTemplateRenderer.ExtractPlaceholders(request.Subject))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var unknown = used.Where(name => !known.Contains(name)).ToList();

        if (unknown.Count > 0)
            return $"These placeholders do not match any field: {string.Join(", ", unknown)}.";

        // SMS length is checked against the template text. It is a floor, not a guarantee —
        // substituted values make the real message longer — but a template that is already
        // over the limit before any data is merged can never send.
        if (channel == MessageChannel.Sms)
        {
            var segments = ChannelRules.SegmentCount(request.Body);
            if (segments > ChannelRules.SmsMaxSegments)
                return $"The body is {segments} SMS segments before any field is substituted, over the {ChannelRules.SmsMaxSegments}-segment limit.";
        }

        return null;
    }
}
