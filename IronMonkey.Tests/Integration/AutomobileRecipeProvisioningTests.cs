using IronMonkey.ApiService.Authentication.Services;
using IronMonkey.Data;
using IronMonkey.Data.Entities;
using IronMonkey.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;
using BC = BCrypt.Net.BCrypt;

namespace IronMonkey.Tests.Integration;

[Collection("Integration")]
public class AutomobileRecipeProvisioningTests : IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture _fixture;

    // Fixed GUID for Automobile Dealership recipe — seeded by SeedDomainRecipes migration
    private static readonly Guid AutomobileRecipeId = new Guid("00000000-0000-0000-0000-000000000002");

    public AutomobileRecipeProvisioningTests(PostgreSqlFixture fixture) => _fixture = fixture;

    private CentralDbContext CreateCentralDbContext()
    {
        var options = new DbContextOptionsBuilder<CentralDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options;
        return new CentralDbContext(options);
    }

    private TenantProvisioningService CreateProvisioningService(CentralDbContext centralDb)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:CentralDb"] = _fixture.ConnectionString
            })
            .Build();
        return new TenantProvisioningService(centralDb, new TenantDbContextFactory(), config);
    }

    [Fact]
    public async Task ProvisionTenant_WithAutomobileRecipe_SeedsAllPipelineStages()
    {
        await using var centralDb = CreateCentralDbContext();
        await centralDb.Database.MigrateAsync();

        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        var signupRequest = SignupRequest.Create(
            companyName: $"Auto Dealer {uniqueId}",
            adminEmail: $"admin-{uniqueId}@autodealer.com",
            adminPasswordHash: BC.HashPassword("SecurePass123!"),
            phone: "555-0100",
            recipeId: null,
            companySize: "10-50",
            address: "123 Main St",
            billingContact: $"billing-{uniqueId}@autodealer.com");
        centralDb.SignupRequests.Add(signupRequest);
        await centralDb.SaveChangesAsync();
        signupRequest.Approve();
        await centralDb.SaveChangesAsync();

        var service = CreateProvisioningService(centralDb);
        await service.ProvisionTenantAsync(signupRequest.Id, AutomobileRecipeId);

        var tenant = await centralDb.Tenants.AsNoTracking()
            .SingleAsync(t => t.Name == $"Auto Dealer {uniqueId}");
        var tenantContextFactory = new TenantDbContextFactory();
        await using var tenantDb = tenantContextFactory.CreateForTenant(tenant.DatabaseConnectionString!, tenant.Id);

        var stages = await tenantDb.PipelineStages.OrderBy(s => s.Order).ToListAsync();
        Assert.Equal(6, stages.Count);
        Assert.Contains(stages, s => s.Name == "Inquiry" && s.StageType == StageType.Entry);
        Assert.Contains(stages, s => s.Name == "Test Drive" && s.StageType == StageType.Active);
        Assert.Contains(stages, s => s.Name == "Negotiation" && s.StageType == StageType.Active);
        Assert.Contains(stages, s => s.Name == "F&I" && s.StageType == StageType.Active);
        Assert.Contains(stages, s => s.Name == "Sold" && s.StageType == StageType.ClosedWon);
        Assert.Contains(stages, s => s.Name == "Lost" && s.StageType == StageType.ClosedLost);

        // Assert ordered by Order property (0..5)
        for (int i = 0; i < stages.Count; i++)
        {
            Assert.Equal(i, stages[i].Order);
        }
    }

    [Fact]
    public async Task ProvisionTenant_WithAutomobileRecipe_SeedsAllCustomFields()
    {
        await using var centralDb = CreateCentralDbContext();
        await centralDb.Database.MigrateAsync();

        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        var signupRequest = SignupRequest.Create(
            companyName: $"Auto Fields {uniqueId}",
            adminEmail: $"admin-{uniqueId}@autofields.com",
            adminPasswordHash: BC.HashPassword("SecurePass123!"),
            phone: "555-0101",
            recipeId: null,
            companySize: "10-50",
            address: "456 Oak Ave",
            billingContact: $"billing-{uniqueId}@autofields.com");
        centralDb.SignupRequests.Add(signupRequest);
        await centralDb.SaveChangesAsync();
        signupRequest.Approve();
        await centralDb.SaveChangesAsync();

        var service = CreateProvisioningService(centralDb);
        await service.ProvisionTenantAsync(signupRequest.Id, AutomobileRecipeId);

        var tenant = await centralDb.Tenants.AsNoTracking()
            .SingleAsync(t => t.Name == $"Auto Fields {uniqueId}");
        var tenantContextFactory = new TenantDbContextFactory();
        await using var tenantDb = tenantContextFactory.CreateForTenant(tenant.DatabaseConnectionString!, tenant.Id);

        var fields = await tenantDb.CustomFieldDefinitions.ToListAsync();
        Assert.Equal(6, fields.Count);
        Assert.Contains(fields, f => f.FieldName == "Vehicle Make" && f.FieldType == CustomFieldType.Dropdown);
        Assert.Contains(fields, f => f.FieldName == "Vehicle Model" && f.FieldType == CustomFieldType.Text);
        Assert.Contains(fields, f => f.FieldName == "Vehicle Year" && f.FieldType == CustomFieldType.Number);
        Assert.Contains(fields, f => f.FieldName == "Budget Range" && f.FieldType == CustomFieldType.Dropdown);
        Assert.Contains(fields, f => f.FieldName == "Has Trade-In" && f.FieldType == CustomFieldType.Boolean);
        Assert.Contains(fields, f => f.FieldName == "Preferred Contact" && f.FieldType == CustomFieldType.Dropdown);
    }

    [Fact]
    public async Task ProvisionTenant_WithAutomobileRecipe_SeedsWorkflowRules()
    {
        await using var centralDb = CreateCentralDbContext();
        await centralDb.Database.MigrateAsync();

        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        var signupRequest = SignupRequest.Create(
            companyName: $"Auto Rules {uniqueId}",
            adminEmail: $"admin-{uniqueId}@autorules.com",
            adminPasswordHash: BC.HashPassword("SecurePass123!"),
            phone: "555-0102",
            recipeId: null,
            companySize: "10-50",
            address: "789 Pine Rd",
            billingContact: $"billing-{uniqueId}@autorules.com");
        centralDb.SignupRequests.Add(signupRequest);
        await centralDb.SaveChangesAsync();
        signupRequest.Approve();
        await centralDb.SaveChangesAsync();

        var service = CreateProvisioningService(centralDb);
        await service.ProvisionTenantAsync(signupRequest.Id, AutomobileRecipeId);

        var tenant = await centralDb.Tenants.AsNoTracking()
            .SingleAsync(t => t.Name == $"Auto Rules {uniqueId}");
        var tenantContextFactory = new TenantDbContextFactory();
        await using var tenantDb = tenantContextFactory.CreateForTenant(tenant.DatabaseConnectionString!, tenant.Id);

        var rules = await tenantDb.WorkflowRules.ToListAsync();
        Assert.Equal(2, rules.Count);
        Assert.Contains(rules, r => r.Name == "Notify on Negotiation" && r.Trigger == WorkflowTrigger.StatusChange);
        Assert.Contains(rules, r => r.Name == "Flag Stale Inquiry" && r.Trigger == WorkflowTrigger.TimeElapsed);
    }

    [Fact]
    public async Task ProvisionTenant_WithAutomobileRecipe_SeedsRoles()
    {
        await using var centralDb = CreateCentralDbContext();
        await centralDb.Database.MigrateAsync();

        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        var signupRequest = SignupRequest.Create(
            companyName: $"Auto Roles {uniqueId}",
            adminEmail: $"admin-{uniqueId}@autoroles.com",
            adminPasswordHash: BC.HashPassword("SecurePass123!"),
            phone: "555-0103",
            recipeId: null,
            companySize: "10-50",
            address: "321 Elm St",
            billingContact: $"billing-{uniqueId}@autoroles.com");
        centralDb.SignupRequests.Add(signupRequest);
        await centralDb.SaveChangesAsync();
        signupRequest.Approve();
        await centralDb.SaveChangesAsync();

        var service = CreateProvisioningService(centralDb);
        await service.ProvisionTenantAsync(signupRequest.Id, AutomobileRecipeId);

        var tenant = await centralDb.Tenants.AsNoTracking()
            .SingleAsync(t => t.Name == $"Auto Roles {uniqueId}");
        var tenantContextFactory = new TenantDbContextFactory();
        await using var tenantDb = tenantContextFactory.CreateForTenant(tenant.DatabaseConnectionString!, tenant.Id);

        var roles = await tenantDb.Roles.ToListAsync();

        // Per Pitfall 5 in RESEARCH.md, recipe RoleDefinition is informational only.
        // In Phase 7, domain-specific roles are NOT created as Role entities —
        // only system roles (SuperAdmin=1, Admin=201, Owner=301, TeleCaller=302) exist.
        // TODO Phase 8: custom recipe roles (Sales Manager etc) will be creatable via admin API
        Assert.Contains(roles, r => r.Name == "SuperAdmin");
        Assert.Contains(roles, r => r.Name == "Admin");
        Assert.Contains(roles, r => r.Name == "Owner");
        Assert.Contains(roles, r => r.Name == "TeleCaller");
    }

    [Fact]
    public async Task ProvisionTenant_WithAutomobileRecipe_SeedsSampleLeads()
    {
        await using var centralDb = CreateCentralDbContext();
        await centralDb.Database.MigrateAsync();

        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        var signupRequest = SignupRequest.Create(
            companyName: $"Auto Leads {uniqueId}",
            adminEmail: $"admin-{uniqueId}@autoleads.com",
            adminPasswordHash: BC.HashPassword("SecurePass123!"),
            phone: "555-0104",
            recipeId: null,
            companySize: "10-50",
            address: "654 Maple Ave",
            billingContact: $"billing-{uniqueId}@autoleads.com");
        centralDb.SignupRequests.Add(signupRequest);
        await centralDb.SaveChangesAsync();
        signupRequest.Approve();
        await centralDb.SaveChangesAsync();

        var service = CreateProvisioningService(centralDb);
        await service.ProvisionTenantAsync(signupRequest.Id, AutomobileRecipeId);

        var tenant = await centralDb.Tenants.AsNoTracking()
            .SingleAsync(t => t.Name == $"Auto Leads {uniqueId}");
        var tenantContextFactory = new TenantDbContextFactory();
        await using var tenantDb = tenantContextFactory.CreateForTenant(tenant.DatabaseConnectionString!, tenant.Id);

        var leads = await tenantDb.Leads.Include(l => l.Stage).ToListAsync();
        Assert.Equal(5, leads.Count);
        Assert.Equal(2, leads.Count(l => l.Stage.Name == "Inquiry"));
        Assert.Equal(1, leads.Count(l => l.Stage.Name == "Test Drive"));
        Assert.Equal(1, leads.Count(l => l.Stage.Name == "Negotiation"));
        Assert.Equal(1, leads.Count(l => l.Stage.Name == "F&I"));
        Assert.Equal(0, leads.Count(l => l.Stage.Name == "Sold"));
        Assert.Equal(0, leads.Count(l => l.Stage.Name == "Lost"));
        Assert.All(leads, l => Assert.Equal(LeadSource.WebForm, l.Source));
    }

    [Fact]
    public async Task ProvisionTenant_WithAutomobileRecipe_SampleLeadsHaveCustomFields()
    {
        await using var centralDb = CreateCentralDbContext();
        await centralDb.Database.MigrateAsync();

        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        var signupRequest = SignupRequest.Create(
            companyName: $"Auto CF Leads {uniqueId}",
            adminEmail: $"admin-{uniqueId}@autocfleads.com",
            adminPasswordHash: BC.HashPassword("SecurePass123!"),
            phone: "555-0105",
            recipeId: null,
            companySize: "10-50",
            address: "987 Cedar Blvd",
            billingContact: $"billing-{uniqueId}@autocfleads.com");
        centralDb.SignupRequests.Add(signupRequest);
        await centralDb.SaveChangesAsync();
        signupRequest.Approve();
        await centralDb.SaveChangesAsync();

        var service = CreateProvisioningService(centralDb);
        await service.ProvisionTenantAsync(signupRequest.Id, AutomobileRecipeId);

        var tenant = await centralDb.Tenants.AsNoTracking()
            .SingleAsync(t => t.Name == $"Auto CF Leads {uniqueId}");
        var tenantContextFactory = new TenantDbContextFactory();
        await using var tenantDb = tenantContextFactory.CreateForTenant(tenant.DatabaseConnectionString!, tenant.Id);

        var leads = await tenantDb.Leads.Include(l => l.Stage).ToListAsync();

        // Find an Inquiry lead and verify custom field values are populated
        var inquiryLead = leads.First(l => l.Stage.Name == "Inquiry");
        Assert.True(inquiryLead.CustomFields.Values.ContainsKey("Vehicle Make"));
        Assert.True(inquiryLead.CustomFields.Values.ContainsKey("Budget Range"));
        Assert.NotNull(inquiryLead.CustomFields.Values["Vehicle Make"]);

        // Find Test Drive lead and verify custom fields
        var testDriveLead = leads.Single(l => l.Stage.Name == "Test Drive");
        Assert.True(testDriveLead.CustomFields.Values.ContainsKey("Vehicle Make"));
    }
}
