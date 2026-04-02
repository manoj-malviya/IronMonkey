using IronMonkey.ApiService.Authentication.Endpoints;
using IronMonkey.Data;
using IronMonkey.Data.Entities;
using IronMonkey.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Xunit;
using BC = BCrypt.Net.BCrypt;

namespace IronMonkey.Tests.Integration.TenantManagement;

[Collection("Integration")]
public class RejectSignupRequestTests : IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture _fixture;

    public RejectSignupRequestTests(PostgreSqlFixture fixture)
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
    public async Task RejectSignup_WhenPending_ChangesStatusToRejected()
    {
        await using var centralDb = CreateCentralDbContext();
        await centralDb.Database.MigrateAsync();

        var signup = SignupRequest.Create(
            companyName: "Reject Co",
            adminEmail: $"admin-{Guid.NewGuid():N}@rejectco.com",
            adminPasswordHash: BC.HashPassword("Pass123!"),
            phone: "555-0300",
            recipeId: null,
            companySize: "1-10",
            address: "3 Reject Rd",
            billingContact: "b@rejectco.com");
        centralDb.SignupRequests.Add(signup);
        await centralDb.SaveChangesAsync();

        var request = new RejectTenantEndpoint.Request(RejectionReason: "incomplete info");
        await RejectTenantEndpoint.Handle(signup.Id, request, centralDb, CancellationToken.None);

        var refreshed = await centralDb.SignupRequests.FindAsync(signup.Id);
        Assert.Equal("Rejected", refreshed!.Status);
        Assert.Equal("incomplete info", refreshed.ReviewNote);
    }
}
