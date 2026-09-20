using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.ApiService.Common.Extensions;
using IronMonkey.ApiService.Common.Results;
using IronMonkey.Common;
using IronMonkey.Data;
using IronMonkey.Data.Entities;

namespace IronMonkey.ApiService.Features.Leads;

/// <summary>
/// Turns a qualified lead into a Contact (the person) and, optionally, an Opportunity
/// (the deal) — the standard CRM hand-off. The lead is kept and stamped with the ids it
/// produced rather than deleted, so the pipeline history stays intact.
/// </summary>
public class ConvertLeadEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapPost("/api/leads/{id:guid}/convert", Handle)
        .WithSummary("Convert a lead into a contact and optionally an opportunity")
        .WithTags("Leads")
        .RequireAuthorization()
        .WithRequestValidation<Request>();

    public record Request(
        bool CreateOpportunity,
        string? OpportunityTitle,
        decimal Amount,
        DateTime? ExpectedCloseDate,
        string? Stage);

    public record Response(Guid LeadId, Guid ContactId, Guid? OpportunityId, string Message);

    public class RequestValidator : AbstractValidator<Request>
    {
        public RequestValidator()
        {
            RuleFor(x => x.Amount).GreaterThanOrEqualTo(0).WithMessage("Amount cannot be negative.");
            RuleFor(x => x.OpportunityTitle)
                .NotEmpty().When(x => x.CreateOpportunity)
                .WithMessage("An opportunity title is required.");
        }
    }

    private static async Task<Results<Ok<Response>, ValidationError, NotFound>> Handle(
        Guid id,
        Request request,
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        CancellationToken cancellationToken)
    {
        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);

        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        var lead = await db.Leads.SingleOrDefaultAsync(l => l.Id == id, cancellationToken);
        if (lead is null)
            return TypedResults.NotFound();

        if (lead.IsConverted)
            return new ValidationError("This lead has already been converted.");

        var stage = request.Stage ?? OpportunityStages.Qualification;
        if (request.CreateOpportunity && !OpportunityStages.IsValid(stage))
            return new ValidationError($"Invalid stage. Valid: {string.Join(", ", OpportunityStages.All)}");

        var email = lead.Email.Trim().ToLowerInvariant();

        // Reuse an existing contact with the same email instead of creating a duplicate:
        // converting two leads for the same person is normal and should land on one contact.
        var contact = await db.Contacts.SingleOrDefaultAsync(c => c.Email == email, cancellationToken);

        if (contact is null)
        {
            contact = Contact.Create(
                tenantId,
                $"{lead.FirstName} {lead.LastName}".Trim(),
                lead.Mobile,
                email);

            // Carry across any custom field the tenant defined on both records under the
            // same name — otherwise details captured on the lead are lost at conversion,
            // which is exactly when they start mattering. Values are keyed by definition
            // Id, so the lead's key has to be translated to the contact field's key.
            await CopyMatchingCustomFieldsAsync(db, lead, contact, cancellationToken);

            db.Contacts.Add(contact);
        }

        Opportunity? opportunity = null;
        if (request.CreateOpportunity)
        {
            opportunity = Opportunity.Create(
                tenantId,
                request.OpportunityTitle!.Trim(),
                contact.Id,
                DateTime.SpecifyKind(
                    request.ExpectedCloseDate ?? DateTime.UtcNow.AddDays(30),
                    DateTimeKind.Utc),
                stage);
            opportunity.SetAmount(request.Amount);
            db.Opportunities.Add(opportunity);
        }

        lead.Convert(accountId: null, contactId: contact.Id, opportunityId: opportunity?.Id);

        // One SaveChanges for all three writes: they share the tenant DbContext, so this is
        // a single transaction — a failure leaves the lead unconverted rather than half-done.
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(new Response(
            lead.Id, contact.Id, opportunity?.Id,
            opportunity is null
                ? "Lead converted to a contact."
                : "Lead converted to a contact and opportunity."));
    }

    private static async Task CopyMatchingCustomFieldsAsync(
        TenantDbContext db, Lead lead, Contact contact, CancellationToken cancellationToken)
    {
        if (lead.CustomFields.Values.Count == 0)
            return;

        var definitions = await db.CustomFieldDefinitions.ToListAsync(cancellationToken);

        var leadFieldsById = definitions
            .Where(f => f.AppliesTo == CustomFieldEntity.Lead)
            .ToDictionary(f => f.Id.ToString(), f => f);

        var contactFieldsByName = definitions
            .Where(f => f.AppliesTo == CustomFieldEntity.Contact)
            .ToDictionary(f => f.FieldName, StringComparer.OrdinalIgnoreCase);

        foreach (var (key, value) in lead.CustomFields.Values)
        {
            if (!leadFieldsById.TryGetValue(key, out var leadField))
                continue;

            if (contactFieldsByName.TryGetValue(leadField.FieldName, out var contactField)
                && contactField.FieldType == leadField.FieldType)
            {
                contact.CustomFields.Set(contactField.Id.ToString(), value);
            }
        }
    }
}
