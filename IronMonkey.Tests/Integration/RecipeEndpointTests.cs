using IronMonkey.ApiService.Authentication.Services;
using IronMonkey.ApiService.Features.Recipes;
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
public class RecipeEndpointTests : IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture _fixture;

    public RecipeEndpointTests(PostgreSqlFixture fixture) => _fixture = fixture;

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

    private static string UniqueSlug(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..40];

    private static SignupRequest CreateApprovedSignupRequest(string uniqueEmail, Guid? recipeId = null)
    {
        var sr = SignupRequest.Create(
            companyName: $"Test Co {Guid.NewGuid():N}"[..30],
            adminEmail: uniqueEmail,
            adminPasswordHash: BC.HashPassword("Test1234!"),
            phone: "555-0100",
            recipeId: recipeId,
            companySize: "1-10",
            address: "123 Test St",
            billingContact: "billing@test.com");
        sr.Approve();
        return sr;
    }

    // Test 1 (ONBD-05): ListRecipes_ReturnsOnlyActiveRecipes
    [Fact]
    public async Task ListRecipes_ReturnsOnlyActiveRecipes()
    {
        await using var centralDb = CreateCentralDbContext();
        await centralDb.Database.MigrateAsync();

        var slug1 = UniqueSlug("list-active-a");
        var slug2 = UniqueSlug("list-active-b");
        var slugInactive = UniqueSlug("list-inactive");

        var content = new RecipeContentModel
        {
            PipelineStages = [new PipelineStageDefinition { Name = "New", Order = 0, StageType = "Entry" }]
        };

        var active1 = IndustryRecipe.Create("Active Recipe 1", "Desc 1", slug1, "icon-1", false, content);
        var active2 = IndustryRecipe.Create("Active Recipe 2", "Desc 2", slug2, "icon-2", false, content);
        var inactive = IndustryRecipe.Create("Inactive Recipe", "Desc 3", slugInactive, "icon-3", false, content);
        inactive.Deactivate();

        centralDb.IndustryRecipes.AddRange(active1, active2, inactive);
        await centralDb.SaveChangesAsync();

        // Act: mirrors GET /api/recipes behavior
        var activeRecipes = await centralDb.IndustryRecipes
            .AsNoTracking()
            .Where(r => r.IsActive && (r.IndustrySlug == slug1 || r.IndustrySlug == slug2 || r.IndustrySlug == slugInactive))
            .ToListAsync();

        // Assert
        Assert.Equal(2, activeRecipes.Count);
        Assert.DoesNotContain(activeRecipes, r => r.IndustrySlug == slugInactive);
        Assert.Contains(activeRecipes, r => r.IndustrySlug == slug1);
        Assert.Contains(activeRecipes, r => r.IndustrySlug == slug2);
    }

    // Test 2 (ONBD-02): PreviewRecipe_ReturnsFullContent_WhenRecipeIsActive
    [Fact]
    public async Task PreviewRecipe_ReturnsFullContent_WhenRecipeIsActive()
    {
        await using var centralDb = CreateCentralDbContext();
        await centralDb.Database.MigrateAsync();

        var slug = UniqueSlug("preview-active");
        var content = new RecipeContentModel
        {
            PipelineStages =
            [
                new PipelineStageDefinition { Name = "New Lead", Order = 0, StageType = "Entry" },
                new PipelineStageDefinition { Name = "In Progress", Order = 1, StageType = "Active" }
            ],
            CustomFields =
            [
                new CustomFieldDefinitionDto { FieldName = "VehicleModel", FieldType = "Text", IsRequired = false }
            ]
        };
        var recipe = IndustryRecipe.Create("Preview Industry", "Preview desc", slug, "icon-preview", false, content);
        centralDb.IndustryRecipes.Add(recipe);
        await centralDb.SaveChangesAsync();

        // Act: mirrors GET /api/recipes/{id} behavior
        var found = await centralDb.IndustryRecipes
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == recipe.Id && r.IsActive);

        // Assert
        Assert.NotNull(found);
        var deserializedContent = System.Text.Json.JsonSerializer.Deserialize<RecipeContentModel>(found.ContentJson);
        Assert.NotNull(deserializedContent);
        Assert.Equal(2, deserializedContent.PipelineStages.Count);
        Assert.Single(deserializedContent.CustomFields);
    }

    // Test 3 (ONBD-02): PreviewRecipe_ReturnsNull_WhenRecipeIsDeactivated
    [Fact]
    public async Task PreviewRecipe_ReturnsNull_WhenRecipeIsDeactivated()
    {
        await using var centralDb = CreateCentralDbContext();
        await centralDb.Database.MigrateAsync();

        var slug = UniqueSlug("preview-inactive");
        var content = new RecipeContentModel
        {
            PipelineStages = [new PipelineStageDefinition { Name = "New", Order = 0, StageType = "Entry" }]
        };
        var recipe = IndustryRecipe.Create("Deactivated Recipe", "Desc", slug, "icon", false, content);
        centralDb.IndustryRecipes.Add(recipe);
        await centralDb.SaveChangesAsync();
        recipe.Deactivate();
        await centralDb.SaveChangesAsync();

        // Act: deactivated recipes excluded from preview
        var found = await centralDb.IndustryRecipes
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == recipe.Id && r.IsActive);

        // Assert
        Assert.Null(found);
    }

    // Test 4 (RADM-01): CreateRecipe_PersistsToDatabase
    [Fact]
    public async Task CreateRecipe_PersistsToDatabase()
    {
        await using var centralDb = CreateCentralDbContext();
        await centralDb.Database.MigrateAsync();

        var slug = UniqueSlug("create-recipe");
        var content = new RecipeContentModel
        {
            PipelineStages = [new PipelineStageDefinition { Name = "New Lead", Order = 0, StageType = "Entry" }]
        };

        // Act: mirrors POST /api/recipes
        var recipe = IndustryRecipe.Create("Test Recipe", "Test description", slug, "icon-test", false, content);
        centralDb.IndustryRecipes.Add(recipe);
        await centralDb.SaveChangesAsync();

        // Assert
        var found = await centralDb.IndustryRecipes.AsNoTracking().FirstOrDefaultAsync(r => r.Id == recipe.Id);
        Assert.NotNull(found);
        Assert.Equal("Test Recipe", found.Name);
        Assert.True(found.IsActive);
        Assert.Equal(1, found.Version);
    }

    // Test 5 (RADM-01): CreateRecipe_WithDuplicateSlug_ShouldBeDetectable
    [Fact]
    public async Task CreateRecipe_WithDuplicateSlug_ShouldBeDetectable()
    {
        await using var centralDb = CreateCentralDbContext();
        await centralDb.Database.MigrateAsync();

        var slug = UniqueSlug("duplicate-slug");
        var content = new RecipeContentModel
        {
            PipelineStages = [new PipelineStageDefinition { Name = "New", Order = 0, StageType = "Entry" }]
        };

        var recipe = IndustryRecipe.Create("Original Recipe", "Desc", slug, "icon", false, content);
        centralDb.IndustryRecipes.Add(recipe);
        await centralDb.SaveChangesAsync();

        // Act: check if another active recipe with same slug exists (endpoint would return ValidationError)
        var slugExists = await centralDb.IndustryRecipes
            .AnyAsync(r => r.IndustrySlug == slug && r.IsActive);

        // Assert
        Assert.True(slugExists);
    }

    // Test 6 (RADM-02): UpdateRecipe_IncrementsVersion
    [Fact]
    public async Task UpdateRecipe_IncrementsVersion()
    {
        await using var centralDb = CreateCentralDbContext();
        await centralDb.Database.MigrateAsync();

        var slug = UniqueSlug("update-recipe");
        var content1 = new RecipeContentModel
        {
            PipelineStages = [new PipelineStageDefinition { Name = "New", Order = 0, StageType = "Entry" }]
        };
        var recipe = IndustryRecipe.Create("Update Recipe", "Desc", slug, "icon", false, content1);
        centralDb.IndustryRecipes.Add(recipe);
        await centralDb.SaveChangesAsync();
        Assert.Equal(1, recipe.Version);

        // Act: mirrors PUT /api/recipes/{id}
        var content2 = new RecipeContentModel
        {
            PipelineStages =
            [
                new PipelineStageDefinition { Name = "Lead", Order = 0, StageType = "Entry" },
                new PipelineStageDefinition { Name = "Qualified", Order = 1, StageType = "Active" },
                new PipelineStageDefinition { Name = "Won", Order = 2, StageType = "ClosedWon" }
            ]
        };
        recipe.UpdateContent(content2);
        await centralDb.SaveChangesAsync();

        // Assert
        Assert.Equal(2, recipe.Version);
        var deserializedContent = System.Text.Json.JsonSerializer.Deserialize<RecipeContentModel>(recipe.ContentJson);
        Assert.NotNull(deserializedContent);
        Assert.Equal(3, deserializedContent.PipelineStages.Count);
    }

    // Test 7 (RADM-03): DeactivateRecipe_SetsIsActiveFalse
    [Fact]
    public async Task DeactivateRecipe_SetsIsActiveFalse()
    {
        await using var centralDb = CreateCentralDbContext();
        await centralDb.Database.MigrateAsync();

        var slug = UniqueSlug("deactivate-recipe");
        var content = new RecipeContentModel
        {
            PipelineStages = [new PipelineStageDefinition { Name = "New", Order = 0, StageType = "Entry" }]
        };
        var recipe = IndustryRecipe.Create("Deactivate Me", "Desc", slug, "icon", false, content);
        centralDb.IndustryRecipes.Add(recipe);
        await centralDb.SaveChangesAsync();
        Assert.True(recipe.IsActive);

        // Act: mirrors DELETE /api/recipes/{id}
        recipe.Deactivate();
        await centralDb.SaveChangesAsync();

        // Assert: reload from DB to confirm persisted
        var reloaded = await centralDb.IndustryRecipes.AsNoTracking().FirstAsync(r => r.Id == recipe.Id);
        Assert.False(reloaded.IsActive);
    }

    // Test 8 (RADM-03): DeactivatedRecipe_NotReturnedInActiveList
    [Fact]
    public async Task DeactivatedRecipe_NotReturnedInActiveList()
    {
        await using var centralDb = CreateCentralDbContext();
        await centralDb.Database.MigrateAsync();

        var slug = UniqueSlug("deact-list-check");
        var content = new RecipeContentModel
        {
            PipelineStages = [new PipelineStageDefinition { Name = "New", Order = 0, StageType = "Entry" }]
        };
        var recipe = IndustryRecipe.Create("Will Be Deactivated", "Desc", slug, "icon", false, content);
        centralDb.IndustryRecipes.Add(recipe);
        await centralDb.SaveChangesAsync();

        recipe.Deactivate();
        await centralDb.SaveChangesAsync();

        // Act: query active recipes
        var activeRecipes = await centralDb.IndustryRecipes
            .AsNoTracking()
            .Where(r => r.IsActive && r.IndustrySlug == slug)
            .ToListAsync();

        // Assert
        Assert.Empty(activeRecipes);
    }

    // Test 9 (ONBD-03 + D-12): ProvisionTenant_WithDeactivatedRecipe_ThrowsInvalidOperationException
    [Fact]
    public async Task ProvisionTenant_WithDeactivatedRecipe_ThrowsInvalidOperationException()
    {
        await using var centralDb = CreateCentralDbContext();
        await centralDb.Database.MigrateAsync();

        // Arrange: create and deactivate a recipe
        var slug = UniqueSlug("deact-provision");
        var content = new RecipeContentModel
        {
            PipelineStages = [new PipelineStageDefinition { Name = "New", Order = 0, StageType = "Entry" }]
        };
        var recipe = IndustryRecipe.Create("Deactivated For Provision", "Desc", slug, "icon", false, content);
        centralDb.IndustryRecipes.Add(recipe);
        await centralDb.SaveChangesAsync();
        recipe.Deactivate();
        await centralDb.SaveChangesAsync();

        // Create and approve a signup request with that recipeId
        var email = $"test-{Guid.NewGuid():N}@example.com";
        var signupRequest = CreateApprovedSignupRequest(email, recipe.Id);
        centralDb.SignupRequests.Add(signupRequest);
        await centralDb.SaveChangesAsync();

        var svc = CreateProvisioningService(centralDb);

        // Act + Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.ProvisionTenantAsync(signupRequest.Id, recipe.Id, CancellationToken.None));

        Assert.Contains("deactivated", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // Test 10 (ONBD-01 + ONBD-03): SignupRequest_WithRecipeId_UsedDuringProvisioning
    [Fact]
    public async Task SignupRequest_WithRecipeId_UsedDuringProvisioning()
    {
        await using var centralDb = CreateCentralDbContext();
        await centralDb.Database.MigrateAsync();

        // Arrange: create active recipe with 2 stages
        var slug = UniqueSlug("recipe-provision");
        var content = new RecipeContentModel
        {
            PipelineStages =
            [
                new PipelineStageDefinition { Name = "New Lead", Order = 0, StageType = "Entry" },
                new PipelineStageDefinition { Name = "In Progress", Order = 1, StageType = "Active" }
            ]
        };
        var recipe = IndustryRecipe.Create("Provision Recipe Industry", "Desc", slug, "icon", false, content);
        centralDb.IndustryRecipes.Add(recipe);
        await centralDb.SaveChangesAsync();

        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        var companyName = $"Recipe Prov Co {uniqueId}";
        var email = $"test-{uniqueId}@example.com";
        var signupRequest = SignupRequest.Create(
            companyName: companyName,
            adminEmail: email,
            adminPasswordHash: BC.HashPassword("Test1234!"),
            phone: "555-0100",
            recipeId: recipe.Id,
            companySize: "1-10",
            address: "123 Test St",
            billingContact: "billing@test.com");
        signupRequest.Approve();
        centralDb.SignupRequests.Add(signupRequest);
        await centralDb.SaveChangesAsync();

        var svc = CreateProvisioningService(centralDb);

        // Act
        await svc.ProvisionTenantAsync(signupRequest.Id, recipe.Id, CancellationToken.None);

        // Assert: provisioned tenant DB has 2 pipeline stages
        var tenant = await centralDb.Tenants.AsNoTracking()
            .SingleAsync(t => t.Name == companyName);
        var tenantContextFactory = new TenantDbContextFactory();
        await using var tenantDb = tenantContextFactory.CreateForTenant(tenant.DatabaseConnectionString!, tenant.Id);
        var stages = await tenantDb.PipelineStages.ToListAsync();
        Assert.Equal(2, stages.Count);
    }

    // Test 11 (ONBD-01): SignupRequest_WithoutRecipeId_DefaultsToBlankRecipe
    [Fact]
    public async Task SignupRequest_WithoutRecipeId_DefaultsToBlankRecipe()
    {
        await using var centralDb = CreateCentralDbContext();
        await centralDb.Database.MigrateAsync();

        // Arrange: create a SignupRequest with RecipeId = null; approve it
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        var companyName = $"Blank Default Co {uniqueId}";
        var email = $"test-{uniqueId}@blank.com";
        var signupRequest = SignupRequest.Create(
            companyName: companyName,
            adminEmail: email,
            adminPasswordHash: BC.HashPassword("Test1234!"),
            phone: "555-0100",
            recipeId: null,
            companySize: "1-10",
            address: "123 Test St",
            billingContact: "billing@test.com");
        signupRequest.Approve();
        centralDb.SignupRequests.Add(signupRequest);
        await centralDb.SaveChangesAsync();

        var svc = CreateProvisioningService(centralDb);

        // Act
        await svc.ProvisionTenantAsync(signupRequest.Id, null, CancellationToken.None);

        // Assert: tenant DB has at least 1 pipeline stage (Blank recipe default stage seeded)
        var tenant = await centralDb.Tenants.AsNoTracking()
            .SingleAsync(t => t.Name == companyName);
        var tenantContextFactory = new TenantDbContextFactory();
        await using var tenantDb = tenantContextFactory.CreateForTenant(tenant.DatabaseConnectionString!, tenant.Id);
        var stages = await tenantDb.PipelineStages.ToListAsync();
        Assert.NotEmpty(stages);
    }

    // Test 12 (D-14): RecipeContentValidator_RejectsInvalidStageType
    [Fact]
    public void RecipeContentValidator_RejectsInvalidStageType()
    {
        var content = new RecipeContentModel
        {
            PipelineStages =
            [
                new PipelineStageDefinition { Name = "Bad Stage", Order = 0, StageType = "InvalidType" }
            ]
        };

        var validator = new RecipeContentValidator();
        var result = validator.Validate(content);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName.Contains("StageType"));
    }

    // Test 13 (D-14): RecipeContentValidator_RejectsInvalidFieldType
    [Fact]
    public void RecipeContentValidator_RejectsInvalidFieldType()
    {
        var content = new RecipeContentModel
        {
            PipelineStages =
            [
                new PipelineStageDefinition { Name = "New", Order = 0, StageType = "Entry" }
            ],
            CustomFields =
            [
                new CustomFieldDefinitionDto { FieldName = "SomeField", FieldType = "BadType", IsRequired = false }
            ]
        };

        var validator = new RecipeContentValidator();
        var result = validator.Validate(content);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName.Contains("FieldType"));
    }

    // Test 14 (D-14): RecipeContentValidator_AcceptsValidRecipe
    [Fact]
    public void RecipeContentValidator_AcceptsValidRecipe()
    {
        var content = new RecipeContentModel
        {
            PipelineStages =
            [
                new PipelineStageDefinition { Name = "New Lead", Order = 0, StageType = "Entry" }
            ],
            CustomFields =
            [
                new CustomFieldDefinitionDto { FieldName = "Notes", FieldType = "Text", IsRequired = false }
            ]
        };

        var validator = new RecipeContentValidator();
        var result = validator.Validate(content);

        Assert.True(result.IsValid);
    }
}
