using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.ApiService.Common.Extensions;
using IronMonkey.ApiService.Common.Results;
using IronMonkey.ApiService.Features.Configuration;
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

    /// <param name="StageId">
    /// Optional. Omitted lands the deal in the tenant's default entry stage, so a caller that
    /// does not care about stages does not have to fetch the list first.
    /// </param>
    /// <param name="Amount">
    /// Optional lump-sum value. Deal value is computed from line items, so a positive amount
    /// becomes a single free-text one-off line rather than a stored decimal; omit it (or send
    /// 0) to start with no lines and build the deal from the catalog.
    /// </param>
    public record Request(
        string Title, Guid ContactId, Guid? StageId,
        decimal? Amount, DateTime ExpectedCloseDate);

    public record Response(Guid Id, string Title, Guid StageId, string Stage, decimal Amount);

    public class RequestValidator : AbstractValidator<Request>
    {
        public RequestValidator()
        {
            RuleFor(x => x.Title).NotEmpty().WithMessage("Title is required.");
            RuleFor(x => x.ContactId).NotEmpty().WithMessage("A contact is required.");
            RuleFor(x => x.Amount).GreaterThanOrEqualTo(0).When(x => x.Amount is not null).WithMessage("Amount cannot be negative.");
        }
    }

    private static async Task<Results<Created<Response>, ValidationError>> Handle(
        Request request,
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        IUserContext userContext,
        CancellationToken cancellationToken)
    {
        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);

        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        var contactExists = await db.Contacts.AnyAsync(c => c.Id == request.ContactId, cancellationToken);
        if (!contactExists)
            return new ValidationError("The selected contact no longer exists.");

        // The stage is resolved against this tenant's own opportunity stages, so a lead
        // stage id or another tenant's id simply does not resolve.
        PipelineStage? stage;
        if (request.StageId is { } stageId)
        {
            stage = await OpportunityStageResolver.FindAsync(db, stageId, cancellationToken);
            if (stage is null)
                return new ValidationError("The selected stage does not exist for this tenant.");
            if (!stage.IsActive)
                return new ValidationError($"'{stage.Name}' is not an active stage.");
        }
        else
        {
            stage = await OpportunityStageResolver.GetDefaultEntryAsync(db, cancellationToken);
            if (stage is null)
                return new ValidationError("This tenant has no active opportunity stage to place the deal in.");
        }

        var opportunity = Opportunity.Create(
            tenantId, request.Title.Trim(), request.ContactId,
            // Postgres timestamptz requires UTC; a date picked in the browser arrives
            // unspecified and would otherwise throw on save.
            DateTime.SpecifyKind(request.ExpectedCloseDate, DateTimeKind.Utc),
            stage.Id);
        if (request.Amount is > 0m)
            opportunity.SetAmount(request.Amount.Value);

        db.Opportunities.Add(opportunity);

        // The first placement is history too: without it the time a deal spent in its entry
        // stage has no start, so velocity reporting begins at the first *move* instead of at
        // creation.
        StageChangeRecorder.Record(
            db, tenantId, PipelineRecordType.Opportunity, opportunity.Id,
            fromStageId: null, toStageId: stage.Id, userContext.UserId,
            // First placement, so there is no previous pipeline. The deal's pipeline is the
            // one its stage belongs to — read off the stage rather than passed in, so the
            // two cannot disagree.
            fromPipelineId: null, toPipelineId: stage.PipelineId);

        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Created(
            $"/api/opportunities/{opportunity.Id}",
            new Response(opportunity.Id, opportunity.Title, stage.Id, stage.Name, opportunity.Amount));
    }
}
