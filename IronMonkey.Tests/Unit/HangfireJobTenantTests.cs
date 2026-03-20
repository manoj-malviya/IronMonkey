using Moq;
using Xunit;

namespace IronMonkey.Tests.Unit;

// TNCY-02: Hangfire jobs carry explicit TenantId — no ambient tenant state
public class HangfireJobTenantTests
{
    [Fact(Skip = "Wave 0 stub — implement after Plan 05 (Hangfire integration)")]
    public async Task HangfireJob_WhenEnqueued_TenantIdSerializedAsExplicitParameter()
    {
        // Arrange: mock ITenantRegistry, create job with tenantId=X
        // Act: execute job with tenantId=X
        // Assert: job called GetConnectionStringAsync(X), not some other tenant
        await Task.CompletedTask;
    }

    [Fact(Skip = "Wave 0 stub — implement after Plan 05 (Hangfire integration)")]
    public async Task HangfireJob_WhenTenantIdInvalid_ThrowsTenantNotFoundException()
    {
        // Arrange: ITenantRegistry returns null for unknown tenantId
        // Act: execute job with unknown tenantId
        // Assert: throws TenantNotFoundException (not NullReferenceException)
        await Task.CompletedTask;
    }
}
