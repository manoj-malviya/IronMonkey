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
public class EducationRecipeProvisioningTests : IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture _fixture;

    // Fixed GUID for Education recipe — seeded by SeedDomainRecipes migration (Plan 01)
    private static readonly Guid EducationRecipeId = new Guid("00000000-0000-0000-0000-000000000003");

    public EducationRecipeProvisioningTests(PostgreSqlFixture fixture) => _fixture = fixture;

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
    public async Task SeedsAllPipelineStages()
    {
        await using var centralDb = CreateCentralDbContext();
        await centralDb.Database.MigrateAsync();

        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        var signupRequest = SignupRequest.Create(
            companyName: $"Edu Stages Academy {uniqueId}",
            adminEmail: $"admin-{uniqueId}@edastages.com",
            adminPasswordHash: BC.HashPassword("SecurePass123!"),
            phone: "555-0310",
            recipeId: null,
            companySize: "50-100",
            address: "100 Campus Dr",
            billingContact: "billing@edastages.com");
        centralDb.SignupRequests.Add(signupRequest);
        await centralDb.SaveChangesAsync();
        signupRequest.Approve();
        await centralDb.SaveChangesAsync();

        var service = CreateProvisioningService(centralDb);
        await service.ProvisionTenantAsync(signupRequest.Id, EducationRecipeId);

        var tenant = await centralDb.Tenants.AsNoTracking()
            .SingleAsync(t => t.Name == $"Edu Stages Academy {uniqueId}");
        var tenantContextFactory = new TenantDbContextFactory();
        await using var tenantDb = tenantContextFactory.CreateForTenant(tenant.DatabaseConnectionString!, tenant.Id);

        var stages = await tenantDb.PipelineStages.OrderBy(s => s.Order).ToListAsync();

        Assert.Equal(6, stages.Count);

        // Verify exact stage names and types (D-06)
        Assert.Contains(stages, s => s.Name == "Inquiry" && s.StageType == StageType.Entry);
        Assert.Contains(stages, s => s.Name == "Application" && s.StageType == StageType.Active);
        Assert.Contains(stages, s => s.Name == "Under Review" && s.StageType == StageType.Active);
        Assert.Contains(stages, s => s.Name == "Interview" && s.StageType == StageType.Active);
        Assert.Contains(stages, s => s.Name == "Enrolled" && s.StageType == StageType.ClosedWon);
        Assert.Contains(stages, s => s.Name == "Declined" && s.StageType == StageType.ClosedLost);

        // Verify stages are ordered 0..5
        for (int i = 0; i < stages.Count; i++)
            Assert.Equal(i, stages[i].Order);
    }

    [Fact]
    public async Task SeedsAllCustomFields()
    {
        await using var centralDb = CreateCentralDbContext();
        await centralDb.Database.MigrateAsync();

        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        var signupRequest = SignupRequest.Create(
            companyName: $"Edu Fields Academy {uniqueId}",
            adminEmail: $"admin-{uniqueId}@edafields.com",
            adminPasswordHash: BC.HashPassword("SecurePass123!"),
            phone: "555-0311",
            recipeId: null,
            companySize: "50-100",
            address: "200 Scholar Lane",
            billingContact: "billing@edafields.com");
        centralDb.SignupRequests.Add(signupRequest);
        await centralDb.SaveChangesAsync();
        signupRequest.Approve();
        await centralDb.SaveChangesAsync();

        var service = CreateProvisioningService(centralDb);
        await service.ProvisionTenantAsync(signupRequest.Id, EducationRecipeId);

        var tenant = await centralDb.Tenants.AsNoTracking()
            .SingleAsync(t => t.Name == $"Edu Fields Academy {uniqueId}");
        var tenantContextFactory = new TenantDbContextFactory();
        await using var tenantDb = tenantContextFactory.CreateForTenant(tenant.DatabaseConnectionString!, tenant.Id);

        var fields = await tenantDb.CustomFieldDefinitions.ToListAsync();

        Assert.Equal(6, fields.Count);

        // Verify exact field names and types (D-07)
        Assert.Contains(fields, f => f.FieldName == "Program of Interest" && f.FieldType == CustomFieldType.Dropdown);
        Assert.Contains(fields, f => f.FieldName == "Grade/Year Level" && f.FieldType == CustomFieldType.Dropdown);
        Assert.Contains(fields, f => f.FieldName == "Previous School" && f.FieldType == CustomFieldType.Text);
        Assert.Contains(fields, f => f.FieldName == "Guardian Name" && f.FieldType == CustomFieldType.Text);
        Assert.Contains(fields, f => f.FieldName == "Guardian Phone" && f.FieldType == CustomFieldType.Text);
        Assert.Contains(fields, f => f.FieldName == "Scholarship Needed" && f.FieldType == CustomFieldType.Boolean);
    }

    [Fact]
    public async Task SeedsWorkflowRules()
    {
        await using var centralDb = CreateCentralDbContext();
        await centralDb.Database.MigrateAsync();

        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        var signupRequest = SignupRequest.Create(
            companyName: $"Edu Rules Academy {uniqueId}",
            adminEmail: $"admin-{uniqueId}@edarules.com",
            adminPasswordHash: BC.HashPassword("SecurePass123!"),
            phone: "555-0312",
            recipeId: null,
            companySize: "50-100",
            address: "300 Admissions Blvd",
            billingContact: "billing@edarules.com");
        centralDb.SignupRequests.Add(signupRequest);
        await centralDb.SaveChangesAsync();
        signupRequest.Approve();
        await centralDb.SaveChangesAsync();

        var service = CreateProvisioningService(centralDb);
        await service.ProvisionTenantAsync(signupRequest.Id, EducationRecipeId);

        var tenant = await centralDb.Tenants.AsNoTracking()
            .SingleAsync(t => t.Name == $"Edu Rules Academy {uniqueId}");
        var tenantContextFactory = new TenantDbContextFactory();
        await using var tenantDb = tenantContextFactory.CreateForTenant(tenant.DatabaseConnectionString!, tenant.Id);

        var rules = await tenantDb.WorkflowRules.ToListAsync();

        Assert.Equal(2, rules.Count);

        // Verify exact rule names and trigger types (D-08)
        Assert.Contains(rules, r => r.Name == "Notify on Review" && r.Trigger == WorkflowTrigger.StatusChange);
        Assert.Contains(rules, r => r.Name == "Flag Stale Inquiry" && r.Trigger == WorkflowTrigger.TimeElapsed);
    }

    [Fact]
    public async Task SeedsRoles()
    {
        await using var centralDb = CreateCentralDbContext();
        await centralDb.Database.MigrateAsync();

        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        var signupRequest = SignupRequest.Create(
            companyName: $"Edu Roles Academy {uniqueId}",
            adminEmail: $"admin-{uniqueId}@edaroles.com",
            adminPasswordHash: BC.HashPassword("SecurePass123!"),
            phone: "555-0313",
            recipeId: null,
            companySize: "50-100",
            address: "400 Faculty Row",
            billingContact: "billing@edaroles.com");
        centralDb.SignupRequests.Add(signupRequest);
        await centralDb.SaveChangesAsync();
        signupRequest.Approve();
        await centralDb.SaveChangesAsync();

        var service = CreateProvisioningService(centralDb);
        await service.ProvisionTenantAsync(signupRequest.Id, EducationRecipeId);

        var tenant = await centralDb.Tenants.AsNoTracking()
            .SingleAsync(t => t.Name == $"Edu Roles Academy {uniqueId}");
        var tenantContextFactory = new TenantDbContextFactory();
        await using var tenantDb = tenantContextFactory.CreateForTenant(tenant.DatabaseConnectionString!, tenant.Id);

        var roles = await tenantDb.Roles.ToListAsync();

        // TODO (Phase 7 Pitfall 5): Domain roles (Admissions Director, Admissions Officer, Academic Counselor)
        // are informational only in Phase 7. TenantProvisioningService does not process the Roles section
        // of RecipeContentModel — roles are seeded via EF migration (SuperAdmin=1, Admin=201, Owner=301, TeleCaller=302).
        // Domain-specific roles will be seeded in a future phase when granular permissions are implemented.
        // For now, verify the platform roles seeded by migration are present.
        Assert.Contains(roles, r => r.Name == "Admin");
        Assert.Contains(roles, r => r.Name == "SuperAdmin");
    }

    [Fact]
    public async Task SeedsSampleLeads()
    {
        await using var centralDb = CreateCentralDbContext();
        await centralDb.Database.MigrateAsync();

        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        var signupRequest = SignupRequest.Create(
            companyName: $"Edu Leads Academy {uniqueId}",
            adminEmail: $"admin-{uniqueId}@edaleads.com",
            adminPasswordHash: BC.HashPassword("SecurePass123!"),
            phone: "555-0314",
            recipeId: null,
            companySize: "50-100",
            address: "500 Enrollment Ave",
            billingContact: "billing@edaleads.com");
        centralDb.SignupRequests.Add(signupRequest);
        await centralDb.SaveChangesAsync();
        signupRequest.Approve();
        await centralDb.SaveChangesAsync();

        var service = CreateProvisioningService(centralDb);
        await service.ProvisionTenantAsync(signupRequest.Id, EducationRecipeId);

        var tenant = await centralDb.Tenants.AsNoTracking()
            .SingleAsync(t => t.Name == $"Edu Leads Academy {uniqueId}");
        var tenantContextFactory = new TenantDbContextFactory();
        await using var tenantDb = tenantContextFactory.CreateForTenant(tenant.DatabaseConnectionString!, tenant.Id);

        var leads = await tenantDb.Leads.Include(l => l.Stage).ToListAsync();

        Assert.Equal(5, leads.Count);

        // Verify lead distribution across stages (D-11: 2 Inquiry, 1 Application, 1 Under Review, 1 Interview)
        Assert.Equal(2, leads.Count(l => l.Stage.Name == "Inquiry"));
        Assert.Equal(1, leads.Count(l => l.Stage.Name == "Application"));
        Assert.Equal(1, leads.Count(l => l.Stage.Name == "Under Review"));
        Assert.Equal(1, leads.Count(l => l.Stage.Name == "Interview"));

        // No leads in terminal stages
        Assert.Equal(0, leads.Count(l => l.Stage.Name == "Enrolled"));
        Assert.Equal(0, leads.Count(l => l.Stage.Name == "Declined"));

        // All leads sourced from WebForm (D-14)
        Assert.All(leads, l => Assert.Equal(LeadSource.WebForm, l.Source));
    }

    [Fact]
    public async Task SampleLeadCustomFields()
    {
        await using var centralDb = CreateCentralDbContext();
        await centralDb.Database.MigrateAsync();

        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        var signupRequest = SignupRequest.Create(
            companyName: $"Edu CF Academy {uniqueId}",
            adminEmail: $"admin-{uniqueId}@edacf.com",
            adminPasswordHash: BC.HashPassword("SecurePass123!"),
            phone: "555-0315",
            recipeId: null,
            companySize: "50-100",
            address: "600 Program St",
            billingContact: "billing@edacf.com");
        centralDb.SignupRequests.Add(signupRequest);
        await centralDb.SaveChangesAsync();
        signupRequest.Approve();
        await centralDb.SaveChangesAsync();

        var service = CreateProvisioningService(centralDb);
        await service.ProvisionTenantAsync(signupRequest.Id, EducationRecipeId);

        var tenant = await centralDb.Tenants.AsNoTracking()
            .SingleAsync(t => t.Name == $"Edu CF Academy {uniqueId}");
        var tenantContextFactory = new TenantDbContextFactory();
        await using var tenantDb = tenantContextFactory.CreateForTenant(tenant.DatabaseConnectionString!, tenant.Id);

        var leads = await tenantDb.Leads.Include(l => l.Stage).ToListAsync();

        // Verify Inquiry lead custom fields (D-13)
        var inquiryLead = leads.First(l => l.Stage.Name == "Inquiry");
        Assert.True(inquiryLead.CustomFields.Values.ContainsKey("Program of Interest"));
        Assert.NotNull(inquiryLead.CustomFields.Values["Program of Interest"]);

        // Verify Application lead custom fields (D-13)
        var applicationLead = leads.Single(l => l.Stage.Name == "Application");
        Assert.True(applicationLead.CustomFields.Values.ContainsKey("Program of Interest"));
        Assert.True(applicationLead.CustomFields.Values.ContainsKey("Previous School"));

        // Verify Under Review lead custom fields (D-13)
        var reviewLead = leads.Single(l => l.Stage.Name == "Under Review");
        Assert.True(reviewLead.CustomFields.Values.ContainsKey("Program of Interest"));
    }
}
