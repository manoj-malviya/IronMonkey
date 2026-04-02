using IronMonkey.ApiService.Authentication.Endpoints;
using IronMonkey.Data;
using IronMonkey.Data.Entities;
using IronMonkey.Tests.Fixtures;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Xunit;
using BC = BCrypt.Net.BCrypt;

namespace IronMonkey.Tests.Integration.TenantManagement;

[Collection("Integration")]
public class GetSignupRequestEndpointTests : IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture _fixture;

    public GetSignupRequestEndpointTests(PostgreSqlFixture fixture)
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
    public async Task GetSignupRequest_WhenExists_ReturnsDetailWithAllFields()
    {
        // Arrange
        await using var centralDb = CreateCentralDbContext();
        await centralDb.Database.MigrateAsync();

        var email = $"admin-{Guid.NewGuid():N}@gettest.com";
        var signup = SignupRequest.Create(
            companyName: "Detail Test Company",
            adminEmail: email,
            adminPasswordHash: BC.HashPassword("SecurePass123!"),
            phone: "555-9999",
            recipeId: null,
            companySize: "50-100",
            address: "42 Test Street",
            billingContact: "billing@detailtest.com");

        centralDb.SignupRequests.Add(signup);
        await centralDb.SaveChangesAsync();

        // Act
        var result = await GetSignupRequestEndpoint.HandleForTest(signup.Id, centralDb, CancellationToken.None);

        // Assert: should be 200 OK
        var okResult = Assert.IsType<Ok<GetSignupRequestEndpoint.SignupRequestDetail>>(result.Result);
        var detail = okResult.Value;
        Assert.NotNull(detail);
        Assert.Equal(signup.Id, detail.Id);
        Assert.Equal("Detail Test Company", detail.CompanyName);
        Assert.Equal(email, detail.AdminEmail);
        Assert.Equal("555-9999", detail.Phone);
        Assert.Equal("50-100", detail.CompanySize);
        Assert.Equal("42 Test Street", detail.Address);
        Assert.Equal("billing@detailtest.com", detail.BillingContact);
        Assert.Null(detail.RecipeId);
        Assert.Equal("Pending", detail.Status);
        Assert.Null(detail.ReviewNote);
        Assert.True(detail.CreatedAt > DateTime.MinValue);
    }

    [Fact]
    public async Task GetSignupRequest_WhenNotFound_Returns404()
    {
        // Arrange
        await using var centralDb = CreateCentralDbContext();
        await centralDb.Database.MigrateAsync();

        var nonExistentId = Guid.NewGuid();

        // Act
        var result = await GetSignupRequestEndpoint.HandleForTest(nonExistentId, centralDb, CancellationToken.None);

        // Assert: should be 404 NotFound
        Assert.IsType<NotFound>(result.Result);
    }
}
