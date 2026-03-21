using Microsoft.AspNetCore.Http.HttpResults;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.Data.Entities;

namespace IronMonkey.ApiService.Features.Leads.Merge;

public class MergeLeadsEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapPost("/api/leads/{id}/merge", Handle)
        .WithSummary("Merge a target lead into the source lead (source survives)")
        .WithTags("Leads")
        .RequireAuthorization();

    public record Request(Guid TargetLeadId);

    public record Response(
        Guid Id,
        string FirstName,
        string LastName,
        string Email,
        string Source,
        Guid PipelineStageId,
        Dictionary<string, object?> CustomFields);

    private static async Task<Results<Ok<Response>, BadRequest<string>, NotFound>> Handle(
        Guid id,
        Request request,
        ILeadMergeService mergeService,
        IUserContext userContext,
        CancellationToken cancellationToken)
    {
        if (id == request.TargetLeadId)
            return TypedResults.BadRequest("Cannot merge a lead with itself");

        var tenantId = userContext.TenantId;
        var userId = userContext.UserId;

        try
        {
            var mergedLead = await mergeService.MergeAsync(tenantId, id, request.TargetLeadId, userId, cancellationToken);

            var response = new Response(
                mergedLead.Id,
                mergedLead.FirstName,
                mergedLead.LastName,
                mergedLead.Email,
                mergedLead.Source.ToString(),
                mergedLead.PipelineStageId,
                mergedLead.CustomFields.Values);

            return TypedResults.Ok(response);
        }
        catch (KeyNotFoundException)
        {
            return TypedResults.NotFound();
        }
        catch (ArgumentException ex)
        {
            return TypedResults.BadRequest(ex.Message);
        }
    }
}
