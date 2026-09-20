using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.Data;

namespace IronMonkey.ApiService.Authentication.Endpoints;

/// <summary>
/// Aggregate counts for the platform dashboard. Everything here is a central-DB
/// count — deliberately no per-tenant fan-out, which would mean opening one
/// connection per tenant to render a summary card.
/// </summary>
public class PlatformStatsEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapGet("/stats", Handle)
        .WithSummary("Aggregate platform counts for the admin dashboard")
        .WithTags("Platform Admin");

    public record Response(
        int TotalTenants,
        int ProvisionedTenants,
        int PendingSignups,
        int ApprovedAwaitingProvision);

    private static async Task<Ok<Response>> Handle(
        CentralDbContext centralDb,
        CancellationToken cancellationToken)
    {
        var totalTenants = await centralDb.Tenants
            .AsNoTracking()
            .CountAsync(cancellationToken);

        var provisionedTenants = await centralDb.Tenants
            .AsNoTracking()
            .CountAsync(t => t.IsProvisioned, cancellationToken);

        var pendingSignups = await centralDb.SignupRequests
            .AsNoTracking()
            .CountAsync(r => r.Status == "Pending", cancellationToken);

        // Approved requests whose tenant never finished provisioning — the queue the
        // "Provision" retry button on the tenants page acts on.
        var approvedAwaitingProvision = await centralDb.SignupRequests
            .AsNoTracking()
            .CountAsync(r => r.Status == "Approved", cancellationToken);

        return TypedResults.Ok(new Response(
            totalTenants,
            provisionedTenants,
            pendingSignups,
            approvedAwaitingProvision));
    }

    // Test-accessible handler — same logic as Handle, exposed for integration testing
    internal static async Task<Response> HandleForTest(
        CentralDbContext centralDb,
        CancellationToken cancellationToken)
    {
        var okResult = await Handle(centralDb, cancellationToken);
        return okResult.Value!;
    }
}
