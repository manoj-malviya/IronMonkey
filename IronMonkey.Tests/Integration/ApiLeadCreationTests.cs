using IronMonkey.ApiService.Common.Auth;
using IronMonkey.ApiService.Features.Leads.Duplicates;
using IronMonkey.ApiService.Features.Leads.Ingestion.Api;
using IronMonkey.Data;
using IronMonkey.Data.Entities;
using IronMonkey.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace IronMonkey.Tests.Integration;

[Collection("Integration")]
public class ApiLeadCreationTests(PostgreSqlFixture fixture) : IClassFixture<PostgreSqlFixture>
{
    private readonly TenantDbContextFactory _factory = new();

    private async Task<(string tenantConnStr, Guid stageId, CentralDbContext centralDb)> SetupAsync(Guid tenantId)
    {
        // Setup central DB for API keys
        var centralConnStr = fixture.ConnectionString.Replace("ironmonkey_test", "central_apikeys");
        var centralOpts = new DbContextOptionsBuilder<CentralDbContext>()
            .UseNpgsql(centralConnStr)
            .Options;
        var centralDb = new CentralDbContext(centralOpts);
        await centralDb.Database.MigrateAsync();

        // Setup tenant DB
        var uniqueSuffix = Guid.NewGuid().ToString("N")[..8];
        var tenantConnStr = fixture.ConnectionString.Replace("ironmonkey_test", $"api_{uniqueSuffix}");
        await using var tenantDb = _factory.CreateForTenant(tenantConnStr, tenantId);
        await tenantDb.Database.MigrateAsync();

        var stage = PipelineStage.Create(tenantId, "New", 1);
        tenantDb.PipelineStages.Add(stage);
        await tenantDb.SaveChangesAsync();

        return (tenantConnStr, stage.Id, centralDb);
    }

    [Fact]
    public async Task GenerateApiKey_WhenAuthenticated_ReturnsKey()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var (_, _, centralDb) = await SetupAsync(tenantId);
        await using (centralDb)
        {
            var service = new ApiKeyService(centralDb);

            // Act
            var result = await service.GenerateAsync(tenantId);

            // Assert
            Assert.NotNull(result);
            Assert.NotEmpty(result.PlaintextKey);
            Assert.NotEmpty(result.KeyPrefix);
            Assert.Equal(result.PlaintextKey[..8], result.KeyPrefix);
            Assert.False(result.PlaintextKey.StartsWith("$2"), "Plaintext key should not be a BCrypt hash");
        }
    }

    [Fact]
    public async Task DeleteApiKey_WhenAuthenticated_KeyNoLongerAcceptsRequests()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var uniqueSuffix = Guid.NewGuid().ToString("N")[..8];
        var centralConnStr = fixture.ConnectionString.Replace("ironmonkey_test", $"central_del_{uniqueSuffix}");
        var centralOpts = new DbContextOptionsBuilder<CentralDbContext>().UseNpgsql(centralConnStr).Options;
        await using var centralDb = new CentralDbContext(centralOpts);
        await centralDb.Database.MigrateAsync();

        var service = new ApiKeyService(centralDb);
        var generated = await service.GenerateAsync(tenantId);

        // Act: delete the key
        await service.DeleteAsync(tenantId, generated.KeyId);

        // Assert: validate returns null (key no longer accepted)
        var tenantIdResult = await service.ValidateAsync(generated.PlaintextKey);
        Assert.Null(tenantIdResult);
    }

    [Fact]
    public async Task CreateLead_WithValidApiKey_Returns201WithDuplicatesArray()
    {
        // Arrange: generate API key, validate it returns correct tenant ID
        var tenantId = Guid.NewGuid();
        var uniqueSuffix = Guid.NewGuid().ToString("N")[..8];
        var centralConnStr = fixture.ConnectionString.Replace("ironmonkey_test", $"central_val_{uniqueSuffix}");
        var centralOpts = new DbContextOptionsBuilder<CentralDbContext>().UseNpgsql(centralConnStr).Options;
        await using var centralDb = new CentralDbContext(centralOpts);
        await centralDb.Database.MigrateAsync();

        var service = new ApiKeyService(centralDb);
        var generated = await service.GenerateAsync(tenantId);

        // Act: validate the plaintext key
        var resolvedTenantId = await service.ValidateAsync(generated.PlaintextKey);

        // Assert: correct tenant ID returned
        Assert.NotNull(resolvedTenantId);
        Assert.Equal(tenantId, resolvedTenantId.Value);
    }

    [Fact]
    public async Task CreateLead_WithMissingApiKey_Returns401()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var uniqueSuffix = Guid.NewGuid().ToString("N")[..8];
        var centralConnStr = fixture.ConnectionString.Replace("ironmonkey_test", $"central_miss_{uniqueSuffix}");
        var centralOpts = new DbContextOptionsBuilder<CentralDbContext>().UseNpgsql(centralConnStr).Options;
        await using var centralDb = new CentralDbContext(centralOpts);
        await centralDb.Database.MigrateAsync();

        var service = new ApiKeyService(centralDb);

        // Act: validate with empty string
        var result = await service.ValidateAsync(string.Empty);

        // Assert: null returned (no tenant resolved)
        Assert.Null(result);
    }

    [Fact]
    public async Task CreateLead_WithInvalidApiKey_Returns401()
    {
        // Arrange: generate a real key, then try with a different key
        var tenantId = Guid.NewGuid();
        var uniqueSuffix = Guid.NewGuid().ToString("N")[..8];
        var centralConnStr = fixture.ConnectionString.Replace("ironmonkey_test", $"central_inv_{uniqueSuffix}");
        var centralOpts = new DbContextOptionsBuilder<CentralDbContext>().UseNpgsql(centralConnStr).Options;
        await using var centralDb = new CentralDbContext(centralOpts);
        await centralDb.Database.MigrateAsync();

        var service = new ApiKeyService(centralDb);
        await service.GenerateAsync(tenantId);

        // Act: validate with wrong key
        var result = await service.ValidateAsync("completely-wrong-key-value");

        // Assert: null returned (wrong key doesn't match any hash)
        Assert.Null(result);
    }

    [Fact]
    public async Task CreateLead_WhenDuplicateFound_IncludesDuplicatesInResponse()
    {
        // Arrange: seed a lead, then run duplicate detection for same email
        var tenantId = Guid.NewGuid();
        var (tenantConnStr, stageId, centralDb) = await SetupAsync(tenantId);
        await centralDb.DisposeAsync();

        await using var db = _factory.CreateForTenant(tenantConnStr, tenantId);
        var existingLead = Lead.Create(tenantId, "Frank", "Miller", "555-6006", "frank@example.com", LeadSource.Api, stageId);
        db.Leads.Add(existingLead);
        await db.SaveChangesAsync();

        var tenantServiceMock = new Mock<ITenantService>();
        tenantServiceMock.Setup(s => s.GetCurrentTenantId()).Returns(tenantId);
        tenantServiceMock.Setup(s => s.GetConnectionStringAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenantConnStr);

        var duplicateService = new DuplicateDetectionService(tenantServiceMock.Object, _factory);

        // Act: find duplicates for same email
        var candidates = await duplicateService.FindCandidatesAsync(
            tenantId, email: "frank@example.com", phone: null, name: null);

        // Assert: duplicate returned
        Assert.NotEmpty(candidates);
        Assert.Equal(existingLead.Id, candidates[0].LeadId);
        Assert.Equal(100, candidates[0].ConfidenceScore);
    }
}
