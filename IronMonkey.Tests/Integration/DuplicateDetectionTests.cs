using IronMonkey.ApiService.Common.Auth;
using IronMonkey.ApiService.Features.Leads.Duplicates;
using IronMonkey.Data;
using IronMonkey.Data.Entities;
using IronMonkey.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace IronMonkey.Tests.Integration;

// LEAD-04: Duplicate detection surfaces candidates by email, phone, and fuzzy name match
[Collection("Integration")]
public class DuplicateDetectionTests(PostgreSqlFixture fixture) : IClassFixture<PostgreSqlFixture>
{
    private readonly TenantDbContextFactory _factory = new();

    /// <summary>
    /// Creates a unique test database, applies migrations, seeds a PipelineStage,
    /// and returns the connection string plus the seeded stage Id.
    /// </summary>
    private async Task<(string connStr, Guid stageId)> SetupDbAsync(Guid tenantId)
    {
        var uniqueSuffix = Guid.NewGuid().ToString("N")[..8];
        var connStr = fixture.ConnectionString.Replace("ironmonkey_test", $"dd_{uniqueSuffix}");
        await using var db = _factory.CreateForTenant(connStr, tenantId);
        await db.Database.MigrateAsync();

        var stage = PipelineStage.Create(tenantId, "New", 1);
        db.PipelineStages.Add(stage);
        await db.SaveChangesAsync();

        return (connStr, stage.Id);
    }

    private DuplicateDetectionService CreateService(Guid tenantId, string connStr)
    {
        var tenantServiceMock = new Mock<ITenantService>();
        tenantServiceMock.Setup(s => s.GetCurrentTenantId()).Returns(tenantId);
        tenantServiceMock.Setup(s => s.GetConnectionStringAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(connStr);

        return new DuplicateDetectionService(tenantServiceMock.Object, _factory);
    }

    [Fact]
    public async Task DuplicateCheck_OnMatchingEmail_ReturnsCandidates()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var (connStr, stageId) = await SetupDbAsync(tenantId);

        await using var db = _factory.CreateForTenant(connStr, tenantId);
        var lead = Lead.Create(tenantId, "John", "Doe", "555-000-0001", "john@example.com", LeadSource.Manual, stageId);
        db.Leads.Add(lead);
        await db.SaveChangesAsync();

        var service = CreateService(tenantId, connStr);

        // Act
        var candidates = await service.FindCandidatesAsync(tenantId, email: "john@example.com", phone: null, name: null);

        // Assert
        Assert.Single(candidates);
        Assert.Equal(100, candidates[0].ConfidenceScore);
        Assert.Equal(lead.Id, candidates[0].LeadId);
    }

    [Fact]
    public async Task DuplicateCheck_OnMatchingPhone_ReturnsCandidates()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var (connStr, stageId) = await SetupDbAsync(tenantId);

        await using var db = _factory.CreateForTenant(connStr, tenantId);
        var lead = Lead.Create(tenantId, "Jane", "Smith", "555-123-4567", "jane@example.com", LeadSource.Manual, stageId);
        db.Leads.Add(lead);
        await db.SaveChangesAsync();

        var service = CreateService(tenantId, connStr);

        // Act: pass normalized phone (stripped of non-digits)
        var candidates = await service.FindCandidatesAsync(tenantId, email: null, phone: "5551234567", name: null);

        // Assert
        Assert.Single(candidates);
        Assert.Equal(95, candidates[0].ConfidenceScore);
        Assert.Equal(lead.Id, candidates[0].LeadId);
    }

    [Fact]
    public async Task DuplicateCheck_OnSimilarName_ReturnsFuzzyMatch()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var (connStr, stageId) = await SetupDbAsync(tenantId);

        await using var db = _factory.CreateForTenant(connStr, tenantId);
        var lead = Lead.Create(tenantId, "John", "Smith", "555-000-0002", "johnsmith@example.com", LeadSource.Manual, stageId);
        db.Leads.Add(lead);
        await db.SaveChangesAsync();

        var service = CreateService(tenantId, connStr);

        // Act: "Jon Smith" should fuzzy match "John Smith" with score >= 75
        var candidates = await service.FindCandidatesAsync(tenantId, email: null, phone: null, name: "Jon Smith");

        // Assert
        Assert.True(candidates.Count >= 1, "Expected at least one fuzzy match for 'Jon Smith' vs 'John Smith'");
        Assert.All(candidates, c => Assert.True(c.ConfidenceScore >= 75));
    }

    [Fact]
    public async Task DuplicateCheck_WithNoMatches_ReturnsEmptyList()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var (connStr, stageId) = await SetupDbAsync(tenantId);

        await using var db = _factory.CreateForTenant(connStr, tenantId);
        var lead = Lead.Create(tenantId, "Other", "Person", "555-000-0003", "other@example.com", LeadSource.Manual, stageId);
        db.Leads.Add(lead);
        await db.SaveChangesAsync();

        var service = CreateService(tenantId, connStr);

        // Act
        var candidates = await service.FindCandidatesAsync(tenantId, email: "nomatch@example.com", phone: null, name: null);

        // Assert
        Assert.Empty(candidates);
    }
}
