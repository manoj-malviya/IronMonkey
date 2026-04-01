using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Results;
using IronMonkey.Data;

namespace IronMonkey.ApiService.Authentication.Endpoints;

public class ApproveTenantEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapPost("/signup/{id}/approve", Handle)
        .WithSummary("Approve a pending signup request")
        .WithTags("Platform Admin");

    public record Request(string? ApprovalNote);
    public record Response(string Message);

    internal static async Task<Results<Ok<Response>, NotFound, ValidationError>> Handle(
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
            return new ValidationError($"Signup request is already {signupRequest.Status} and cannot be approved.");

        signupRequest.Approve(request.ApprovalNote);
        await centralDb.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(new Response("Signup request approved successfully."));
    }
}
