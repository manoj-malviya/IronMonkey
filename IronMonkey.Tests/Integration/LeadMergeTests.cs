using IronMonkey.ApiService.Common.Auth;
using IronMonkey.ApiService.Features.Leads.Merge;
using IronMonkey.Data;
using IronMonkey.Data.Entities;
using IronMonkey.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace IronMonkey.Tests.Integration;

// LEAD-05: Lead merge preserves surviving lead fields and creates audit record
[Collection("Integration")]
public class LeadMergeTests(PostgreSqlFixture fixture) : IClassFixture<PostgreSqlFixture>
{
    private readonly TenantDbContextFactory _factory = new();

    /// <summary>
    /// Creates a unique test database, applies migrations, seeds a PipelineStage,
    /// and returns the connection string plus the seeded stage Id.
    /// </summary>
    private async Task<(string connStr, Guid stageId)> SetupDbAsync(Guid tenantId)
    {
        var uniqueSuffix = Guid.NewGuid().ToString("N")[..8];
        var connStr = fixture.ConnectionString.Replace("ironmonkey_test", $"lm_{uniqueSuffix}");
        await using var db = _factory.CreateForTenant(connStr, tenantId);
        await db.Database.MigrateAsync();

        var stage = PipelineStage.Create(tenantId, "New", 1);
        db.PipelineStages.Add(stage);
        await db.SaveChangesAsync();

        return (connStr, stage.Id);
    }

    private LeadMergeService CreateService(Guid tenantId, string connStr)
    {
        var tenantServiceMock = new Mock<ITenantService>();
        tenantServiceMock.Setup(s => s.GetCurrentTenantId()).Returns(tenantId);
        tenantServiceMock.Setup(s => s.GetConnectionStringAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(connStr);

        return new LeadMergeService(tenantServiceMock.Object, _factory);
    }

    [Fact]
    public async Task MergeLeads_SurvivingLeadRetainsSourceFields()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var (connStr, stageId) = await SetupDbAsync(tenantId);
        var userId = Guid.NewGuid();

        await using var db = _factory.CreateForTenant(connStr, tenantId);
        var sourceLead = Lead.Create(tenantId, "Alice", "Cooper", "555-001-0001", "alice@example.com", LeadSource.Manual, stageId);
        var targetLead = Lead.Create(tenantId, "Bob", "Cooper", "555-001-0002", "bob@example.com", LeadSource.Manual, stageId);
        db.Leads.AddRange(sourceLead, targetLead);
        await db.SaveChangesAsync();

        var service = CreateService(tenantId, connStr);

        // Act
        var result = await service.MergeAsync(tenantId, sourceLead.Id, targetLead.Id, userId);

        // Assert: surviving lead retains source lead's FirstName
        Assert.Equal("Alice", result.FirstName);

        // Verify from database
        var options = new DbContextOptionsBuilder<TenantDbContext>().UseNpgsql(connStr).Options;
        await using var freshDb = new TenantDbContext(options, tenantId);
        var savedSource = await freshDb.Leads.FirstAsync(l => l.Id == sourceLead.Id);
        Assert.Equal("Alice", savedSource.FirstName);
    }

    [Fact]
    public async Task MergeLeads_TargetLeadIsSoftDeleted()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var (connStr, stageId) = await SetupDbAsync(tenantId);
        var userId = Guid.NewGuid();

        await using var db = _factory.CreateForTenant(connStr, tenantId);
        var sourceLead = Lead.Create(tenantId, "Carol", "White", "555-002-0001", "carol@example.com", LeadSource.Manual, stageId);
        var targetLead = Lead.Create(tenantId, "Dave", "White", "555-002-0002", "dave@example.com", LeadSource.Manual, stageId);
        db.Leads.AddRange(sourceLead, targetLead);
        await db.SaveChangesAsync();

        var service = CreateService(tenantId, connStr);

        // Act
        await service.MergeAsync(tenantId, sourceLead.Id, targetLead.Id, userId);

        // Assert: target lead is soft-deleted (query with IgnoreQueryFilters to bypass global filter)
        var options = new DbContextOptionsBuilder<TenantDbContext>().UseNpgsql(connStr).Options;
        await using var freshDb = new TenantDbContext(options, tenantId);

        var deletedTarget = await freshDb.Leads
            .IgnoreQueryFilters()
            .FirstAsync(l => l.Id == targetLead.Id);

        Assert.True(deletedTarget.IsDeleted);
        Assert.NotNull(deletedTarget.DeletedAt);
    }

    [Fact]
    public async Task MergeLeads_AuditRecordCreated_WithBothLeadIds()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var (connStr, stageId) = await SetupDbAsync(tenantId);
        var userId = Guid.NewGuid();

        await using var db = _factory.CreateForTenant(connStr, tenantId);
        var sourceLead = Lead.Create(tenantId, "Eve", "Green", "555-003-0001", "eve@example.com", LeadSource.Manual, stageId);
        var targetLead = Lead.Create(tenantId, "Frank", "Green", "555-003-0002", "frank@example.com", LeadSource.Manual, stageId);
        db.Leads.AddRange(sourceLead, targetLead);
        await db.SaveChangesAsync();

        var service = CreateService(tenantId, connStr);

        // Act
        await service.MergeAsync(tenantId, sourceLead.Id, targetLead.Id, userId);

        // Assert: audit record created with both lead IDs
        var options = new DbContextOptionsBuilder<TenantDbContext>().UseNpgsql(connStr).Options;
        await using var freshDb = new TenantDbContext(options, tenantId);

        var mergeRecords = await freshDb.LeadMerges.ToListAsync();
        Assert.Single(mergeRecords);
        Assert.Equal(sourceLead.Id, mergeRecords[0].SourceLeadId);
        Assert.Equal(targetLead.Id, mergeRecords[0].TargetLeadId);
    }

    [Fact]
    public async Task MergeLeads_SurvivingLeadCustomFieldsIncludeTargetValues()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var (connStr, stageId) = await SetupDbAsync(tenantId);
        var userId = Guid.NewGuid();

        await using var db = _factory.CreateForTenant(connStr, tenantId);

        // Create a CustomFieldDefinition
        var fieldDef = CustomFieldDefinition.Create(tenantId, "Industry", CustomFieldType.Text, false);
        db.CustomFieldDefinitions.Add(fieldDef);
        await db.SaveChangesAsync();

        var fieldKey = fieldDef.Id.ToString();

        // Source lead: null value for field-1
        var sourceLead = Lead.Create(tenantId, "Grace", "Black", "555-004-0001", "grace@example.com", LeadSource.Manual, stageId);
        // Do NOT set the custom field on source (null/missing)

        // Target lead: has "target-value" for field-1
        var targetLead = Lead.Create(tenantId, "Henry", "Black", "555-004-0002", "henry@example.com", LeadSource.Manual, stageId);
        targetLead.CustomFields.Set(fieldKey, "target-value");

        db.Leads.AddRange(sourceLead, targetLead);
        await db.SaveChangesAsync();

        var service = CreateService(tenantId, connStr);

        // Act
        await service.MergeAsync(tenantId, sourceLead.Id, targetLead.Id, userId);

        // Assert: source lead now has the target value for the previously-null field
        var options = new DbContextOptionsBuilder<TenantDbContext>().UseNpgsql(connStr).Options;
        await using var freshDb = new TenantDbContext(options, tenantId);

        var savedSource = await freshDb.Leads.FirstAsync(l => l.Id == sourceLead.Id);
        var mergedValue = savedSource.CustomFields.Get(fieldKey);
        Assert.Equal("target-value", mergedValue?.ToString());
    }
}
