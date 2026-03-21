using IronMonkey.ApiService.BackgroundJobs;
using IronMonkey.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace IronMonkey.Tests.Unit;

// TNCY-02: Hangfire jobs carry explicit TenantId — no ambient tenant state
public class HangfireJobTenantTests
{
    [Fact]
    public async Task HangfireJob_WhenEnqueued_TenantIdPassedToRegistry()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var connectionString = "Host=localhost;Database=tenant_test;";

        var mockRegistry = new Mock<ITenantRegistry>();
        mockRegistry
            .Setup(r => r.GetConnectionStringAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(connectionString);

        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseInMemoryDatabase(databaseName: $"test-tenant-{Guid.NewGuid()}")
            .Options;

        var mockFactory = new Mock<ITenantDbContextFactory>();
        mockFactory
            .Setup(f => f.CreateForTenant(connectionString, tenantId))
            .Returns(new TenantDbContext(options, tenantId));

        var mockLogger = new Mock<ILogger<OutboxProcessingJob>>();

        var job = new OutboxProcessingJob(mockRegistry.Object, mockFactory.Object, mockLogger.Object);

        // Act
        await job.ExecuteAsync(tenantId, CancellationToken.None);

        // Assert: GetConnectionStringAsync was called with exactly tenantId, not any other guid
        mockRegistry.Verify(r => r.GetConnectionStringAsync(tenantId, It.IsAny<CancellationToken>()), Times.Once);
        mockRegistry.Verify(r => r.GetConnectionStringAsync(It.Is<Guid>(id => id != tenantId), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HangfireJob_WhenTenantIdInvalid_ThrowsInvalidOperationException()
    {
        // Arrange
        var unknownTenantId = Guid.NewGuid();

        var mockRegistry = new Mock<ITenantRegistry>();
        mockRegistry
            .Setup(r => r.GetConnectionStringAsync(unknownTenantId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException($"Tenant {unknownTenantId} is not provisioned or connection string is null."));

        var mockFactory = new Mock<ITenantDbContextFactory>();
        var mockLogger = new Mock<ILogger<OutboxProcessingJob>>();

        var job = new OutboxProcessingJob(mockRegistry.Object, mockFactory.Object, mockLogger.Object);

        // Act & Assert: InvalidOperationException propagates so Hangfire retries the job
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            job.ExecuteAsync(unknownTenantId, CancellationToken.None));
    }
}
