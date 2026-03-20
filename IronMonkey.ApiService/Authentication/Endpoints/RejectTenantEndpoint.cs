using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Results;
using IronMonkey.Data;

namespace IronMonkey.ApiService.Authentication.Endpoints;

public class RejectTenantEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapPost("/signup/{id}/reject", Handle)
        .WithSummary("Reject a pending signup request")
        .WithTags("Platform Admin");

    public record Request(string? RejectionReason);
    public record Response(string Message);

    private static async Task<Results<Ok<Response>, NotFound, ValidationError>> Handle(
        Guid id,
        Request request,
        CentralDbContext centralDb,
        CancellationToken cancellationToken)
    {
        var signupRequest = await centralDb.SignupRequests
            .SingleOrDefaultAsync(r => r.Id == id, cancellationToken);

        if (signupRequest is null)
            return TypedResults.NotFound();

        if (signupRequest.Status != "Pending")
            return new ValidationError($"Signup request is already {signupRequest.Status} and cannot be rejected.");

        signupRequest.Reject(request.RejectionReason);
        await centralDb.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(new Response("Signup request rejected."));
    }
}
