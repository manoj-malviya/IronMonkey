using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.ApiService.Common.Extensions;
using IronMonkey.ApiService.Common.Results;
using IronMonkey.ApiService.Features.CustomFields;
using IronMonkey.Data.Entities;
using IronMonkey.Data;

namespace IronMonkey.ApiService.Features.Contacts;

public class UpdateContactEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapPut("/api/contacts/{id:guid}", Handle)
        .WithSummary("Update a contact's details")
        .WithTags("Contacts")
        .RequireAuthorization()
        .WithRequestValidation<Request>();

    public record Request(string Name, string Email, string Mobile,
        Dictionary<string, object?>? CustomFields = null);
    public record Response(Guid Id, string Message);

    public class RequestValidator : AbstractValidator<Request>
    {
        public RequestValidator()
        {
            RuleFor(x => x.Name).NotEmpty().WithMessage("Name is required.");
            RuleFor(x => x.Email).NotEmpty().EmailAddress().WithMessage("A valid email address is required.");
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

        var email = request.Email.Trim().ToLowerInvariant();

        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        var contact = await db.Contacts.SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (contact is null)
            return TypedResults.NotFound();

        var taken = await db.Contacts.AnyAsync(c => c.Email == email && c.Id != id, cancellationToken);
        if (taken)
            return new ValidationError("A contact with this email address already exists.");

        var definitions = await db.CustomFieldDefinitions
            .Where(f => f.AppliesTo == CustomFieldEntity.Contact)
            .ToListAsync(cancellationToken);

        var bound = CustomFieldValueBinder.Bind(definitions, request.CustomFields);
        if (!bound.IsValid)
            return new ValidationError(string.Join(" ", bound.Errors));

        contact.UpdateContactInfo(request.Name.Trim(), request.Mobile?.Trim() ?? string.Empty, email);

        contact.CustomFields.Values.Clear();
        foreach (var (key, value) in bound.Values)
            contact.CustomFields.Set(key, value);
        db.Entry(contact).Property(c => c.CustomFields).IsModified = true;

        contact.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(new Response(contact.Id, "Contact updated successfully."));
    }
}
