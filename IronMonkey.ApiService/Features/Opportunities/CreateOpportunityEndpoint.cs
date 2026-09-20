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

namespace IronMonkey.ApiService.Features.Opportunities;

public class CreateOpportunityEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapPost("/api/opportunities", Handle)
        .WithSummary("Create an opportunity against an existing contact")
        .WithTags("Opportunities")
        .RequireAuthorization()
        .WithRequestValidation<Request>();

    public record Request(
        string Title, Guid ContactId, string Stage,
        decimal Amount, DateTime ExpectedCloseDate);

    public record Response(Guid Id, string Title, string Stage, decimal Amount);

    public class RequestValidator : AbstractValidator<Request>
    {
        public RequestValidator()
        {
            RuleFor(x => x.Title).NotEmpty().WithMessage("Title is required.");
            RuleFor(x => x.ContactId).NotEmpty().WithMessage("A contact is required.");
            RuleFor(x => x.Amount).GreaterThanOrEqualTo(0).WithMessage("Amount cannot be negative.");
        }
    }

    private static async Task<Results<Created<Response>, ValidationError>> Handle(
        Request request,
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        CancellationToken cancellationToken)
    {
        if (!OpportunityStages.IsValid(request.Stage))
            return new ValidationError($"Invalid stage. Valid: {string.Join(", ", OpportunityStages.All)}");

        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);

        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        var contactExists = await db.Contacts.AnyAsync(c => c.Id == request.ContactId, cancellationToken);
        if (!contactExists)
            return new ValidationError("The selected contact no longer exists.");

        var opportunity = Opportunity.Create(
            tenantId, request.Title.Trim(), request.ContactId,
            // Postgres timestamptz requires UTC; a date picked in the browser arrives
            // unspecified and would otherwise throw on save.
            DateTime.SpecifyKind(request.ExpectedCloseDate, DateTimeKind.Utc),
            request.Stage);
        opportunity.SetAmount(request.Amount);

        db.Opportunities.Add(opportunity);
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Created(
            $"/api/opportunities/{opportunity.Id}",
            new Response(opportunity.Id, opportunity.Title, opportunity.Stage, opportunity.Amount));
    }
}
