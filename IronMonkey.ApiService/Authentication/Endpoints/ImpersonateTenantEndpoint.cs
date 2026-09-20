using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.Common.Auth;
using IronMonkey.Data;
using IronMonkey.Data.Entities;

namespace IronMonkey.ApiService.Authentication.Endpoints;

/// <summary>
/// Issues a short-lived token scoped to one tenant, so a platform operator can use the
/// ordinary tenant endpoints for support and debugging.
///
/// A platform user has no row in any tenant database and their token carries
/// <c>tenant_id = Guid.Empty</c>, which <see cref="TenantService"/> rejects — so without this,
/// every tenant-scoped endpoint is unreachable for them. Rather than duplicating ~45 endpoints
/// behind /admin routes, the caller borrows the tenant's own Admin identity: permissions then
/// resolve through the normal role_permissions path with no change to the authorization code.
/// </summary>
public class ImpersonateTenantEndpoint : IEndpoint
{
    /// <summary>
    /// Deliberately short. The token is stateless and cannot be revoked, so its lifetime is
    /// the only bound on a borrowed tenant identity.
    /// </summary>
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(30);

    public static void Map(IEndpointRouteBuilder app) => app
        .MapPost("/tenants/{id}/impersonate", Handle)
        .WithSummary("Issue a short-lived token scoped to a tenant, assuming its Admin identity")
        .WithTags("Platform Admin");

    public record Request(string? Reason);

    public record Response(
        string Token,
        Guid TenantId,
        string TenantName,
        string ImpersonatedUserEmail,
        DateTime ExpiresAt);

    private static async Task<Results<Ok<Response>, BadRequest<string>, NotFound, ForbidHttpResult>> Handle(
        Guid id,
        Request? request,
        CentralDbContext centralDb,
        ITenantDbContextFactory tenantContextFactory,
        ITenantConnectionStringResolver connectionStringResolver,
        IUserContext userContext,
        Jwt jwt,
        ILogger<ImpersonateTenantEndpoint> logger,
        CancellationToken cancellationToken)
    {
        // Defense in depth: the /admin group already requires admin:access, which only a
        // platform role grants. This additionally blocks an impersonation token from minting
        // another, so a borrowed identity can never be chained into a fresh 30-minute lease.
        if (userContext.ActAs is not null)
        {
            logger.LogWarning(
                "Rejected chained impersonation of tenant {TenantId} from a token already acting as {ActAs}.",
                id, userContext.ActAs);
            return TypedResults.Forbid();
        }

        var tenant = await centralDb.Tenants
            .AsNoTracking()
            .SingleOrDefaultAsync(t => t.Id == id, cancellationToken);

        if (tenant is null)
            return TypedResults.NotFound();

        if (!tenant.IsProvisioned || tenant.DatabaseConnectionString is null)
            return TypedResults.BadRequest($"Tenant '{tenant.Name}' is not provisioned yet.");

        await using var tenantDb = tenantContextFactory.CreateForTenant(
            connectionStringResolver.Resolve(tenant.DatabaseConnectionString), tenant.Id);

        // Query from Roles, not Users: User.Roles is a computed property (`=> _roles.ToList()`)
        // that EF cannot translate, so `u.Roles.Any(...)` throws at runtime. Role.Users is the
        // real mapped navigation — the same direction AuthorizationService traverses.
        var adminUser = await tenantDb.Roles
            .Where(r => r.Name == RoleConstants.Admin)
            .SelectMany(r => r.Users)
            .OrderBy(u => u.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (adminUser is null)
        {
            // Structurally unexpected: provisioning always creates an Admin from the signup
            // credentials. Log loudly rather than returning a bare 500 — it means the tenant
            // database was provisioned incompletely or its Admin was deleted.
            logger.LogError(
                "Tenant {TenantId} ({TenantName}) has no Admin user; cannot impersonate.",
                tenant.Id, tenant.Name);
            return TypedResults.BadRequest($"Tenant '{tenant.Name}' has no Admin user to impersonate.");
        }

        var platformUserId = userContext.UserId;

        var platformUserEmail = await centralDb.PlatformUsers
            .AsNoTracking()
            .Where(u => u.Id == platformUserId)
            .Select(u => u.Email)
            .SingleOrDefaultAsync(cancellationToken) ?? string.Empty;

        var expiresAt = DateTime.UtcNow.Add(TokenLifetime);

        var token = jwt.GenerateToken(
            new LoggedInUser(
                adminUser.Id.ToString(),
                adminUser.Name,
                adminUser.Email,
                RoleConstants.Admin,
                tenant.Id),
            lifetime: TokenLifetime,
            actAs: platformUserId.ToString());

        centralDb.TenantImpersonations.Add(TenantImpersonation.Create(
            platformUserId: platformUserId,
            platformUserEmail: platformUserEmail,
            tenantId: tenant.Id,
            impersonatedUserId: adminUser.Id,
            impersonatedUserEmail: adminUser.Email,
            expiresAt: expiresAt,
            reason: request?.Reason));

        await centralDb.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Platform user {PlatformUserId} ({PlatformUserEmail}) is impersonating {ImpersonatedEmail} " +
            "in tenant {TenantId} ({TenantName}) until {ExpiresAt:o}.",
            platformUserId, platformUserEmail, adminUser.Email, tenant.Id, tenant.Name, expiresAt);

        return TypedResults.Ok(new Response(
            token,
            tenant.Id,
            tenant.Name,
            adminUser.Email,
            expiresAt));
    }
}
