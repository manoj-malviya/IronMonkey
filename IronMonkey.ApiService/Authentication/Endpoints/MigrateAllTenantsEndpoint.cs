using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.BackgroundJobs;
using IronMonkey.ApiService.Common;
using IronMonkey.Data;

namespace IronMonkey.ApiService.Authentication.Endpoints;

public class MigrateAllTenantsEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapPost("/migrate-all", Handle)
        .WithSummary("Apply pending schema migrations to all provisioned tenant databases")
        .WithTags("Platform Admin")
        .RequireAuthorization();

    public record Response(int MigratedCount, int FailedCount, IReadOnlyList<string> Errors);

    private static async Task<Ok<Response>> Handle(
        ITenantRegistry tenantRegistry,
        ITenantDbContextFactory tenantContextFactory,
        ILogger<MigrateAllTenantsEndpoint> logger,
        CancellationToken cancellationToken)
    {
        var tenants = await tenantRegistry.GetAllProvisionedTenantsAsync(cancellationToken);
        var errors = new List<string>();
        var migratedCount = 0;

        foreach (var (tenantId, connectionString) in tenants)
        {
            try
            {
                await using var db = tenantContextFactory.CreateForTenant(connectionString, tenantId);
                await db.Database.MigrateAsync(cancellationToken);
                migratedCount++;
                logger.LogInformation("Applied migrations to tenant {TenantId}", tenantId);
            }
            catch (Exception ex)
            {
                var errorMsg = $"Tenant {tenantId}: {ex.Message}";
                errors.Add(errorMsg);
                logger.LogError(ex, "Failed to migrate tenant {TenantId}", tenantId);
                // Log but continue — partial failures should not abort the whole run
            }
        }

        logger.LogInformation("Migration run complete. Migrated: {MigratedCount}, Failed: {FailedCount}",
            migratedCount, errors.Count);

        return TypedResults.Ok(new Response(migratedCount, errors.Count, errors));
    }
}
