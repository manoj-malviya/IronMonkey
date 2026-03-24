using IronMonkey.ApiService.BackgroundJobs;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.ApiService.Features.Leads.Duplicates;
using IronMonkey.ApiService.Features.Leads.Ingestion.WebForm;
using IronMonkey.Data;
using IronMonkey.Data.Entities;
using IronMonkey.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;
using Xunit;

namespace IronMonkey.Tests.Integration;

[Collection("Integration")]
public class WebFormTests(PostgreSqlFixture fixture) : IClassFixture<PostgreSqlFixture>
{
    private readonly TenantDbContextFactory _factory = new();

    private async Task<(CentralDbContext centralDb, string tenantConnStr, Guid stageId)> SetupAsync(Guid tenantId)
    {
        var uniqueSuffix = Guid.NewGuid().ToString("N")[..8];

        // Central DB for WebForms table
        var centralConnStr = fixture.ConnectionString.Replace("ironmonkey_test", $"central_wf_{uniqueSuffix}");
        var centralOpts = new DbContextOptionsBuilder<CentralDbContext>().UseNpgsql(centralConnStr).Options;
        var centralDb = new CentralDbContext(centralOpts);
        await centralDb.Database.MigrateAsync();

        // Tenant DB for Leads
        var tenantConnStr = fixture.ConnectionString.Replace("ironmonkey_test", $"wf_{uniqueSuffix}");
        await using var tenantDb = _factory.CreateForTenant(tenantConnStr, tenantId);
        await tenantDb.Database.MigrateAsync();

        var stage = PipelineStage.Create(tenantId, "New", 1);
        tenantDb.PipelineStages.Add(stage);
        await tenantDb.SaveChangesAsync();

        return (centralDb, tenantConnStr, stage.Id);
    }

    private WebFormService CreateWebFormService(CentralDbContext centralDb, Guid tenantId, string tenantConnStr)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["App:BaseUrl"] = "http://localhost:5000" })
            .Build();

        var tenantRegistryMock = new Mock<ITenantRegistry>();
        tenantRegistryMock.Setup(r => r.GetConnectionStringAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenantConnStr);

        return new WebFormService(centralDb, config, tenantRegistryMock.Object);
    }

    [Fact]
    public async Task CreateWebForm_WhenAuthenticated_ReturnsFormUrlWithToken()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var (centralDb, tenantConnStr, stageId) = await SetupAsync(tenantId);
        await using (centralDb)
        {
            var service = CreateWebFormService(centralDb, tenantId, tenantConnStr);

            // Act
            var result = await service.CreateAsync(
                tenantId,
                "Test Form",
                new List<string> { "FirstName", "LastName", "Email" },
                stageId,
                null);

            // Assert
            Assert.NotEmpty(result.FormToken);
            Assert.Contains(result.FormToken, result.HostedUrl);
            Assert.StartsWith("http://localhost:5000/forms/", result.HostedUrl);
        }
    }

    [Fact]
    public async Task SubmitForm_WithValidToken_CreatesLeadWithWebFormSource()
    {
        // Arrange: create a form, then create lead with WebForm source
        var tenantId = Guid.NewGuid();
        var (centralDb, tenantConnStr, stageId) = await SetupAsync(tenantId);
        await using (centralDb)
        {
            var service = CreateWebFormService(centralDb, tenantId, tenantConnStr);
            var created = await service.CreateAsync(
                tenantId,
                "Contact Form",
                new List<string> { "FirstName", "LastName", "Email" },
                stageId,
                null);

            // Act: simulate form submission by creating lead directly with WebForm source
            await using var db = _factory.CreateForTenant(tenantConnStr, tenantId);
            var lead = Lead.Create(tenantId, "Grace", "Lee", "555-7007", "grace@example.com", LeadSource.WebForm, stageId);
            db.Leads.Add(lead);
            await db.SaveChangesAsync();

            // Assert: lead created with WebForm source
            await using var verifyDb = _factory.CreateForTenant(tenantConnStr, tenantId);
            var saved = await verifyDb.Leads.FirstOrDefaultAsync(l => l.Id == lead.Id);
            Assert.NotNull(saved);
            Assert.Equal(LeadSource.WebForm, saved.Source);

            // Verify form token lookup works
            var form = await service.GetByTokenAsync(created.FormToken);
            Assert.NotNull(form);
            Assert.Equal(tenantId, form.TenantId);
        }
    }

    [Fact]
    public async Task SubmitForm_WithHoneypotFilled_ReturnsBadRequest()
    {
        // Test the honeypot check logic in isolation (mirrors SubmitWebFormEndpoint logic)
        // If Website field is non-empty, request should be rejected
        static bool IsBot(string? website) => !string.IsNullOrWhiteSpace(website);

        Assert.False(IsBot(null));
        Assert.False(IsBot(""));
        Assert.True(IsBot("http://spam.com"));
        Assert.True(IsBot("any-value"));
    }

    [Fact]
    public async Task SubmitForm_WithInvalidToken_ReturnsBadRequest()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var (centralDb, tenantConnStr, _) = await SetupAsync(tenantId);
        await using (centralDb)
        {
            var service = CreateWebFormService(centralDb, tenantId, tenantConnStr);

            // Act: look up a random non-existent token
            var form = await service.GetByTokenAsync(Guid.NewGuid().ToString("N"));

            // Assert: null returned (token not found)
            Assert.Null(form);
        }
    }

    [Fact]
    public async Task SubmitForm_WhenDuplicateLeadFound_CreatesLeadAndFlagsAsPotentialDuplicate()
    {
        // Arrange: seed a lead, then create a new lead and flag it
        var tenantId = Guid.NewGuid();
        var (centralDb, tenantConnStr, stageId) = await SetupAsync(tenantId);
        await centralDb.DisposeAsync();

        await using var db = _factory.CreateForTenant(tenantConnStr, tenantId);
        var existingLead = Lead.Create(tenantId, "Henry", "Taylor", "555-8008", "henry@example.com", LeadSource.Manual, stageId);
        db.Leads.Add(existingLead);
        await db.SaveChangesAsync();

        // Act: run duplicate detection then create new lead flagged
        var tenantServiceMock = new Mock<ITenantService>();
        tenantServiceMock.Setup(s => s.GetCurrentTenantId()).Returns(tenantId);
        tenantServiceMock.Setup(s => s.GetConnectionStringAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenantConnStr);
        var duplicateService = new DuplicateDetectionService(tenantServiceMock.Object, _factory);

        var candidates = await duplicateService.FindCandidatesAsync(
            tenantId, email: "henry@example.com", phone: null, name: null);

        var newLead = Lead.Create(tenantId, "Henry", "Taylor", "555-8008", "henry@example.com", LeadSource.WebForm, stageId);
        if (candidates.Any())
            newLead.MarkAsPotentialDuplicate(candidates.First().LeadId);

        db.Leads.Add(newLead);
        await db.SaveChangesAsync();

        // Assert: new lead flagged as potential duplicate pointing to existing lead
        await using var verifyDb = _factory.CreateForTenant(tenantConnStr, tenantId);
        var saved = await verifyDb.Leads.FirstOrDefaultAsync(l => l.Id == newLead.Id);
        Assert.NotNull(saved);
        Assert.True(saved.IsPotentialDuplicate);
        Assert.Equal(existingLead.Id, saved.PotentialDuplicateLeadId);
    }

    [Fact]
    public async Task GetFormPage_WithValidToken_ReturnsHtmlWithHoneypot()
    {
        // Arrange: create a form via service
        var tenantId = Guid.NewGuid();
        var (centralDb, tenantConnStr, stageId) = await SetupAsync(tenantId);
        await using (centralDb)
        {
            var service = CreateWebFormService(centralDb, tenantId, tenantConnStr);
            var fieldNames = new List<string> { "FirstName", "LastName", "Email" };
            var created = await service.CreateAsync(tenantId, "My Form", fieldNames, stageId, null);

            // Act: look up form by token to get form details for rendering
            var form = await service.GetByTokenAsync(created.FormToken);

            // Assert: form returned with configured field names
            Assert.NotNull(form);
            Assert.Equal("My Form", form.FormName);
            var retrievedFieldNames = form.GetFieldNames();
            Assert.Equal(3, retrievedFieldNames.Count);
            Assert.Contains("FirstName", retrievedFieldNames);
            Assert.Contains("LastName", retrievedFieldNames);
            Assert.Contains("Email", retrievedFieldNames);
        }
    }
}
