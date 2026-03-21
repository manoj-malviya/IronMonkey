using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.Data;

namespace IronMonkey.ApiService.Authentication.Endpoints;

public class ListSignupRequestsEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapGet("/signup", Handle)
        .WithSummary("List all signup requests with optional status filter")
        .WithTags("Platform Admin");

    public record SignupRequestSummary(
        Guid Id,
        string CompanyName,
        string AdminEmail,
        string Status,
        DateTime CreatedAt);

    private static async Task<Ok<List<SignupRequestSummary>>> Handle(
        string? status,
        CentralDbContext centralDb,
        CancellationToken cancellationToken)
    {
        var query = centralDb.SignupRequests.AsNoTracking();

        if (!string.IsNullOrEmpty(status))
        {
            query = query.Where(r => r.Status == status);
        }

        var results = await query
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new SignupRequestSummary(r.Id, r.CompanyName, r.AdminEmail, r.Status, r.CreatedAt))
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(results);
    }
}
