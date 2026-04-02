using IronMonkey.ApiService.Authentication.Endpoints;
using IronMonkey.Data;
using IronMonkey.Data.Entities;
using IronMonkey.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Xunit;
using BC = BCrypt.Net.BCrypt;

namespace IronMonkey.Tests.Integration.TenantManagement;

[Collection("Integration")]
public class ApproveAndProvisionTests : IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture _fixture;

    public ApproveAndProvisionTests(PostgreSqlFixture fixture)
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
    public async Task ApproveTenant_WhenPending_ChangesStatusToApproved()
    {
        await using var centralDb = CreateCentralDbContext();
        await centralDb.Database.MigrateAsync();

        var signup = SignupRequest.Create(
            companyName: "Approve Co",
            adminEmail: $"admin-{Guid.NewGuid():N}@approveco.com",
            adminPasswordHash: BC.HashPassword("Pass123!"),
            phone: "555-0200",
            recipeId: null,
            companySize: "10-50",
            address: "1 Main St",
            billingContact: "billing@approveco.com");
        centralDb.SignupRequests.Add(signup);
        await centralDb.SaveChangesAsync();

        var request = new ApproveTenantEndpoint.Request(ApprovalNote: null);
        await ApproveTenantEndpoint.Handle(signup.Id, request, centralDb, CancellationToken.None);

        var refreshed = await centralDb.SignupRequests.FindAsync(signup.Id);
        Assert.Equal("Approved", refreshed!.Status);
    }

    [Fact]
    public async Task ApproveTenant_WithNote_PersistsNote()
    {
        await using var centralDb = CreateCentralDbContext();
        await centralDb.Database.MigrateAsync();

        var signup = SignupRequest.Create(
            companyName: "Note Co",
            adminEmail: $"admin-{Guid.NewGuid():N}@noteco.com",
            adminPasswordHash: BC.HashPassword("Pass123!"),
            phone: "555-0201",
            recipeId: null,
            companySize: "1-10",
            address: "2 Note Ave",
            billingContact: "b@noteco.com");
        centralDb.SignupRequests.Add(signup);
        await centralDb.SaveChangesAsync();

        var request = new ApproveTenantEndpoint.Request(ApprovalNote: "looks good");
        await ApproveTenantEndpoint.Handle(signup.Id, request, centralDb, CancellationToken.None);

        var refreshed = await centralDb.SignupRequests.FindAsync(signup.Id);
        Assert.Equal("looks good", refreshed!.ReviewNote);
    }
}
