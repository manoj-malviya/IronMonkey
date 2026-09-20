using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.ApiService.Common.Extensions;
using IronMonkey.ApiService.Common.Results;
using IronMonkey.ApiService.Features.CustomFields;
using IronMonkey.Data;
using IronMonkey.Data.Entities;

namespace IronMonkey.ApiService.Features.Contacts;

public class CreateContactEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapPost("/api/contacts", Handle)
        .WithSummary("Create a contact in the current tenant")
        .WithTags("Contacts")
        .RequireAuthorization()
        .WithRequestValidation<Request>();

    public record Request(string Name, string Email, string Mobile,
        Dictionary<string, object?>? CustomFields = null);
    public record Response(Guid Id, string Name, string Email, string Mobile);

    public class RequestValidator : AbstractValidator<Request>
    {
        public RequestValidator()
        {
            RuleFor(x => x.Name).NotEmpty().WithMessage("Name is required.");
            RuleFor(x => x.Email).NotEmpty().EmailAddress().WithMessage("A valid email address is required.");
        }
    }

    private static async Task<Results<Created<Response>, ValidationError>> Handle(
        Request request,
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        CancellationToken cancellationToken)
    {
        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);

        var email = request.Email.Trim().ToLowerInvariant();

        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        // Email is how a person is recognised across leads and opportunities, so a second
        // contact with the same address would silently split one customer's history in two.
        var exists = await db.Contacts.AnyAsync(c => c.Email == email, cancellationToken);
        if (exists)
            return new ValidationError("A contact with this email address already exists.");

        var definitions = await db.CustomFieldDefinitions
            .Where(f => f.AppliesTo == CustomFieldEntity.Contact)
            .ToListAsync(cancellationToken);

        var bound = CustomFieldValueBinder.Bind(definitions, request.CustomFields);
        if (!bound.IsValid)
            return new ValidationError(string.Join(" ", bound.Errors));

        var contact = Contact.Create(tenantId, request.Name.Trim(), request.Mobile?.Trim() ?? string.Empty, email);
        foreach (var (key, value) in bound.Values)
            contact.CustomFields.Set(key, value);

        db.Contacts.Add(contact);
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Created(
            $"/api/contacts/{contact.Id}",
            new Response(contact.Id, contact.Name, contact.Email, contact.Mobile));
    }
}
