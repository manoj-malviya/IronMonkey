using Microsoft.AspNetCore.Http.HttpResults;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;

namespace IronMonkey.ApiService.Features.Leads.Duplicates;

public class CheckDuplicatesEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapPost("/api/leads/check-duplicates", Handle)
        .WithSummary("Check for duplicate leads before saving")
        .WithTags("Leads")
        .RequireAuthorization();

    public record Request(string? Email, string? Phone, string? Name);

    public record CandidateDto(Guid LeadId, string FullName, string Email, string Reason, int ConfidenceScore);

    public record Response(List<CandidateDto> Candidates);

    private static async Task<Results<Ok<Response>, BadRequest<string>>> Handle(
        Request request,
        IDuplicateDetectionService duplicateDetectionService,
        IUserContext userContext,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Email) &&
            string.IsNullOrWhiteSpace(request.Phone) &&
            string.IsNullOrWhiteSpace(request.Name))
        {
            return TypedResults.BadRequest("At least one of email, phone, or name is required");
        }

        var tenantId = userContext.TenantId;
        var candidates = await duplicateDetectionService.FindCandidatesAsync(
            tenantId, request.Email, request.Phone, request.Name, cancellationToken);

        var dtos = candidates
            .Select(c => new CandidateDto(c.LeadId, c.FullName, c.Email, c.Reason, c.ConfidenceScore))
            .ToList();

        return TypedResults.Ok(new Response(dtos));
    }
}
