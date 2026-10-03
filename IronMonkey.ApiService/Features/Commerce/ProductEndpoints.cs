using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.ApiService.Common.Results;
using IronMonkey.ApiService.Features.CustomFields;
using IronMonkey.Common.Auth;
using IronMonkey.Data;
using IronMonkey.Data.Commerce;
using IronMonkey.Data.Entities;

namespace IronMonkey.ApiService.Features.Commerce;

public sealed record PriceVersionResponse(
    Guid Id, Guid PriceListId, string PriceListName, string? CurrencyCode,
    decimal UnitPrice, decimal? UnitCost, DateTime EffectiveFrom, DateTime? EffectiveTo, bool IsCurrent);

/// <param name="ListPrice">The current price in the tenant's default price list, if any.</param>
public sealed record ProductResponse(
    Guid Id, string Code, string Name, string? Description, string? Category, bool IsActive,
    string ChargeType, string BillingFrequency, int DefaultPeriods, string? UnitOfMeasure,
    decimal DefaultTaxRatePercent, decimal? ListPrice, decimal? ListCost, string? ListCurrencyCode,
    Dictionary<string, object?> CustomFields, List<PriceVersionResponse> Prices);

/// <param name="ListPrice">Optional opening price in the default price list.</param>
public sealed record SaveProductRequest(
    string Code, string Name, string? Description, string? Category,
    string ChargeType, string? BillingFrequency, int? DefaultPeriods, string? UnitOfMeasure,
    decimal? DefaultTaxRatePercent, decimal? ListPrice, decimal? ListCost, bool? IsActive,
    Dictionary<string, object?>? CustomFields);

/// <param name="PriceListId">Null = the tenant's default price list.</param>
/// <param name="EffectiveFrom">Null = now. Must be after the current version's start.</param>
public sealed record AddPriceRequest(Guid? PriceListId, decimal UnitPrice, decimal? UnitCost, DateTime? EffectiveFrom);

internal static class ProductMapping
{
    public static async Task<ProductResponse> ToResponseAsync(TenantDbContext db, Product product, DateTime nowUtc, CancellationToken ct)
    {
        var prices = await db.ProductPrices
            .AsNoTracking()
            .Include(p => p.PriceList)
            .Where(p => p.ProductId == product.Id)
            .OrderBy(p => p.PriceList.Name).ThenByDescending(p => p.EffectiveFrom)
            .ToListAsync(ct);

        var versions = prices.Select(p => new PriceVersionResponse(
            p.Id, p.PriceListId, p.PriceList.Name, p.PriceList.CurrencyCode,
            p.UnitPrice, p.UnitCost, p.EffectiveFrom, p.EffectiveTo, p.IsEffectiveAt(nowUtc))).ToList();

        var list = prices.FirstOrDefault(p => p.PriceList.IsDefault && p.IsEffectiveAt(nowUtc));

        return new ProductResponse(
            product.Id, product.Code, product.Name, product.Description, product.Category, product.IsActive,
            product.ChargeType.ToString(), product.BillingFrequency.ToString(), product.DefaultPeriods,
            product.UnitOfMeasure, product.DefaultTaxRatePercent,
            list?.UnitPrice, list?.UnitCost, list?.PriceList.CurrencyCode,
            product.CustomFields.Values, versions);
    }

    public static string? Validate(SaveProductRequest request, out ChargeType chargeType, out BillingFrequency frequency)
    {
        frequency = BillingFrequency.None;
        if (!Enum.TryParse(request.ChargeType, ignoreCase: true, out chargeType))
            return $"Charge type must be one of: {string.Join(", ", Enum.GetNames<ChargeType>())}.";
        if (!string.IsNullOrWhiteSpace(request.BillingFrequency)
            && !Enum.TryParse(request.BillingFrequency, ignoreCase: true, out frequency))
            return $"Billing frequency must be one of: {string.Join(", ", Enum.GetNames<BillingFrequency>())}.";
        if (chargeType == ChargeType.Recurring && frequency == BillingFrequency.None)
            return "A recurring product needs a billing frequency.";
        if (string.IsNullOrWhiteSpace(request.Code)) return "Code is required.";
        if (request.Code.Trim().Length > 64) return "Code must be 64 characters or fewer.";
        if (string.IsNullOrWhiteSpace(request.Name)) return "Name is required.";
        if (request.DefaultPeriods is < 1 or > 1200) return "Default periods must be between 1 and 1200.";
        if (request.DefaultTaxRatePercent is < 0 or > 100) return "Tax rate must be between 0 and 100 percent.";
        if (request.ListPrice is < 0) return "List price cannot be negative.";
        if (request.ListCost is < 0) return "Cost cannot be negative.";
        return null;
    }

    public static async Task<string?> BindCustomFieldsAsync(TenantDbContext db, Product product,
        Dictionary<string, object?>? submitted, CancellationToken ct)
    {
        // The same binder as leads and contacts — product attributes are custom fields with
        // Product scope, not a parallel field system.
        var definitions = await db.CustomFieldDefinitions
            .Where(f => f.AppliesTo == CustomFieldEntity.Product)
            .ToListAsync(ct);

        var bound = CustomFieldValueBinder.Bind(definitions, submitted);
        if (!bound.IsValid) return string.Join(" ", bound.Errors);

        product.SetCustomFields(bound.Values);
        return null;
    }
}

public class ListProductsEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapGet("/api/products", Handle)
        .WithSummary("List the tenant's product catalog")
        .WithTags("Catalog")
        .RequireAuthorization();

    /// <param name="includeInactive">Nullable on purpose: a non-nullable bool query parameter
    /// is required in minimal APIs and 500s every caller that omits it.</param>
    internal static async Task<Ok<List<ProductResponse>>> Handle(
        bool? includeInactive,
        string? category,
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);
        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        var query = db.Products.AsNoTracking();
        if (includeInactive != true) query = query.Where(p => p.IsActive);
        if (!string.IsNullOrWhiteSpace(category)) query = query.Where(p => p.Category == category);

        var products = await query.OrderBy(p => p.Category).ThenBy(p => p.Name).ThenBy(p => p.Id).ToListAsync(cancellationToken);
        var now = timeProvider.GetUtcNow().UtcDateTime;

        var result = new List<ProductResponse>(products.Count);
        foreach (var product in products)
            result.Add(await ProductMapping.ToResponseAsync(db, product, now, cancellationToken));

        return TypedResults.Ok(result);
    }
}

public class GetProductEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapGet("/api/products/{id:guid}", Handle)
        .WithSummary("Get a product with its full price history")
        .WithTags("Catalog")
        .RequireAuthorization();

    internal static async Task<Results<Ok<ProductResponse>, NotFound>> Handle(
        Guid id,
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);
        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        var product = await db.Products.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (product is null) return TypedResults.NotFound();

        return TypedResults.Ok(await ProductMapping.ToResponseAsync(db, product, timeProvider.GetUtcNow().UtcDateTime, cancellationToken));
    }
}

public class CreateProductEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapPost("/api/products", Handle)
        .WithSummary("Add a product to the catalog")
        .WithTags("Catalog")
        .RequireAuthorization(PermissionConstants.CatalogWrite);

    internal static async Task<Results<Created<ProductResponse>, ValidationError, Conflict<string>>> Handle(
        SaveProductRequest request,
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (ProductMapping.Validate(request, out var chargeType, out var frequency) is { } error)
            return new ValidationError(error);

        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);
        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        var code = request.Code.Trim();
        if (await db.Products.AnyAsync(p => p.Code.ToLower() == code.ToLower(), cancellationToken))
            return TypedResults.Conflict($"A product with code '{code}' already exists.");

        var product = Product.Create(tenantId, code, request.Name, chargeType, frequency, request.DefaultPeriods ?? 1);
        product.Update(code, request.Name, request.Description, request.Category, chargeType, frequency,
            request.DefaultPeriods ?? 1, request.UnitOfMeasure, request.DefaultTaxRatePercent ?? 0m);
        if (request.IsActive == false) product.Deactivate();

        if (await ProductMapping.BindCustomFieldsAsync(db, product, request.CustomFields, cancellationToken) is { } fieldError)
            return new ValidationError(fieldError);

        db.Products.Add(product);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (request.ListPrice is { } listPrice)
        {
            var list = await PriceListDefaults.EnsureDefaultAsync(db, tenantId, cancellationToken);
            db.ProductPrices.Add(ProductPrice.Create(tenantId, product.Id, list.Id, listPrice, request.ListCost, now));
        }

        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Created($"/api/products/{product.Id}",
            await ProductMapping.ToResponseAsync(db, product, now, cancellationToken));
    }
}

public class UpdateProductEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapPut("/api/products/{id:guid}", Handle)
        .WithSummary("Update a product. Prices are versioned separately via POST /prices.")
        .WithTags("Catalog")
        .RequireAuthorization(PermissionConstants.CatalogWrite);

    /// <remarks>
    /// <c>ListPrice</c> on this request is ignored deliberately. Changing a price is a new
    /// version with an effective date, never an in-place edit — see
    /// <see cref="AddProductPriceEndpoint"/>.
    /// </remarks>
    internal static async Task<Results<Ok<ProductResponse>, ValidationError, NotFound, Conflict<string>>> Handle(
        Guid id,
        SaveProductRequest request,
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (ProductMapping.Validate(request, out var chargeType, out var frequency) is { } error)
            return new ValidationError(error);

        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);
        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        var product = await db.Products.SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (product is null) return TypedResults.NotFound();

        var code = request.Code.Trim();
        if (await db.Products.AnyAsync(p => p.Id != id && p.Code.ToLower() == code.ToLower(), cancellationToken))
            return TypedResults.Conflict($"A product with code '{code}' already exists.");

        product.Update(code, request.Name, request.Description, request.Category, chargeType, frequency,
            request.DefaultPeriods ?? product.DefaultPeriods, request.UnitOfMeasure,
            request.DefaultTaxRatePercent ?? product.DefaultTaxRatePercent);

        if (request.IsActive == false) product.Deactivate();
        else if (request.IsActive == true) product.Activate();

        if (await ProductMapping.BindCustomFieldsAsync(db, product, request.CustomFields, cancellationToken) is { } fieldError)
            return new ValidationError(fieldError);

        product.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(await ProductMapping.ToResponseAsync(db, product, timeProvider.GetUtcNow().UtcDateTime, cancellationToken));
    }
}

public class AddProductPriceEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapPost("/api/products/{id:guid}/prices", Handle)
        .WithSummary("Add a new price version effective from a date")
        .WithTags("Catalog")
        .RequireAuthorization(PermissionConstants.CatalogWrite);

    internal static async Task<Results<Ok<ProductResponse>, ValidationError, NotFound>> Handle(
        Guid id,
        AddPriceRequest request,
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (request.UnitPrice < 0) return new ValidationError("Unit price cannot be negative.");
        if (request.UnitCost is < 0) return new ValidationError("Cost cannot be negative.");

        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);
        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        var product = await db.Products.SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (product is null) return TypedResults.NotFound();

        PriceList list;
        if (request.PriceListId is { } listId)
        {
            var found = await db.PriceLists.SingleOrDefaultAsync(p => p.Id == listId, cancellationToken);
            if (found is null) return new ValidationError("That price list does not exist.");
            list = found;
        }
        else
        {
            list = await PriceListDefaults.EnsureDefaultAsync(db, tenantId, cancellationToken);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var effectiveFrom = request.EffectiveFrom is { } requested
            ? DateTime.SpecifyKind(requested, requested.Kind == DateTimeKind.Unspecified ? DateTimeKind.Utc : requested.Kind).ToUniversalTime()
            : now;

        var (_, versionError) = await PriceResolver.AddVersionAsync(
            db, tenantId, product.Id, list.Id, request.UnitPrice, request.UnitCost, effectiveFrom, cancellationToken);
        if (versionError is not null) return new ValidationError(versionError);

        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(await ProductMapping.ToResponseAsync(db, product, now, cancellationToken));
    }
}
