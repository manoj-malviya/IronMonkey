using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.ApiService.Common.Extensions;
using IronMonkey.ApiService.Common.Results;
using IronMonkey.Common;
using IronMonkey.Data;

namespace IronMonkey.ApiService.Features.Opportunities;

public class UpdateOpportunityEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapPut("/api/opportunities/{id:guid}", Handle)
        .WithSummary("Update an opportunity, including stage and loss reason")
        .WithTags("Opportunities")
        .RequireAuthorization()
        .WithRequestValidation<Request>();

    public record Request(
        string Title, Guid ContactId, string Stage,
        decimal Amount, DateTime ExpectedCloseDate, string? LossReason);

    public record Response(Guid Id, string Message);

    public class RequestValidator : AbstractValidator<Request>
    {
        public RequestValidator()
        {
            RuleFor(x => x.Title).NotEmpty().WithMessage("Title is required.");
            RuleFor(x => x.ContactId).NotEmpty().WithMessage("A contact is required.");
            RuleFor(x => x.Amount).GreaterThanOrEqualTo(0).WithMessage("Amount cannot be negative.");
        }
    }

    private static async Task<Results<Ok<Response>, ValidationError, NotFound>> Handle(
        Guid id,
        Request request,
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        CancellationToken cancellationToken)
    {
        if (!OpportunityStages.IsValid(request.Stage))
            return new ValidationError($"Invalid stage. Valid: {string.Join(", ", OpportunityStages.All)}");

        // A lost deal without a reason is the main thing this data is analysed for, so it
        // is required rather than silently blank.
        if (request.Stage == OpportunityStages.Lost && string.IsNullOrWhiteSpace(request.LossReason))
            return new ValidationError("A loss reason is required when marking an opportunity as Lost.");

        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);

        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        var opportunity = await db.Opportunities.SingleOrDefaultAsync(o => o.Id == id, cancellationToken);
        if (opportunity is null)
            return TypedResults.NotFound();

        var contactExists = await db.Contacts.AnyAsync(c => c.Id == request.ContactId, cancellationToken);
        if (!contactExists)
            return new ValidationError("The selected contact no longer exists.");

        opportunity.UpdateOpportunity(
            request.Title.Trim(), request.ContactId,
            DateTime.SpecifyKind(request.ExpectedCloseDate, DateTimeKind.Utc),
            request.Stage);
        opportunity.SetAmount(request.Amount);

        // MarkAsLost also sets the stage, so it is called after UpdateOpportunity to record
        // the reason without a second write path.
        if (request.Stage == OpportunityStages.Lost)
            opportunity.MarkAsLost(request.LossReason!.Trim());

        opportunity.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(new Response(opportunity.Id, "Opportunity updated successfully."));
    }
}
