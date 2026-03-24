using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.ApiService.Common.Results;

namespace IronMonkey.ApiService.Features.Activity.Timeline;

public class AddLeadNoteEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapPost("/api/leads/{leadId}/activity/notes", Handle)
        .WithSummary("Add a manual note to a lead's activity timeline")
        .WithTags("Activity")
        .RequireAuthorization();

    public record Request(string Content);
    public record Response(Guid ActivityLogId);

    public class RequestValidator : AbstractValidator<Request>
    {
        public RequestValidator()
        {
            RuleFor(r => r.Content).NotEmpty().MaximumLength(2000);
        }
    }

    private static async Task<Results<Ok<Response>, ValidationError>> Handle(
        Guid leadId,
        Request request,
        IValidator<Request> validator,
        ITenantService tenantService,
        IActivityTrackingService activityService,
        IUserContext userContext,
        CancellationToken cancellationToken)
    {
        var validationResult = await validator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
            return new ValidationError(string.Join("; ", validationResult.Errors.Select(e => e.ErrorMessage)));

        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);
        var actorId = userContext.UserId;

        await activityService.AddNoteAsync(tenantId, connectionString, leadId, actorId, request.Content, cancellationToken);

        return TypedResults.Ok(new Response(Guid.NewGuid()));
    }
}
