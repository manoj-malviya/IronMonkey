using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.ApiService.Common.Extensions;
using IronMonkey.ApiService.Common.Results;
using IronMonkey.ApiService.Features.Configuration;
using IronMonkey.ApiService.Features.Pipelines;
using IronMonkey.Data;
using IronMonkey.Data.Entities;

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
        string Title, Guid ContactId, Guid StageId,
        decimal Amount, DateTime ExpectedCloseDate, string? LossReason);

    public record Response(Guid Id, string Message);

    public class RequestValidator : AbstractValidator<Request>
    {
        public RequestValidator()
        {
            RuleFor(x => x.Title).NotEmpty().WithMessage("Title is required.");
            RuleFor(x => x.ContactId).NotEmpty().WithMessage("A contact is required.");
            RuleFor(x => x.StageId).NotEmpty().WithMessage("A stage is required.");
            RuleFor(x => x.Amount).GreaterThanOrEqualTo(0).WithMessage("Amount cannot be negative.");
        }
    }

    private static async Task<Results<Ok<Response>, ValidationError, NotFound>> Handle(
        Guid id,
        Request request,
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        IUserContext userContext,
        CancellationToken cancellationToken)
    {
        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);

        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        var opportunity = await db.Opportunities.SingleOrDefaultAsync(o => o.Id == id, cancellationToken);
        if (opportunity is null)
            return TypedResults.NotFound();

        var contactExists = await db.Contacts.AnyAsync(c => c.Id == request.ContactId, cancellationToken);
        if (!contactExists)
            return new ValidationError("The selected contact no longer exists.");

        // Resolved within the deal's OWN pipeline, not merely within the tenant's opportunity
        // stages. The record-type filter alone would still accept a stage from a SECOND deal
        // pipeline, leaving the deal on a stage its pipeline does not contain. Changing
        // pipeline is a separate operation: POST /api/opportunities/{id}/pipeline.
        var stage = await PipelineStageResolution.FindInPipelineAsync(
            db, opportunity.PipelineId, request.StageId, cancellationToken);

        if (stage is null)
            return new ValidationError("That stage does not belong to this deal's pipeline.");

        // An inactive stage is still allowed if the deal is already sitting in it — otherwise
        // deactivating a stage would make every deal in it unsaveable until it is moved.
        if (!stage.IsActive && opportunity.PipelineStageId != stage.Id)
            return new ValidationError($"'{stage.Name}' is not an active stage.");

        // A lost deal without a reason is the main thing this data is analysed for, so it is
        // required rather than silently blank. The condition is the stage TYPE, not its name:
        // a tenant that renamed "Lost" to "Declined" still gets the prompt, and a tenant that
        // named some active stage "Lost" does not.
        var isLost = stage.StageType == StageType.ClosedLost;

        if (isLost && string.IsNullOrWhiteSpace(request.LossReason))
            return new ValidationError("A loss reason is required when closing an opportunity as lost.");

        var previousStageId = opportunity.PipelineStageId;

        opportunity.UpdateOpportunity(
            request.Title.Trim(), request.ContactId,
            DateTime.SpecifyKind(request.ExpectedCloseDate, DateTimeKind.Utc),
            stage.Id);
        opportunity.SetAmount(request.Amount);

        if (isLost)
        {
            // MarkAsLost also sets the stage, so it is called after UpdateOpportunity to
            // record the reason without a second write path.
            opportunity.MarkAsLost(stage.Id, request.LossReason!.Trim());
        }
        else
        {
            // Reopening a deal drops the reason it was closed with — otherwise a deal moved
            // back into the pipeline still reads as lost everywhere the reason is shown.
            opportunity.ClearLossReason();
        }

        StageChangeRecorder.Record(
            db, tenantId, PipelineRecordType.Opportunity, opportunity.Id,
            previousStageId, stage.Id, userContext.UserId,
            // A stage edit never changes the pipeline: the stage was resolved within the
            // deal's own pipeline, so from and to are the same by construction. Moving
            // between pipelines goes through POST /api/opportunities/{id}/pipeline instead.
            fromPipelineId: opportunity.PipelineId, toPipelineId: stage.PipelineId);

        opportunity.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(new Response(opportunity.Id, "Opportunity updated successfully."));
    }
}
