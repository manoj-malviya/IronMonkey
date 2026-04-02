using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.Data;

namespace IronMonkey.ApiService.Authentication.Endpoints;

public class GetSignupRequestEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapGet("/signup/{id}", Handle)
        .WithSummary("Get full details of a signup request")
        .WithTags("Platform Admin");

    public record SignupRequestDetail(
        Guid Id,
        string CompanyName,
        string AdminEmail,
        string Phone,
        string CompanySize,
        string Address,
        string BillingContact,
        Guid? RecipeId,
        string Status,
        string? ReviewNote,
        DateTime CreatedAt);

    private static async Task<Results<Ok<SignupRequestDetail>, NotFound>> Handle(
        Guid id,
        CentralDbContext centralDb,
        CancellationToken cancellationToken)
    {
        var signup = await centralDb.SignupRequests
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

        if (signup is null)
            return TypedResults.NotFound();

        return TypedResults.Ok(new SignupRequestDetail(
            signup.Id,
            signup.CompanyName,
            signup.AdminEmail,
            signup.Phone,
            signup.CompanySize,
            signup.Address,
            signup.BillingContact,
            signup.RecipeId,
            signup.Status,
            signup.ReviewNote,
            signup.CreatedAt));
    }

    // Test-accessible handler — same logic as Handle, exposed for integration testing
    internal static async Task<Results<Ok<SignupRequestDetail>, NotFound>> HandleForTest(
        Guid id,
        CentralDbContext centralDb,
        CancellationToken cancellationToken)
    {
        return await Handle(id, centralDb, cancellationToken);
    }
}
