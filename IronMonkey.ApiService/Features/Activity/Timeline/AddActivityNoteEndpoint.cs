using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.ApiService.Common.Results;

namespace IronMonkey.ApiService.Features.Activity.Timeline;

/// <summary>Adds a manual note to any subject's timeline.</summary>
public class AddActivityNoteEndpoint : IEndpoint
{
    private static readonly string[] AllowedSubjects = ["Lead", "Contact", "Opportunity"];

    public static void Map(IEndpointRouteBuilder app) => app
        .MapPost("/api/activity/{subjectType}/{subjectId:guid}/notes", Handle)
        .WithSummary("Add a manual note to a lead, contact or opportunity timeline")
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
        string subjectType,
        Guid subjectId,
        Request request,
        IValidator<Request> validator,
        ITenantService tenantService,
        IActivityTrackingService activityService,
        IUserContext userContext,
        CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
            return new ValidationError(string.Join("; ", validation.Errors.Select(e => e.ErrorMessage)));

        var subject = AllowedSubjects.FirstOrDefault(
            s => string.Equals(s, subjectType, StringComparison.OrdinalIgnoreCase));

        if (subject is null)
            return new ValidationError($"Invalid subject type. Valid values: {string.Join(", ", AllowedSubjects)}");

        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);

        var id = await activityService.AddNoteForAsync(
            tenantId, connectionString, subject, subjectId, userContext.UserId, request.Content, cancellationToken);

        return TypedResults.Ok(new Response(id));
    }
}
