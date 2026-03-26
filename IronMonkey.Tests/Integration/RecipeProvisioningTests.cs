using IronMonkey.ApiService.Authentication.Services;
using IronMonkey.Data;
using IronMonkey.Data.Entities;
using IronMonkey.Data.RecipeContent;
using IronMonkey.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;
using BC = BCrypt.Net.BCrypt;

namespace IronMonkey.Tests.Integration;

[Collection("Integration")]
public class RecipeProvisioningTests : IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture _fixture;

    public RecipeProvisioningTests(PostgreSqlFixture fixture) => _fixture = fixture;

    private CentralDbContext CreateCentralDbContext()
    {
        var options = new DbContextOptionsBuilder<CentralDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options;
        return new CentralDbContext(options);
    }

    private TenantProvisioningService CreateProvisioningService(CentralDbContext centralDb)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:CentralDb"] = _fixture.ConnectionString
            })
            .Build();
        return new TenantProvisioningService(centralDb, new TenantDbContextFactory(), config);
    }

    [Fact]
    public async Task ProvisionTenant_WithRecipe_SeedsAllPipelineStages()
    {
        await using var centralDb = CreateCentralDbContext();
        await centralDb.Database.MigrateAsync();

        // Create a recipe with 2 pipeline stages
        var content = new RecipeContentModel
        {
            PipelineStages =
            [
                new PipelineStageDefinition { Name = "New Lead", Order = 0, StageType = "Entry" },
                new PipelineStageDefinition { Name = "In Progress", Order = 1, StageType = "Active" }
            ]
        };
        var recipe = IndustryRecipe.Create("Test Industry", "Test description", "test-industry-stages", "icon-test", false, content);
        centralDb.IndustryRecipes.Add(recipe);
        await centralDb.SaveChangesAsync();

        // Create and approve a signup request
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        var signupRequest = SignupRequest.Create(
            companyName: $"Recipe Stages Co {uniqueId}",
            adminEmail: $"admin-{uniqueId}@recipestages.com",
            adminPasswordHash: BC.HashPassword("SecurePass123!"),
            phone: "555-0100",
            recipeId: null,
            companySize: "10-50",
            address: "123 Main St",
            billingContact: "billing@recipestages.com");
        centralDb.SignupRequests.Add(signupRequest);
        await centralDb.SaveChangesAsync();
        signupRequest.Approve();
        await centralDb.SaveChangesAsync();

        // Provision tenant with recipe
        var service = CreateProvisioningService(centralDb);
        await service.ProvisionTenantAsync(signupRequest.Id, recipe.Id);

        // Assert 2 stages exist in tenant DB
        var tenant = await centralDb.Tenants.AsNoTracking()
            .SingleAsync(t => t.Name == $"Recipe Stages Co {uniqueId}");
        var tenantContextFactory = new TenantDbContextFactory();
        await using var tenantDb = tenantContextFactory.CreateForTenant(tenant.DatabaseConnectionString!, tenant.Id);
        var stages = await tenantDb.PipelineStages.ToListAsync();
        Assert.Equal(2, stages.Count);
        Assert.Contains(stages, s => s.Name == "New Lead" && s.StageType == StageType.Entry);
        Assert.Contains(stages, s => s.Name == "In Progress" && s.StageType == StageType.Active);
    }

    [Fact]
    public async Task ProvisionTenant_WithRecipe_SeedsCustomFields()
    {
        await using var centralDb = CreateCentralDbContext();
        await centralDb.Database.MigrateAsync();

        // Create a recipe with 1 custom field
        var content = new RecipeContentModel
        {
            CustomFields =
            [
                new CustomFieldDefinitionDto { FieldName = "VehicleModel", FieldType = "Text", IsRequired = false }
            ]
        };
        var recipe = IndustryRecipe.Create("Auto Industry", "Auto description", "auto-fields", "icon-auto", false, content);
        centralDb.IndustryRecipes.Add(recipe);
        await centralDb.SaveChangesAsync();

        // Create and approve a signup request
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        var signupRequest = SignupRequest.Create(
            companyName: $"Custom Fields Co {uniqueId}",
            adminEmail: $"admin-{uniqueId}@customfields.com",
            adminPasswordHash: BC.HashPassword("SecurePass123!"),
            phone: "555-0101",
            recipeId: null,
            companySize: "10-50",
            address: "456 Oak Ave",
            billingContact: "billing@customfields.com");
        centralDb.SignupRequests.Add(signupRequest);
        await centralDb.SaveChangesAsync();
        signupRequest.Approve();
        await centralDb.SaveChangesAsync();

        // Provision tenant with recipe
        var service = CreateProvisioningService(centralDb);
        await service.ProvisionTenantAsync(signupRequest.Id, recipe.Id);

        // Assert 1 custom field exists in tenant DB with FieldName "VehicleModel"
        var tenant = await centralDb.Tenants.AsNoTracking()
            .SingleAsync(t => t.Name == $"Custom Fields Co {uniqueId}");
        var tenantContextFactory = new TenantDbContextFactory();
        await using var tenantDb = tenantContextFactory.CreateForTenant(tenant.DatabaseConnectionString!, tenant.Id);
        var fields = await tenantDb.CustomFieldDefinitions.ToListAsync();
        Assert.Single(fields);
        Assert.Contains(fields, f => f.FieldName == "VehicleModel" && f.FieldType == CustomFieldType.Text);
    }

    [Fact]
    public async Task ProvisionTenant_WithRecipe_SetsAppliedRecipeOnTenant()
    {
        await using var centralDb = CreateCentralDbContext();
        await centralDb.Database.MigrateAsync();

        // Create a recipe
        var content = new RecipeContentModel
        {
            PipelineStages =
            [
                new PipelineStageDefinition { Name = "Contacted", Order = 0, StageType = "Entry" }
            ]
        };
        var recipe = IndustryRecipe.Create("Recipe Track Industry", "Track description", "recipe-track", "icon-track", false, content);
        centralDb.IndustryRecipes.Add(recipe);
        await centralDb.SaveChangesAsync();

        // Create and approve a signup request
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        var signupRequest = SignupRequest.Create(
            companyName: $"Applied Recipe Co {uniqueId}",
            adminEmail: $"admin-{uniqueId}@appliedrecipe.com",
            adminPasswordHash: BC.HashPassword("SecurePass123!"),
            phone: "555-0102",
            recipeId: null,
            companySize: "50-100",
            address: "789 Pine Rd",
            billingContact: "billing@appliedrecipe.com");
        centralDb.SignupRequests.Add(signupRequest);
        await centralDb.SaveChangesAsync();
        signupRequest.Approve();
        await centralDb.SaveChangesAsync();

        // Provision tenant with recipe
        var service = CreateProvisioningService(centralDb);
        await service.ProvisionTenantAsync(signupRequest.Id, recipe.Id);

        // Assert AppliedRecipeId and AppliedRecipeVersion are set on the tenant
        var tenant = await centralDb.Tenants.AsNoTracking()
            .SingleAsync(t => t.Name == $"Applied Recipe Co {uniqueId}");
        Assert.Equal(recipe.Id, tenant.AppliedRecipeId);
        Assert.Equal(1, tenant.AppliedRecipeVersion);
    }
}
