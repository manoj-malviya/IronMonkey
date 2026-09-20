using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.ApiService.Common.Results;
using IronMonkey.Common.Auth;
using IronMonkey.Data;
using IronMonkey.Data.Communications;

namespace IronMonkey.ApiService.Features.Communications.Endpoints;

public sealed record AttachMessageRequest(Guid? LeadId, Guid? ContactId);

/// <summary>
/// Attaches an unmatched inbound message to a lead or contact from the review queue.
///
/// This is the "visible, reviewable home" the design promises unmatched messages: a reply from
/// a number stored with different punctuation is kept rather than dropped, and a human can put
/// it where it belongs.
/// </summary>
public class AttachMessageEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapPut("/api/messages/{id:guid}/attach", Handle)
        .WithSummary("Attach an unmatched inbound message to a lead or contact")
        .WithTags("Communications")
        .RequireAuthorization(PermissionConstants.MessagesSend);

    internal static async Task<Results<Ok, ValidationError, NotFound>> Handle(
        Guid id,
        AttachMessageRequest request,
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        CancellationToken cancellationToken)
    {
        if (request.LeadId is null && request.ContactId is null)
            return new ValidationError("A message must be attached to a lead or a contact.");

        if (request.LeadId is not null && request.ContactId is not null)
            return new ValidationError("A message belongs to either a lead or a contact, not both.");

        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);
        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        var message = await db.Messages.FirstOrDefaultAsync(m => m.Id == id, cancellationToken);
        if (message is null) return TypedResults.NotFound();

        if (message.Direction != MessageDirection.Inbound)
            return new ValidationError("Only an inbound message can be attached to a record.");

        // The target is verified to exist in this tenant. Without the check, an attach could
        // point a message at an id from another tenant, which would then render as an
        // orphaned row on nobody's timeline.
        if (request.LeadId is { } leadId && !await db.Leads.AnyAsync(l => l.Id == leadId, cancellationToken))
            return TypedResults.NotFound();

        if (request.ContactId is { } contactId && !await db.Contacts.AnyAsync(c => c.Id == contactId, cancellationToken))
            return TypedResults.NotFound();

        message.AttachTo(request.LeadId, request.ContactId);
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok();
    }
}
