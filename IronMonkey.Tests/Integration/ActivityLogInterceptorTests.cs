using IronMonkey.Data;
using IronMonkey.Tests.Fixtures;
using Xunit;

namespace IronMonkey.Tests.Integration;

// ACTV-01: ActivityLog interceptor captures entity changes automatically
[Collection("Integration")]
public class ActivityLogInterceptorTests(PostgreSqlFixture fixture) : IClassFixture<PostgreSqlFixture>
{
    private readonly TenantDbContextFactory _factory = new();

    [Fact(Skip = "not implemented — Phase 5 Plan 03")]
    public async Task SaveChanges_WhenLeadCreated_CreatesActivityLogEntry() => await Task.CompletedTask;

    [Fact(Skip = "not implemented — Phase 5 Plan 03")]
    public async Task SaveChanges_WhenLeadFieldUpdated_CapturesOldAndNewValues() => await Task.CompletedTask;

    [Fact(Skip = "not implemented — Phase 5 Plan 03")]
    public async Task SaveChanges_WhenLeadStageMoved_CapturesStageMoveEvent() => await Task.CompletedTask;

    [Fact(Skip = "not implemented — Phase 5 Plan 03")]
    public async Task SaveChanges_WhenTaskCompleted_CreatesActivityLogEntry() => await Task.CompletedTask;

    [Fact(Skip = "not implemented — Phase 5 Plan 03")]
    public async Task ActivityLog_IsTenantScoped_NoCrossTenantLeakage() => await Task.CompletedTask;
}
