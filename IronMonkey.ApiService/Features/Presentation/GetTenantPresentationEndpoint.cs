using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.Data;

namespace IronMonkey.ApiService.Features.Presentation;

/// <summary>
/// Serves the current tenant's terminology, locale and branding.
///
/// Reads the central tenant row rather than the tenant database: the web shell needs these
/// values on every render, including before any tenant-scoped query has run, and they are
/// per-tenant configuration rather than tenant business data.
///
/// The tenant is taken from the authenticated claim, never from the request — a tenant id in
/// the query string would let any authenticated user read another tenant's configuration.
/// </summary>
public class GetTenantPresentationEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapGet("/api/tenant/presentation", Handle)
        .WithSummary("Get the current tenant's terminology, locale and branding")
        .WithTags("Presentation")
        .RequireAuthorization();

    private static async Task<Results<Ok<TenantPresentationResponse>, NotFound>> Handle(
        ITenantService tenantService,
        CentralDbContext centralDb,
        CancellationToken cancellationToken)
    {
        var tenantId = tenantService.GetCurrentTenantId();

        var tenant = await centralDb.Tenants
            .AsNoTracking()
            .SingleOrDefaultAsync(t => t.Id == tenantId, cancellationToken);

        if (tenant is null)
            return TypedResults.NotFound();

        return TypedResults.Ok(PresentationMapping.ToResponse(tenant.Presentation, tenant.Name));
    }
}
