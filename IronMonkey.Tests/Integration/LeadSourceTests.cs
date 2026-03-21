using IronMonkey.Data;
using IronMonkey.Data.Entities;
using IronMonkey.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace IronMonkey.Tests.Integration;

// LEAD-03: Lead source is stored and surfaced on lead detail
[Collection("Integration")]
public class LeadSourceTests(PostgreSqlFixture fixture) : IClassFixture<PostgreSqlFixture>
{
    private readonly TenantDbContextFactory _factory = new();

    [Fact]
    public async Task CreateLead_WithManualSource_SourceStoredCorrectly()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var uniqueSuffix = Guid.NewGuid().ToString("N")[..8];
        var connStr = fixture.ConnectionString.Replace("ironmonkey_test", $"ls_{uniqueSuffix}");
        await using var db = _factory.CreateForTenant(connStr, tenantId);
        await db.Database.MigrateAsync();

        // Seed required PipelineStage
        var stage = PipelineStage.Create(tenantId, "New Lead", 1);
        db.PipelineStages.Add(stage);
        await db.SaveChangesAsync();

        // Act: create lead with Manual source
        var lead = Lead.Create(tenantId, "Jane", "Smith", "555-999-0000", "jane@example.com", LeadSource.Manual, stage.Id);
        db.Leads.Add(lead);
        await db.SaveChangesAsync();

        // Assert: query fresh context
        var options = new DbContextOptionsBuilder<TenantDbContext>().UseNpgsql(connStr).Options;
        await using var freshDb = new TenantDbContext(options, tenantId);

        var savedLead = await freshDb.Leads.FirstAsync(l => l.Id == lead.Id);
        Assert.Equal(LeadSource.Manual, savedLead.Source);
    }

    [Fact]
    public async Task LeadDetail_IncludesSourceField()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var uniqueSuffix = Guid.NewGuid().ToString("N")[..8];
        var connStr = fixture.ConnectionString.Replace("ironmonkey_test", $"ls_{uniqueSuffix}");
        await using var db = _factory.CreateForTenant(connStr, tenantId);
        await db.Database.MigrateAsync();

        // Seed required PipelineStage
        var stage = PipelineStage.Create(tenantId, "Contacted", 1);
        db.PipelineStages.Add(stage);
        await db.SaveChangesAsync();

        // Act: create lead with Import source
        var lead = Lead.Create(tenantId, "Bob", "Jones", "555-777-8888", "bob@example.com", LeadSource.Import, stage.Id);
        db.Leads.Add(lead);
        await db.SaveChangesAsync();

        // Assert: source field is surfaced when querying lead detail
        var options = new DbContextOptionsBuilder<TenantDbContext>().UseNpgsql(connStr).Options;
        await using var freshDb = new TenantDbContext(options, tenantId);

        var savedLead = await freshDb.Leads.FirstAsync(l => l.Id == lead.Id);
        Assert.Equal(LeadSource.Import, savedLead.Source);
    }
}
