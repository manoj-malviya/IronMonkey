using IronMonkey.ApiService.Authentication.Endpoints;
using IronMonkey.Data;
using IronMonkey.Data.Entities;
using IronMonkey.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace IronMonkey.Tests.Integration.TenantManagement;

[Collection("Integration")]
public class PlatformStatsEndpointTests : IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture _fixture;

    public PlatformStatsEndpointTests(PostgreSqlFixture fixture)
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

    private static SignupRequest CreateSignup(string company) =>
        SignupRequest.Create(
            companyName: company,
            adminEmail: $"admin-{Guid.NewGuid():N}@example.test",
            adminPasswordHash: "hash",
            phone: "+15550100",
            recipeId: null,
            companySize: "10-50",
            address: "1 Test Way",
            billingContact: "billing@example.test");

    // Counts are global to the central DB and the fixture is shared, so every assertion
    // is a delta against a baseline taken in the same test rather than an absolute value.
    [Fact]
    public async Task Stats_CountsProvisionedTenantsSeparatelyFromTotal()
    {
        await using var centralDb = CreateCentralDbContext();
        await centralDb.Database.MigrateAsync();

        var before = await PlatformStatsEndpoint.HandleForTest(centralDb, CancellationToken.None);

        var provisioned = Tenant.Create("Stats Provisioned", $"sp-{Guid.NewGuid():N}", "Standard", "Active");
        provisioned.MarkProvisioned("Host=localhost;Database=stats_test");

        var unprovisioned = Tenant.Create("Stats Pending", $"su-{Guid.NewGuid():N}", "Standard", "Active");

        centralDb.Tenants.AddRange(provisioned, unprovisioned);
        await centralDb.SaveChangesAsync();

        var after = await PlatformStatsEndpoint.HandleForTest(centralDb, CancellationToken.None);

        Assert.Equal(before.TotalTenants + 2, after.TotalTenants);
        Assert.Equal(before.ProvisionedTenants + 1, after.ProvisionedTenants);
    }

    [Fact]
    public async Task Stats_MovesSignupFromPendingToApprovedOnApproval()
    {
        await using var centralDb = CreateCentralDbContext();
        await centralDb.Database.MigrateAsync();

        var before = await PlatformStatsEndpoint.HandleForTest(centralDb, CancellationToken.None);

        var signup = CreateSignup("Stats Signup Co");
        centralDb.SignupRequests.Add(signup);
        await centralDb.SaveChangesAsync();

        var afterCreate = await PlatformStatsEndpoint.HandleForTest(centralDb, CancellationToken.None);
        Assert.Equal(before.PendingSignups + 1, afterCreate.PendingSignups);

        // Approving only flips the request's status — provisioning is a separate step —
        // so the request should leave the pending bucket and land in the approved one.
        signup.Approve("approved by test");
        await centralDb.SaveChangesAsync();

        var afterApprove = await PlatformStatsEndpoint.HandleForTest(centralDb, CancellationToken.None);
        Assert.Equal(before.PendingSignups, afterApprove.PendingSignups);
        Assert.Equal(afterCreate.ApprovedAwaitingProvision + 1, afterApprove.ApprovedAwaitingProvision);
    }
}
