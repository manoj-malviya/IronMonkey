using IronMonkey.ApiService.Authentication.Services;
using IronMonkey.Data;
using IronMonkey.Data.Commerce;
using IronMonkey.Data.Entities;
using IronMonkey.Data.RecipeContent;
using IronMonkey.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;
using BC = BCrypt.Net.BCrypt;

namespace IronMonkey.Tests.Integration;

/// <summary>
/// Recipes seed a starter catalog and quote template — and recipes stored before catalogs
/// existed must still provision, to a tenant with an empty catalog.
/// </summary>
[Collection("Integration")]
public class RecipeCatalogProvisioningTests(PostgreSqlFixture fixture) : IClassFixture<PostgreSqlFixture>
{
    private static readonly Guid BlankRecipeId = new("00000000-0000-0000-0000-000000000001");
    private static readonly Guid AutomobileRecipeId = new("00000000-0000-0000-0000-000000000002");

    private CentralDbContext Central() =>
        new(new DbContextOptionsBuilder<CentralDbContext>().UseNpgsql(fixture.ConnectionString).Options);

    private async Task<TenantDbContext> ProvisionAsync(CentralDbContext central, Guid recipeId, string label)
    {
        var unique = Guid.NewGuid().ToString("N")[..8];
        var signup = SignupRequest.Create($"{label} {unique}", $"admin-{unique}@{label.ToLowerInvariant()}.test",
            BC.HashPassword("SecurePass123!"), "555", null, "1-10", "1 St", $"billing-{unique}@t.test");
        central.SignupRequests.Add(signup);
        await central.SaveChangesAsync();
        signup.Approve();
        await central.SaveChangesAsync();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:CentralDb"] = fixture.ConnectionString })
            .Build();
        await new TenantProvisioningService(central, new TenantDbContextFactory(), config)
            .ProvisionTenantAsync(signup.Id, recipeId);

        var tenant = await central.Tenants.AsNoTracking().SingleAsync(t => t.Name == $"{label} {unique}");
        return new TenantDbContextFactory().CreateForTenant(tenant.DatabaseConnectionString!, tenant.Id);
    }

    [Fact]
    public async Task The_automobile_recipe_arrives_with_a_priced_starter_catalog_and_quote_template()
    {
        await using var central = Central();
        await central.Database.MigrateAsync();

        var stored = await central.IndustryRecipes.AsNoTracking().SingleAsync(r => r.Id == AutomobileRecipeId);
        var content = System.Text.Json.JsonSerializer.Deserialize<RecipeContentModel>(stored.ContentJson)!;
        Assert.NotNull(content.Catalog);

        await using var db = await ProvisionAsync(central, AutomobileRecipeId, "AutoCatalog");

        var products = await db.Products.ToListAsync();
        Assert.Equal(content.Catalog!.Products.Count, products.Count);
        Assert.Contains(products, p => p.Code == "VEH-NEW" && p.ChargeType == ChargeType.OneOff && p.Category == "Vehicles");
        Assert.Contains(products, p => p.Code == "FIN-PCP" && p.ChargeType == ChargeType.Recurring
                                       && p.BillingFrequency == BillingFrequency.Monthly && p.DefaultPeriods == 48);

        var list = await db.PriceLists.SingleAsync(l => l.IsDefault);
        Assert.Null(list.CurrencyCode);
        Assert.Equal(products.Count, await db.ProductPrices.CountAsync(p => p.PriceListId == list.Id));

        var settings = await db.QuoteSettings.SingleAsync();
        Assert.Equal("VQ-", settings.NumberPrefix);
        Assert.Equal(10m, settings.ApprovalDiscountThresholdPercent);

        // Lead custom fields are unchanged by the catalog — still six, still on leads.
        Assert.Equal(6, await db.CustomFieldDefinitions.CountAsync(f => f.AppliesTo == CustomFieldEntity.Lead));
    }

    [Fact]
    public async Task A_recipe_stored_before_catalogs_existed_still_provisions_with_an_empty_catalog()
    {
        await using var central = Central();
        await central.Database.MigrateAsync();

        // The Blank recipe was stored by an early migration and has no Catalog key at all.
        var stored = await central.IndustryRecipes.AsNoTracking().SingleAsync(r => r.Id == BlankRecipeId);
        Assert.DoesNotContain("Catalog", stored.ContentJson);

        await using var db = await ProvisionAsync(central, BlankRecipeId, "LegacyRecipe");

        Assert.Empty(await db.Products.ToListAsync());
        Assert.Empty(await db.PriceLists.ToListAsync());
        Assert.Empty(await db.QuoteSettings.ToListAsync());
        Assert.NotEmpty(await db.PipelineStages.ToListAsync());
    }
}
