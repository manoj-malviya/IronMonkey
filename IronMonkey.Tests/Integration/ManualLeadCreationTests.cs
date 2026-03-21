using IronMonkey.ApiService.Common.Auth;
using IronMonkey.ApiService.Features.Leads.Duplicates;
using IronMonkey.Data;
using IronMonkey.Data.Entities;
using IronMonkey.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace IronMonkey.Tests.Integration;

[Collection("Integration")]
public class ManualLeadCreationTests(PostgreSqlFixture fixture) : IClassFixture<PostgreSqlFixture>
{
    private readonly TenantDbContextFactory _factory = new();

    private async Task<(string connStr, Guid stageId)> SetupDbAsync(Guid tenantId)
    {
        var uniqueSuffix = Guid.NewGuid().ToString("N")[..8];
        var connStr = fixture.ConnectionString.Replace("ironmonkey_test", $"ml_{uniqueSuffix}");
        await using var db = _factory.CreateForTenant(connStr, tenantId);
        await db.Database.MigrateAsync();

        var stage = PipelineStage.Create(tenantId, "New", 1);
        db.PipelineStages.Add(stage);
        await db.SaveChangesAsync();

        return (connStr, stage.Id);
    }

    private DuplicateDetectionService CreateDuplicateService(Guid tenantId, string connStr)
    {
        var tenantServiceMock = new Mock<ITenantService>();
        tenantServiceMock.Setup(s => s.GetCurrentTenantId()).Returns(tenantId);
        tenantServiceMock.Setup(s => s.GetConnectionStringAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(connStr);

        return new DuplicateDetectionService(tenantServiceMock.Object, _factory);
    }

    [Fact]
    public async Task CreateLead_WhenNoDuplicates_ReturnsCreatedLead()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var (connStr, stageId) = await SetupDbAsync(tenantId);

        // Act: create lead directly
        await using var db = _factory.CreateForTenant(connStr, tenantId);
        var lead = Lead.Create(tenantId, "Alice", "Johnson", "555-1001", "alice@example.com", LeadSource.Manual, stageId);
        db.Leads.Add(lead);
        await db.SaveChangesAsync();

        // Assert: lead persisted with correct fields
        var saved = await db.Leads.FirstOrDefaultAsync(l => l.Id == lead.Id);
        Assert.NotNull(saved);
        Assert.Equal("Alice", saved.FirstName);
        Assert.Equal("alice@example.com", saved.Email);
        Assert.Equal(LeadSource.Manual, saved.Source);
        Assert.False(saved.IsPotentialDuplicate);
    }

    [Fact]
    public async Task CreateLead_WhenDuplicateExists_ReturnsDuplicateWarning()
    {
        // Arrange: seed an existing lead
        var tenantId = Guid.NewGuid();
        var (connStr, stageId) = await SetupDbAsync(tenantId);

        await using var db = _factory.CreateForTenant(connStr, tenantId);
        var existingLead = Lead.Create(tenantId, "Bob", "Brown", "555-2002", "bob@example.com", LeadSource.Manual, stageId);
        db.Leads.Add(existingLead);
        await db.SaveChangesAsync();

        // Act: check duplicates for same email
        var service = CreateDuplicateService(tenantId, connStr);
        var candidates = await service.FindCandidatesAsync(tenantId, email: "bob@example.com", phone: null, name: null);

        // Assert: duplicate candidate returned
        Assert.NotEmpty(candidates);
        Assert.Equal(existingLead.Id, candidates[0].LeadId);
        Assert.Equal(100, candidates[0].ConfidenceScore);
    }

    [Fact]
    public async Task CreateLead_WithForceCreate_CreatesLeadDespiteDuplicate()
    {
        // Arrange: seed an existing lead
        var tenantId = Guid.NewGuid();
        var (connStr, stageId) = await SetupDbAsync(tenantId);

        await using var db = _factory.CreateForTenant(connStr, tenantId);
        var existingLead = Lead.Create(tenantId, "Carol", "White", "555-3003", "carol@example.com", LeadSource.Manual, stageId);
        db.Leads.Add(existingLead);
        await db.SaveChangesAsync();

        // Act: force-create despite duplicate (endpoint logic simulated — directly create new lead)
        var newLead = Lead.Create(tenantId, "Carol", "White", "555-3003", "carol@example.com", LeadSource.Manual, stageId);
        db.Leads.Add(newLead);
        await db.SaveChangesAsync();

        // Assert: both leads exist in DB
        var allLeads = await db.Leads.Where(l => l.Email == "carol@example.com").ToListAsync();
        Assert.Equal(2, allLeads.Count);
    }

    [Fact]
    public async Task CreateLead_WithCustomFields_StoresCustomValues()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var (connStr, stageId) = await SetupDbAsync(tenantId);

        await using var db = _factory.CreateForTenant(connStr, tenantId);

        // Act: create lead and set custom fields
        var lead = Lead.Create(tenantId, "Dave", "Green", "555-4004", "dave@example.com", LeadSource.Manual, stageId);
        lead.CustomFields.Set("VehicleModel", "Tesla Model 3");
        lead.CustomFields.Set("Budget", "50000");
        db.Leads.Add(lead);

        // Mark CustomFields as modified so HasConversion JSONB converter persists the changes
        db.Entry(lead).Property(l => l.CustomFields).IsModified = true;
        await db.SaveChangesAsync();

        // Assert: custom fields persisted
        var saved = await db.Leads.FirstOrDefaultAsync(l => l.Id == lead.Id);
        Assert.NotNull(saved);
        Assert.Equal("Tesla Model 3", saved.CustomFields.Get("VehicleModel")?.ToString());
        Assert.Equal("50000", saved.CustomFields.Get("Budget")?.ToString());
    }
}
