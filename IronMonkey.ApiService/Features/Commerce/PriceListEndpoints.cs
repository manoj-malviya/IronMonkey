using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.ApiService.Common.Results;
using IronMonkey.Common.Auth;
using IronMonkey.Data;
using IronMonkey.Data.Commerce;
using IronMonkey.Data.Entities;

namespace IronMonkey.ApiService.Features.Commerce;

public sealed record PriceListResponse(Guid Id, string Name, string? Description, string? CurrencyCode, bool IsDefault, bool IsActive);

public sealed record SavePriceListRequest(string Name, string? Description, string? CurrencyCode, bool? IsDefault, bool? IsActive);

internal static class PriceListDefaults
{
    public const string DefaultName = "Standard";

    /// <summary>
    /// Returns the tenant's default price list, creating "Standard" (in the tenant's own
    /// currency) if there is none. A catalog with no default list has no fallback price, so
    /// the first price anyone sets creates it.
    /// </summary>
    public static async Task<PriceList> EnsureDefaultAsync(TenantDbContext db, Guid tenantId, CancellationToken ct)
    {
        var existing = await db.PriceLists.FirstOrDefaultAsync(p => p.IsDefault, ct)
                       ?? db.PriceLists.Local.FirstOrDefault(p => p.IsDefault);
        if (existing is not null) return existing;

        var list = PriceList.Create(tenantId, DefaultName, currencyCode: null, isDefault: true,
            "Fallback prices used when a deal has no price list of its own.");
        db.PriceLists.Add(list);
        return list;
    }

    public static PriceListResponse ToResponse(PriceList p) =>
        new(p.Id, p.Name, p.Description, p.CurrencyCode, p.IsDefault, p.IsActive);
}

public class ListPriceListsEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapGet("/api/price-lists", Handle)
        .WithSummary("List price lists")
        .WithTags("Catalog")
        .RequireAuthorization();

    internal static async Task<Ok<List<PriceListResponse>>> Handle(
        ITenantService tenantService, ITenantDbContextFactory dbContextFactory, CancellationToken cancellationToken)
    {
        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);
        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        var lists = await db.PriceLists.AsNoTracking()
            .OrderByDescending(p => p.IsDefault).ThenBy(p => p.Name).ThenBy(p => p.Id)
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(lists.Select(PriceListDefaults.ToResponse).ToList());
    }
}

public class CreatePriceListEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapPost("/api/price-lists", Handle)
        .WithSummary("Create a price list (segment, region or agreement pricing)")
        .WithTags("Catalog")
        .RequireAuthorization(PermissionConstants.CatalogWrite);

    internal static async Task<Results<Created<PriceListResponse>, ValidationError>> Handle(
        SavePriceListRequest request,
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name)) return new ValidationError("Name is required.");
        if (!MoneyMath.TryNormalizeCurrency(request.CurrencyCode, out var currency))
            return new ValidationError("Currency must be a three-letter ISO 4217 code, e.g. GBP.");

        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);
        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        var hasDefault = await db.PriceLists.AnyAsync(p => p.IsDefault, cancellationToken);
        var makeDefault = request.IsDefault == true || !hasDefault;

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        if (makeDefault && hasDefault)
        {
            // Cleared and saved first: the filtered unique index permits one default, and a
            // single statement batch would briefly hold two.
            foreach (var current in await db.PriceLists.Where(p => p.IsDefault).ToListAsync(cancellationToken))
                current.ClearDefault();
            await db.SaveChangesAsync(cancellationToken);
        }

        var list = PriceList.Create(tenantId, request.Name, currency, makeDefault, request.Description);
        db.PriceLists.Add(list);
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        return TypedResults.Created($"/api/price-lists/{list.Id}", PriceListDefaults.ToResponse(list));
    }
}

public class UpdatePriceListEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapPut("/api/price-lists/{id:guid}", Handle)
        .WithSummary("Rename, activate or make a price list the default. Currency is fixed once created.")
        .WithTags("Catalog")
        .RequireAuthorization(PermissionConstants.CatalogWrite);

    /// <remarks>Currency cannot change: every price already in the list is in that currency,
    /// and relabelling them would reprice every deal priced from it.</remarks>
    internal static async Task<Results<Ok<PriceListResponse>, ValidationError, NotFound>> Handle(
        Guid id,
        SavePriceListRequest request,
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name)) return new ValidationError("Name is required.");

        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);
        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        var list = await db.PriceLists.SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (list is null) return TypedResults.NotFound();

        if (request.IsActive == false && list.IsDefault)
            return new ValidationError("The default price list cannot be deactivated. Make another list the default first.");

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

        if (request.IsDefault == true && !list.IsDefault)
        {
            foreach (var current in await db.PriceLists.Where(p => p.IsDefault).ToListAsync(cancellationToken))
                current.ClearDefault();
            await db.SaveChangesAsync(cancellationToken);
            list.MarkDefault();
            list.Activate();
        }

        list.Update(request.Name, request.Description);
        if (request.IsActive == true) list.Activate();
        else if (request.IsActive == false) list.Deactivate();

        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        return TypedResults.Ok(PriceListDefaults.ToResponse(list));
    }
}
