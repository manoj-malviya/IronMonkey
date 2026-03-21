using IronMonkey.Data;
using IronMonkey.Data.Entities;
using IronMonkey.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace IronMonkey.Tests.Integration;

// LEAD-02: Pipeline stages are tenant-configured and ordered
[Collection("Integration")]
public class PipelineStageTests(PostgreSqlFixture fixture) : IClassFixture<PostgreSqlFixture>
{
    private readonly TenantDbContextFactory _factory = new();

    [Fact]
    public async Task CanCreatePipelineStage_WithNameAndOrder()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var uniqueSuffix = Guid.NewGuid().ToString("N")[..8];
        var connStr = fixture.ConnectionString.Replace("ironmonkey_test", $"ps_{uniqueSuffix}");
        await using var db = _factory.CreateForTenant(connStr, tenantId);
        await db.Database.MigrateAsync();

        // Act
        var stage = PipelineStage.Create(tenantId, "Qualified", 1);
        db.PipelineStages.Add(stage);
        await db.SaveChangesAsync();

        // Assert using fresh context
        var options = new DbContextOptionsBuilder<TenantDbContext>().UseNpgsql(connStr).Options;
        await using var freshDb = new TenantDbContext(options, tenantId);

        var savedStage = await freshDb.PipelineStages.FirstAsync(p => p.Id == stage.Id);
        Assert.Equal("Qualified", savedStage.Name);
        Assert.Equal(1, savedStage.Order);
        Assert.True(savedStage.IsActive);
    }

    [Fact]
    public async Task ListPipelineStages_ReturnsOnlyActiveStages()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var uniqueSuffix = Guid.NewGuid().ToString("N")[..8];
        var connStr = fixture.ConnectionString.Replace("ironmonkey_test", $"ps_{uniqueSuffix}");
        await using var db = _factory.CreateForTenant(connStr, tenantId);
        await db.Database.MigrateAsync();

        // Create one active stage and one deactivated stage
        var activeStage = PipelineStage.Create(tenantId, "Active Stage", 1);
        var inactiveStage = PipelineStage.Create(tenantId, "Inactive Stage", 2);
        db.PipelineStages.Add(activeStage);
        db.PipelineStages.Add(inactiveStage);
        await db.SaveChangesAsync();

        // Deactivate the second stage
        inactiveStage.Deactivate();
        db.PipelineStages.Update(inactiveStage);
        await db.SaveChangesAsync();

        // Act: query filtering active stages explicitly (IsActive is separate from IsDeleted)
        var options = new DbContextOptionsBuilder<TenantDbContext>().UseNpgsql(connStr).Options;
        await using var freshDb = new TenantDbContext(options, tenantId);

        var activeStages = await freshDb.PipelineStages
            .Where(p => p.IsActive)
            .ToListAsync();

        // Assert: only 1 active stage returned
        Assert.Single(activeStages);
        Assert.Equal("Active Stage", activeStages[0].Name);
    }

    [Fact]
    public async Task LeadStageId_MustReferenceValidTenantStage()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var uniqueSuffix = Guid.NewGuid().ToString("N")[..8];
        var connStr = fixture.ConnectionString.Replace("ironmonkey_test", $"ps_{uniqueSuffix}");
        await using var db = _factory.CreateForTenant(connStr, tenantId);
        await db.Database.MigrateAsync();

        // Act: create a lead with a stageId that has no matching PipelineStage (FK violation)
        var invalidStageId = Guid.NewGuid();
        var lead = Lead.Create(tenantId, "John", "Doe", "555-111-2222", "john@example.com", LeadSource.Manual, invalidStageId);
        db.Leads.Add(lead);

        // Assert: SaveChangesAsync throws due to FK constraint
        await Assert.ThrowsAsync<Microsoft.EntityFrameworkCore.DbUpdateException>(
            async () => await db.SaveChangesAsync());
    }
}
