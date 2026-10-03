using IronMonkey.ApiService.Authentication.Services;
using IronMonkey.Data;
using IronMonkey.Data.Entities;
using IronMonkey.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;
using BC = BCrypt.Net.BCrypt;

namespace IronMonkey.Tests.Integration;

[Collection("Integration")]
public class BlankRecipeProvisioningTests : IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture _fixture;

    // Fixed GUID for Blank recipe — seeded by Plan 02 migration
    private static readonly Guid BlankRecipeId = new Guid("00000000-0000-0000-0000-000000000001");

    public BlankRecipeProvisioningTests(PostgreSqlFixture fixture) => _fixture = fixture;

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
    public async Task ProvisionTenant_WithBlankRecipe_CreatesOneEntryStage()
    {
        await using var centralDb = CreateCentralDbContext();
        await centralDb.Database.MigrateAsync();

        // Create and approve a signup request
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        var signupRequest = SignupRequest.Create(
            companyName: $"Blank Recipe Co {uniqueId}",
            adminEmail: $"admin-{uniqueId}@blankrecipe.com",
            adminPasswordHash: BC.HashPassword("SecurePass123!"),
            phone: "555-0200",
            recipeId: null,
            companySize: "1-10",
            address: "100 Blank St",
            billingContact: "billing@blankrecipe.com");
        centralDb.SignupRequests.Add(signupRequest);
        await centralDb.SaveChangesAsync();
        signupRequest.Approve();
        await centralDb.SaveChangesAsync();

        // Provision tenant with the blank recipe ID
        var service = CreateProvisioningService(centralDb);
        await service.ProvisionTenantAsync(signupRequest.Id, BlankRecipeId);

        // Assert exactly 1 pipeline stage exists and it is "New" with StageType.Entry
        var tenant = await centralDb.Tenants.AsNoTracking()
            .SingleAsync(t => t.Name == $"Blank Recipe Co {uniqueId}");
        var tenantContextFactory = new TenantDbContextFactory();
        await using var tenantDb = tenantContextFactory.CreateForTenant(tenant.DatabaseConnectionString!, tenant.Id);
        // Lead stages only. Provisioning also seeds the tenant's opportunity stages into
        // this same table now, and this assertion is about what the recipe defined for leads.
        var stages = await tenantDb.PipelineStages
            .Where(s => s.RecordType == PipelineRecordType.Lead)
            .ToListAsync();
        Assert.Single(stages);
        var stage = stages[0];
        Assert.Equal("New", stage.Name);
        Assert.Equal(StageType.Entry, stage.StageType);

        // The Blank recipe predates opportunity stages and defines none, so the tenant falls
        // back to the product defaults rather than being left unable to create a deal. This
        // is the backward-compatibility guarantee, asserted against a real provision.
        var opportunityStages = await tenantDb.PipelineStages
            .Where(s => s.RecordType == PipelineRecordType.Opportunity)
            .ToListAsync();

        Assert.NotEmpty(opportunityStages);
        Assert.Contains(opportunityStages, s => s.StageType == StageType.ClosedWon);
        Assert.Contains(opportunityStages, s => s.StageType == StageType.ClosedLost);
    }

    [Fact]
    public async Task ProvisionTenant_WithNullRecipeId_StillProvisions()
    {
        await using var centralDb = CreateCentralDbContext();
        await centralDb.Database.MigrateAsync();

        // Create and approve a signup request
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        var signupRequest = SignupRequest.Create(
            companyName: $"No Recipe Co {uniqueId}",
            adminEmail: $"admin-{uniqueId}@norecipe.com",
            adminPasswordHash: BC.HashPassword("SecurePass123!"),
            phone: "555-0201",
            recipeId: null,
            companySize: "10-50",
            address: "200 Null Ave",
            billingContact: "billing@norecipe.com");
        centralDb.SignupRequests.Add(signupRequest);
        await centralDb.SaveChangesAsync();
        signupRequest.Approve();
        await centralDb.SaveChangesAsync();

        // Provision tenant with no recipeId (backward compat — default null)
        var service = CreateProvisioningService(centralDb);
        await service.ProvisionTenantAsync(signupRequest.Id);

        // Assert tenant is provisioned successfully (IsProvisioned = true)
        var tenant = await centralDb.Tenants.AsNoTracking()
            .SingleAsync(t => t.Name == $"No Recipe Co {uniqueId}");
        Assert.True(tenant.IsProvisioned);
    }
}
