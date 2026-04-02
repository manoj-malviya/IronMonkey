using IronMonkey.Data;
using IronMonkey.Data.Entities;
using IronMonkey.Data.RecipeContent;
using IronMonkey.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace IronMonkey.Tests.Integration;

// RCPE-01: IndustryRecipe entity can be created and persisted to the central database
// RCPE-02: IndustryRecipe metadata fields (Name, Description, IconIdentifier, IndustrySlug) are stored correctly
// RCPE-04: IndustryRecipe version starts at 1
[Collection("Integration")]
public class IndustryRecipeEntityTests : IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture _fixture;

    public IndustryRecipeEntityTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    private CentralDbContext CreateCentralDbContext()
    {
        var options = new DbContextOptionsBuilder<CentralDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options;
        return new CentralDbContext(options);
    }

    [Fact]
    public async Task IndustryRecipe_Create_PersistsToDatabase()
    {
        await using var centralDb = CreateCentralDbContext();
        await centralDb.Database.MigrateAsync();

        var content = new RecipeContentModel();
        var recipe = IndustryRecipe.Create("Retail", "Retail CRM", "retail", "icon-retail", false, content);
        centralDb.IndustryRecipes.Add(recipe);
        await centralDb.SaveChangesAsync();

        var found = await centralDb.IndustryRecipes.AsNoTracking().SingleOrDefaultAsync(r => r.Id == recipe.Id);
        Assert.NotNull(found);
        Assert.Equal("Retail", found.Name);
        Assert.Equal("retail", found.IndustrySlug);
    }

    [Fact]
    public async Task IndustryRecipe_ContentJson_RoundTrips()
    {
        await using var centralDb = CreateCentralDbContext();
        await centralDb.Database.MigrateAsync();

        var content = new RecipeContentModel
        {
            PipelineStages = [new PipelineStageDefinition { Name = "Lead In", Order = 0, StageType = "Entry" }]
        };
        var recipe = IndustryRecipe.Create("Test Industry", "Test Desc", "test-industry", "icon-test", false, content);
        centralDb.IndustryRecipes.Add(recipe);
        await centralDb.SaveChangesAsync();

        var found = await centralDb.IndustryRecipes.AsNoTracking().SingleOrDefaultAsync(r => r.Id == recipe.Id);
        Assert.NotNull(found);
        var parsed = System.Text.Json.JsonSerializer.Deserialize<RecipeContentModel>(found.ContentJson);
        Assert.NotNull(parsed);
        Assert.Single(parsed.PipelineStages);
        Assert.Equal("Lead In", parsed.PipelineStages[0].Name);
    }

    [Fact]
    public async Task IndustryRecipe_Metadata_AllFieldsPersist()
    {
        await using var centralDb = CreateCentralDbContext();
        await centralDb.Database.MigrateAsync();

        var content = new RecipeContentModel();
        var recipe = IndustryRecipe.Create("Test", "Desc", "test-slug", "icon-test", false, content);
        centralDb.IndustryRecipes.Add(recipe);
        await centralDb.SaveChangesAsync();

        var found = await centralDb.IndustryRecipes.AsNoTracking().SingleOrDefaultAsync(r => r.Id == recipe.Id);
        Assert.NotNull(found);
        Assert.Equal("Test", found.Name);
        Assert.Equal("Desc", found.Description);
        Assert.Equal("icon-test", found.IconIdentifier);
        Assert.Equal("test-slug", found.IndustrySlug);
    }

    [Fact]
    public async Task IndustryRecipe_Version_StartsAtOne()
    {
        await using var centralDb = CreateCentralDbContext();
        await centralDb.Database.MigrateAsync();

        var content = new RecipeContentModel();
        var recipe = IndustryRecipe.Create("Version Test", "Desc", "version-test", "icon-v", false, content);
        centralDb.IndustryRecipes.Add(recipe);
        await centralDb.SaveChangesAsync();

        var found = await centralDb.IndustryRecipes.AsNoTracking().SingleOrDefaultAsync(r => r.Id == recipe.Id);
        Assert.NotNull(found);
        Assert.Equal(1, found.Version);
    }
}
