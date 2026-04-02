using IronMonkey.ApiService.Authentication.Services;
using IronMonkey.Data;
using IronMonkey.Data.Entities;
using IronMonkey.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;
using BC = BCrypt.Net.BCrypt;

namespace IronMonkey.Tests.Integration;

// TNCY-01: Signup request collected -> platform admin approves -> provision creates isolated database
[Collection("Integration")]
public class TenantProvisioningTests : IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture _fixture;

    public TenantProvisioningTests(PostgreSqlFixture fixture)
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

    private TenantProvisioningService CreateProvisioningService(CentralDbContext centralDb)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:CentralDb"] = _fixture.ConnectionString
            })
            .Build();

        var tenantContextFactory = new TenantDbContextFactory();
        return new TenantProvisioningService(centralDb, tenantContextFactory, config);
    }

    [Fact]
    public async Task SubmitSignupRequest_WhenValidData_PersistsToDatabase()
    {
        // Arrange: setup central DB schema
        await using var centralDb = CreateCentralDbContext();
        await centralDb.Database.MigrateAsync();

        var email = $"admin-{Guid.NewGuid():N}@testcompany.com";
        var signupRequest = SignupRequest.Create(
            companyName: "Test Company Alpha",
            adminEmail: email,
            adminPasswordHash: BC.HashPassword("SecurePass123!"),
            phone: "555-0100",
            recipeId: null,
            companySize: "50-100",
            address: "123 Main St",
            billingContact: "billing@testcompany.com");

        // Act: persist signup request directly (mimics POST /auth/signup)
        centralDb.SignupRequests.Add(signupRequest);
        await centralDb.SaveChangesAsync();

        // Assert: SignupRequest exists in central DB with status Pending
        var found = await centralDb.SignupRequests
            .AsNoTracking()
            .SingleOrDefaultAsync(r => r.AdminEmail == email);

        Assert.NotNull(found);
        Assert.Equal("Pending", found.Status);
        Assert.Equal("Test Company Alpha", found.CompanyName);
    }

    [Fact]
    public async Task ApproveTenant_WhenPending_UpdatesStatusToApproved()
    {
        // Arrange: create central DB and insert Pending signup request
        await using var centralDb = CreateCentralDbContext();
        await centralDb.Database.MigrateAsync();

        var email = $"admin-{Guid.NewGuid():N}@approvetest.com";
        var signupRequest = SignupRequest.Create(
            companyName: "Approval Test Co",
            adminEmail: email,
            adminPasswordHash: BC.HashPassword("SecurePass123!"),
            phone: "555-0200",
            recipeId: null,
            companySize: "10-50",
            address: "456 Oak Ave",
            billingContact: "billing@approvetest.com");

        centralDb.SignupRequests.Add(signupRequest);
        await centralDb.SaveChangesAsync();

        // Act: approve the signup request (mimics POST /admin/signup/{id}/approve)
        signupRequest.Approve("Looks good!");
        await centralDb.SaveChangesAsync();

        // Assert: status is Approved in DB
        var found = await centralDb.SignupRequests
            .AsNoTracking()
            .SingleOrDefaultAsync(r => r.Id == signupRequest.Id);

        Assert.NotNull(found);
        Assert.Equal("Approved", found.Status);
    }

    [Fact]
    public async Task ProvisionTenant_WhenApproved_CreatesSeparateDatabase()
    {
        // Arrange: approved signup request in central DB
        await using var centralDb = CreateCentralDbContext();
        await centralDb.Database.MigrateAsync();

        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        var uniqueSlug = $"provision-{uniqueId}";
        var email = $"admin-{Guid.NewGuid():N}@{uniqueSlug}.com";
        var companyName = $"Provision Co {uniqueId}";

        var signupRequest = SignupRequest.Create(
            companyName: companyName,
            adminEmail: email,
            adminPasswordHash: BC.HashPassword("SecurePass123!"),
            phone: "555-0300",
            recipeId: null,
            companySize: "100+",
            address: "789 Pine Rd",
            billingContact: "billing@provision.com");

        centralDb.SignupRequests.Add(signupRequest);
        await centralDb.SaveChangesAsync();

        signupRequest.Approve();
        await centralDb.SaveChangesAsync();

        // Act: provision the tenant (mimics POST /admin/tenants/{id}/provision)
        var service = CreateProvisioningService(centralDb);
        await service.ProvisionTenantAsync(signupRequest.Id);

        // Assert: Tenant record exists with IsProvisioned = true and DatabaseConnectionString set
        var tenant = await centralDb.Tenants
            .AsNoTracking()
            .SingleOrDefaultAsync(t => t.Name == companyName);

        Assert.NotNull(tenant);
        Assert.True(tenant.IsProvisioned);
        Assert.NotNull(tenant.DatabaseConnectionString);
        Assert.NotEmpty(tenant.DatabaseConnectionString);
    }

    [Fact]
    public async Task ProvisionTenant_WhenApproved_SeedsDefaultData()
    {
        // Arrange: approved signup request
        await using var centralDb = CreateCentralDbContext();
        await centralDb.Database.MigrateAsync();

        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        var companyName = $"Seed Test Co {uniqueId}";
        var email = $"admin-{uniqueId}@seedtest.com";

        var signupRequest = SignupRequest.Create(
            companyName: companyName,
            adminEmail: email,
            adminPasswordHash: BC.HashPassword("SecurePass123!"),
            phone: "555-0400",
            recipeId: null,
            companySize: "50-100",
            address: "321 Elm St",
            billingContact: "billing@seedtest.com");

        centralDb.SignupRequests.Add(signupRequest);
        await centralDb.SaveChangesAsync();

        signupRequest.Approve();
        await centralDb.SaveChangesAsync();

        // Act: provision the tenant
        var service = CreateProvisioningService(centralDb);
        await service.ProvisionTenantAsync(signupRequest.Id);

        // Assert: tenant DB is seeded with admin user
        var tenant = await centralDb.Tenants
            .AsNoTracking()
            .SingleOrDefaultAsync(t => t.Name == companyName);

        Assert.NotNull(tenant);
        Assert.NotNull(tenant.DatabaseConnectionString);

        var tenantContextFactory = new TenantDbContextFactory();
        await using var tenantDb = tenantContextFactory.CreateForTenant(
            tenant.DatabaseConnectionString!, tenant.Id);

        var users = await tenantDb.Users.ToListAsync();
        Assert.True(users.Count >= 1, "Expected at least one user seeded in the tenant DB.");
        Assert.Contains(users, u => u.Email == email);

        var roles = await tenantDb.Roles.ToListAsync();
        Assert.True(roles.Count >= 1, "Expected at least one role seeded in the tenant DB.");
        Assert.Contains(roles, r => r.Name == "Admin");
    }
}
