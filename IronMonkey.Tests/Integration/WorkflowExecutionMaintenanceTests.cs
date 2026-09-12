using IronMonkey.ApiService.BackgroundJobs;
using IronMonkey.ApiService.Features.Leads.Workflow.Execution;
using IronMonkey.Data;
using IronMonkey.Data.Entities;
using IronMonkey.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace IronMonkey.Tests.Integration;

/// <summary>
/// Reconciliation of stale Running rows, and the retention sweep.
///
/// Both matter for correctness, not just capacity: a row stuck in Running shows an Admin a
/// run that never ends, and unbounded history grows inside the tenant's own database.
/// </summary>
[Collection("Integration")]
public class WorkflowExecutionMaintenanceTests(PostgreSqlFixture fixture) : IClassFixture<PostgreSqlFixture>
{
    private readonly TenantDbContextFactory _factory = new();

    private sealed class SingleTenantRegistry(Guid tenantId, string connectionString) : ITenantRegistry
    {
        public Task<string> GetConnectionStringAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(connectionString);

        public Task<IReadOnlyList<(Guid TenantId, string ConnectionString)>> GetAllProvisionedTenantsAsync(
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<(Guid, string)>>([(tenantId, connectionString)]);
    }

    /// <summary>A clock the test moves, so staleness and retention can be crossed deliberately.</summary>
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private async Task<(string ConnectionString, WorkflowRule Rule, Guid LeadId)> SetupAsync(Guid tenantId, string label)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var connectionString = fixture.ConnectionString.Replace("ironmonkey_test", $"wfm_{label}_{suffix}");

        await using var db = _factory.CreateForTenant(connectionString, tenantId);
        await db.Database.MigrateAsync();

        var stage = PipelineStage.Create(tenantId, "New", 1);
        db.PipelineStages.Add(stage);
        var lead = Lead.Create(tenantId, "Ada", "Lovelace", "555-0001", "ada@t.com", LeadSource.Manual, stage.Id);
        db.Leads.Add(lead);
        var rule = WorkflowRule.Create(tenantId, "Rule", WorkflowTrigger.StatusChange, "{}", "{}");
        db.WorkflowRules.Add(rule);
        await db.SaveChangesAsync();

        return (connectionString, rule, lead.Id);
    }

    private static WorkflowExecutionMaintenanceJob BuildJob(
        Guid tenantId, string connectionString, TenantDbContextFactory factory,
        TimeProvider clock, WorkflowExecutionLogOptions? options = null)
        => new(
            new SingleTenantRegistry(tenantId, connectionString),
            factory,
            Options.Create(options ?? new WorkflowExecutionLogOptions()),
            clock,
            NullLogger<WorkflowExecutionMaintenanceJob>.Instance);

    [Fact]
    public async Task StaleRunningRow_IsMarkedAbandonedWithAnEndTime()
    {
        var tenantId = Guid.NewGuid();
        var (connStr, rule, leadId) = await SetupAsync(tenantId, "stale");
        var started = new DateTime(2026, 9, 11, 8, 0, 0, DateTimeKind.Utc);
        Guid stuckId;

        await using (var db = _factory.CreateForTenant(connStr, tenantId))
        {
            // Never closed — exactly what a killed worker leaves behind.
            var stuck = WorkflowExecutionLog.Start(tenantId, rule, leadId, "Ada Lovelace",
                WorkflowTrigger.StatusChange, "corr-stuck", "job-1", 1, started);
            db.WorkflowExecutionLogs.Add(stuck);
            await db.SaveChangesAsync();
            stuckId = stuck.Id;
        }

        // Two hours later, past the default 60-minute threshold.
        var clock = new FixedClock(new DateTimeOffset(started.AddHours(2), TimeSpan.Zero));
        await BuildJob(tenantId, connStr, _factory, clock).ExecuteAsync(CancellationToken.None);

        await using var check = _factory.CreateForTenant(connStr, tenantId);
        var reconciled = await check.WorkflowExecutionLogs.SingleAsync(l => l.Id == stuckId);

        // Abandoned, not Failed: whether the actions ran is genuinely unknown.
        Assert.Equal(WorkflowExecutionStatus.Abandoned, reconciled.Status);
        // And it has an end time, so the UI never renders it as still in flight.
        Assert.NotNull(reconciled.CompletedAt);
        Assert.NotNull(reconciled.DurationMs);
        Assert.NotNull(reconciled.ErrorMessage);
    }

    [Fact]
    public async Task RecentRunningRow_IsLeftAlone()
    {
        var tenantId = Guid.NewGuid();
        var (connStr, rule, leadId) = await SetupAsync(tenantId, "fresh");
        var started = new DateTime(2026, 9, 11, 8, 0, 0, DateTimeKind.Utc);
        Guid runningId;

        await using (var db = _factory.CreateForTenant(connStr, tenantId))
        {
            var running = WorkflowExecutionLog.Start(tenantId, rule, leadId, "Ada Lovelace",
                WorkflowTrigger.StatusChange, "corr-running", "job-2", 1, started);
            db.WorkflowExecutionLogs.Add(running);
            await db.SaveChangesAsync();
            runningId = running.Id;
        }

        // Five minutes in: a slow-but-working webhook must not be declared abandoned.
        var clock = new FixedClock(new DateTimeOffset(started.AddMinutes(5), TimeSpan.Zero));
        await BuildJob(tenantId, connStr, _factory, clock).ExecuteAsync(CancellationToken.None);

        await using var check = _factory.CreateForTenant(connStr, tenantId);
        var untouched = await check.WorkflowExecutionLogs.SingleAsync(l => l.Id == runningId);

        Assert.Equal(WorkflowExecutionStatus.Running, untouched.Status);
        Assert.Null(untouched.CompletedAt);
    }

    [Fact]
    public async Task RunsPastRetention_AreDeletedWithTheirSteps()
    {
        var tenantId = Guid.NewGuid();
        var (connStr, rule, leadId) = await SetupAsync(tenantId, "retain");
        var now = new DateTime(2026, 9, 11, 12, 0, 0, DateTimeKind.Utc);

        await using (var db = _factory.CreateForTenant(connStr, tenantId))
        {
            // One inside the 90-day window, one well outside it.
            foreach (var (age, corr) in new[] { (10, "corr-recent"), (200, "corr-ancient") })
            {
                var startedAt = now.AddDays(-age);
                var log = WorkflowExecutionLog.Start(tenantId, rule, leadId, "Ada Lovelace",
                    WorkflowTrigger.StatusChange, corr, "job", 1, startedAt);

                var step = WorkflowExecutionStep.Start(tenantId, log.Id, 1,
                    WorkflowStepKind.Condition, null, startedAt);
                step.Succeed("Condition matched.", startedAt);
                log.AddStep(step);
                db.WorkflowExecutionSteps.Add(step);
                log.CompleteFromSteps(startedAt.AddMilliseconds(10));

                db.WorkflowExecutionLogs.Add(log);
            }
            await db.SaveChangesAsync();
        }

        var clock = new FixedClock(new DateTimeOffset(now, TimeSpan.Zero));
        await BuildJob(tenantId, connStr, _factory, clock).ExecuteAsync(CancellationToken.None);

        await using var check = _factory.CreateForTenant(connStr, tenantId);
        var remaining = await check.WorkflowExecutionLogs.ToListAsync();

        Assert.Equal("corr-recent", Assert.Single(remaining).CorrelationId);
        // Steps go with their parent — an orphaned step row is invisible but still grows.
        Assert.Equal(1, await check.WorkflowExecutionSteps.CountAsync());
    }

    [Fact]
    public async Task RetentionDisabled_DeletesNothingButStillReconciles()
    {
        var tenantId = Guid.NewGuid();
        var (connStr, rule, leadId) = await SetupAsync(tenantId, "noretain");
        var now = new DateTime(2026, 9, 11, 12, 0, 0, DateTimeKind.Utc);

        await using (var db = _factory.CreateForTenant(connStr, tenantId))
        {
            var ancient = WorkflowExecutionLog.Start(tenantId, rule, leadId, "Ada Lovelace",
                WorkflowTrigger.StatusChange, "corr-ancient", "job", 1, now.AddDays(-400));
            ancient.CompleteFromSteps(now.AddDays(-400).AddMilliseconds(5));
            db.WorkflowExecutionLogs.Add(ancient);

            var stuck = WorkflowExecutionLog.Start(tenantId, rule, leadId, "Ada Lovelace",
                WorkflowTrigger.StatusChange, "corr-stuck", "job", 1, now.AddHours(-5));
            db.WorkflowExecutionLogs.Add(stuck);

            await db.SaveChangesAsync();
        }

        var clock = new FixedClock(new DateTimeOffset(now, TimeSpan.Zero));
        var options = new WorkflowExecutionLogOptions { RetentionEnabled = false };
        await BuildJob(tenantId, connStr, _factory, clock, options).ExecuteAsync(CancellationToken.None);

        await using var check = _factory.CreateForTenant(connStr, tenantId);

        // Nothing deleted — that is the point of the switch.
        Assert.Equal(2, await check.WorkflowExecutionLogs.CountAsync());
        // But a stuck row is still reconciled: leaving it Running forever is a correctness
        // problem, not a capacity one, so the switch does not govern it.
        Assert.Equal(WorkflowExecutionStatus.Abandoned,
            (await check.WorkflowExecutionLogs.SingleAsync(l => l.CorrelationId == "corr-stuck")).Status);
    }

    [Fact]
    public async Task OldRunningRow_IsReconciledRatherThanDeletedMidFlight()
    {
        var tenantId = Guid.NewGuid();
        var (connStr, rule, leadId) = await SetupAsync(tenantId, "oldrunning");
        var now = new DateTime(2026, 9, 11, 12, 0, 0, DateTimeKind.Utc);
        Guid id;

        await using (var db = _factory.CreateForTenant(connStr, tenantId))
        {
            // Both stale and past retention.
            var old = WorkflowExecutionLog.Start(tenantId, rule, leadId, "Ada Lovelace",
                WorkflowTrigger.StatusChange, "corr-old-running", "job", 1, now.AddDays(-200));
            db.WorkflowExecutionLogs.Add(old);
            await db.SaveChangesAsync();
            id = old.Id;
        }

        var clock = new FixedClock(new DateTimeOffset(now, TimeSpan.Zero));
        await BuildJob(tenantId, connStr, _factory, clock).ExecuteAsync(CancellationToken.None);

        await using var check = _factory.CreateForTenant(connStr, tenantId);
        var row = await check.WorkflowExecutionLogs.SingleOrDefaultAsync(l => l.Id == id);

        // Reconciliation gives it a truthful final status, and retention holds a
        // just-reconciled row back for one cycle — so the Abandoned state is actually
        // observable rather than being created and deleted in the same pass.
        Assert.NotNull(row);
        Assert.Equal(WorkflowExecutionStatus.Abandoned, row!.Status);
    }
}
