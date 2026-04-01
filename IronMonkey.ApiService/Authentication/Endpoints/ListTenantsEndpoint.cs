using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.Data;

namespace IronMonkey.ApiService.Authentication.Endpoints;

public class ListTenantsEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapGet("/tenants", Handle)
        .WithSummary("List all tenants")
        .WithTags("Platform Admin");

    public record TenantSummary(
        Guid Id,
        string Name,
        string Status,
        string SubscriptionPlan,
        bool IsProvisioned,
        Guid? AppliedRecipeId,
        DateTime CreatedAt);

    private static async Task<Ok<List<TenantSummary>>> Handle(
        CentralDbContext centralDb,
        CancellationToken cancellationToken)
    {
        var results = await centralDb.Tenants
            .AsNoTracking()
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => new TenantSummary(
                t.Id,
                t.Name,
                t.Status,
                t.SubscriptionPlan,
                t.IsProvisioned,
                t.AppliedRecipeId,
                t.CreatedAt))
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(results);
    }

    // Test-accessible handler — same logic as Handle, exposed for integration testing
    internal static async Task<List<TenantSummary>> HandleForTest(
        CentralDbContext centralDb,
        CancellationToken cancellationToken)
    {
        var okResult = await Handle(centralDb, cancellationToken);
        return okResult.Value!;
    }
}
