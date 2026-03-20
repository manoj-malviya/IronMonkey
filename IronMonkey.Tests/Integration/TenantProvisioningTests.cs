using IronMonkey.Tests.Fixtures;
using Xunit;

namespace IronMonkey.Tests.Integration;

// TNCY-01: Signup request collected -> platform admin approves -> provision creates isolated database
[Collection("Integration")]
public class TenantProvisioningTests : IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture _fixture;

    public TenantProvisioningTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact(Skip = "Wave 0 stub — implement after Plan 04 (TenantProvisioningService)")]
    public async Task SubmitSignupRequest_WhenValidData_PersistsToDatabase()
    {
        // Arrange: POST /auth/signup with company name, admin email, phone, industry, size
        // Act: call endpoint
        // Assert: SignupRequest exists in central DB with status Pending
        await Task.CompletedTask;
    }

    [Fact(Skip = "Wave 0 stub — implement after Plan 04 (TenantProvisioningService)")]
    public async Task ApproveTenant_WhenPending_UpdatesStatusToApproved()
    {
        // Arrange: existing SignupRequest with status Pending
        // Act: POST /admin/signup/{id}/approve
        // Assert: SignupRequest.Status == "Approved"
        await Task.CompletedTask;
    }

    [Fact(Skip = "Wave 0 stub — implement after Plan 04 (TenantProvisioningService)")]
    public async Task ProvisionTenant_WhenApproved_CreatesSeparateDatabase()
    {
        // Arrange: approved SignupRequest
        // Act: POST /admin/tenants/{id}/provision
        // Assert: new PostgreSQL database ironmonkey_{slug} exists
        // Assert: Tenant.DatabaseConnectionString is set in central DB
        await Task.CompletedTask;
    }

    [Fact(Skip = "Wave 0 stub — implement after Plan 04 (TenantProvisioningService)")]
    public async Task ProvisionTenant_WhenApproved_SeedsDefaultData()
    {
        // Arrange: approved SignupRequest
        // Act: POST /admin/tenants/{id}/provision
        // Assert: tenant DB has admin user, Admin role, default pipeline
        await Task.CompletedTask;
    }
}
