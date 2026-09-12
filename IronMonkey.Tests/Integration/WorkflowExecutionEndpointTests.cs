using IronMonkey.ApiService.Common.Auth;
using IronMonkey.ApiService.Features.Leads.Workflow.Execution;
using IronMonkey.Data;
using IronMonkey.Data.Entities;
using IronMonkey.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace IronMonkey.Tests.Integration;

/// <summary>
/// The list and detail endpoints, driven through their real handlers so the filtering,
/// paging and tenancy under test are the ones that ship.
/// </summary>
[Collection("Integration")]
public class WorkflowExecutionEndpointTests(PostgreSqlFixture fixture) : IClassFixture<PostgreSqlFixture>
{
    private readonly TenantDbContextFactory _factory = new();

    private sealed class FixedTenantService(Guid tenantId, string connectionString) : ITenantService
    {
        public Guid GetCurrentTenantId() => tenantId;
        public Task<string> GetConnectionStringAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(connectionString);
    }

    private static readonly IOptions<WorkflowExecutionLogOptions> Options =
        Microsoft.Extensions.Options.Options.Create(new WorkflowExecutionLogOptions());

    private async Task<(string ConnectionString, Guid RuleId, Guid LeadId)> SetupAsync(
        Guid tenantId, string label)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var connectionString = fixture.ConnectionString.Replace("ironmonkey_test", $"wfe_{label}_{suffix}");

        await using var db = _factory.CreateForTenant(connectionString, tenantId);
        await db.Database.MigrateAsync();

        var stage = PipelineStage.Create(tenantId, "New", 1);
        db.PipelineStages.Add(stage);
        var lead = Lead.Create(tenantId, "Ada", "Lovelace", "555-0001", "ada@t.com", LeadSource.Manual, stage.Id);
        db.Leads.Add(lead);
        var rule = WorkflowRule.Create(tenantId, "Test rule", WorkflowTrigger.StatusChange, "{}", "{}");
        db.WorkflowRules.Add(rule);
        await db.SaveChangesAsync();

        return (connectionString, rule.Id, lead.Id);
    }

    /// <summary>Writes a finished execution row directly — the endpoints are what is under test.</summary>
    private static WorkflowExecutionLog AddRun(
        TenantDbContext db, Guid tenantId, WorkflowRule rule, Guid leadId,
        WorkflowExecutionStatus status, DateTime startedAt,
        string correlationId, int attempt = 1, string? actionType = "webhook")
    {
        var log = WorkflowExecutionLog.Start(tenantId, rule, leadId, "Ada Lovelace",
            rule.Trigger, correlationId, "job-1", attempt, startedAt);

        var sequence = 1;
        var condition = WorkflowExecutionStep.Start(tenantId, log.Id, sequence++,
            WorkflowStepKind.Condition, null, startedAt);

        if (status == WorkflowExecutionStatus.Skipped)
        {
            condition.Skip("Condition did not match.", startedAt);
            log.AddStep(condition);
            db.WorkflowExecutionSteps.Add(condition);
            log.RecordConditionResult(true, false);
            log.CompleteSkipped("Condition did not match.", startedAt.AddMilliseconds(5));
        }
        else
        {
            condition.Succeed("Condition matched.", startedAt);
            log.AddStep(condition);
            db.WorkflowExecutionSteps.Add(condition);
            log.RecordConditionResult(true, true);

            var action = WorkflowExecutionStep.Start(tenantId, log.Id, sequence,
                WorkflowStepKind.Action, actionType, startedAt);

            if (status == WorkflowExecutionStatus.Failed)
                action.Fail(WorkflowErrorCategory.WebhookTimeout, "Webhook did not respond in time.", startedAt);
            else
                action.Succeed("Webhook returned HTTP 200.", startedAt);

            log.AddStep(action);
            db.WorkflowExecutionSteps.Add(action);
            log.CompleteFromSteps(startedAt.AddMilliseconds(20));
        }

        db.WorkflowExecutionLogs.Add(log);
        return log;
    }

    private static Task<Microsoft.AspNetCore.Http.HttpResults.Ok<ListWorkflowExecutionsEndpoint.ExecutionPage>> List(
        Guid tenantId, string connectionString, TenantDbContextFactory factory,
        DateTime? from = null, DateTime? to = null, string? status = null, Guid? ruleId = null,
        string? trigger = null, Guid? leadId = null, string? actionType = null,
        int? page = null, int? pageSize = null)
        => ListWorkflowExecutionsEndpoint.Handle(
            from, to, status, ruleId, trigger, leadId, actionType, page, pageSize,
            new FixedTenantService(tenantId, connectionString), factory, Options, CancellationToken.None);

    [Fact]
    public async Task List_ReturnsNewestFirstWithAnHonestTotal()
    {
        var tenantId = Guid.NewGuid();
        var (connStr, _, leadId) = await SetupAsync(tenantId, "order");

        await using (var db = _factory.CreateForTenant(connStr, tenantId))
        {
            var rule = await db.WorkflowRules.FirstAsync();
            var baseTime = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);
            for (var i = 0; i < 30; i++)
            {
                AddRun(db, tenantId, rule, leadId, WorkflowExecutionStatus.Succeeded,
                    baseTime.AddMinutes(i), $"corr-{i}");
            }
            await db.SaveChangesAsync();
        }

        var result = await List(tenantId, connStr, _factory, pageSize: 10);
        var page = result.Value!;

        Assert.Equal(10, page.Items.Count);
        // Counted before paging, so "1–10 of 30" is honest.
        Assert.Equal(30, page.TotalCount);
        Assert.Equal(3, page.TotalPages);

        // Newest first — the most recent run is the one an Admin came to see.
        Assert.True(page.Items[0].StartedAt > page.Items[^1].StartedAt);
        Assert.Equal(page.Items.OrderByDescending(i => i.StartedAt).Select(i => i.Id), page.Items.Select(i => i.Id));
    }

    [Fact]
    public async Task List_PagePastTheEnd_ClampsToTheLastRealPage()
    {
        var tenantId = Guid.NewGuid();
        var (connStr, _, leadId) = await SetupAsync(tenantId, "clamp");

        await using (var db = _factory.CreateForTenant(connStr, tenantId))
        {
            var rule = await db.WorkflowRules.FirstAsync();
            for (var i = 0; i < 5; i++)
                AddRun(db, tenantId, rule, leadId, WorkflowExecutionStatus.Succeeded,
                    DateTime.UtcNow.AddMinutes(-i), $"corr-{i}");
            await db.SaveChangesAsync();
        }

        // An empty list here would read as "no runs" rather than "that page is past the end".
        var result = await List(tenantId, connStr, _factory, page: 99, pageSize: 2);

        Assert.Equal(3, result.Value!.Page);
        Assert.NotEmpty(result.Value!.Items);
    }

    [Fact]
    public async Task List_FiltersByStatus()
    {
        var tenantId = Guid.NewGuid();
        var (connStr, _, leadId) = await SetupAsync(tenantId, "status");

        await using (var db = _factory.CreateForTenant(connStr, tenantId))
        {
            var rule = await db.WorkflowRules.FirstAsync();
            var now = DateTime.UtcNow;
            AddRun(db, tenantId, rule, leadId, WorkflowExecutionStatus.Succeeded, now, "c-ok");
            AddRun(db, tenantId, rule, leadId, WorkflowExecutionStatus.Failed, now.AddMinutes(-1), "c-fail");
            AddRun(db, tenantId, rule, leadId, WorkflowExecutionStatus.Skipped, now.AddMinutes(-2), "c-skip");
            await db.SaveChangesAsync();
        }

        var failed = await List(tenantId, connStr, _factory, status: "Failed");
        Assert.Equal("Failed", Assert.Single(failed.Value!.Items).Status);

        // Skipped is filterable on its own — it is a legitimate outcome to go looking for,
        // not noise hidden behind "not failed".
        var skipped = await List(tenantId, connStr, _factory, status: "Skipped");
        Assert.Equal("Skipped", Assert.Single(skipped.Value!.Items).Status);
    }

    [Fact]
    public async Task List_FiltersByActionTypeThroughSteps()
    {
        var tenantId = Guid.NewGuid();
        var (connStr, _, leadId) = await SetupAsync(tenantId, "action");

        await using (var db = _factory.CreateForTenant(connStr, tenantId))
        {
            var rule = await db.WorkflowRules.FirstAsync();
            var now = DateTime.UtcNow;
            AddRun(db, tenantId, rule, leadId, WorkflowExecutionStatus.Succeeded, now, "c-web", actionType: "webhook");
            AddRun(db, tenantId, rule, leadId, WorkflowExecutionStatus.Succeeded, now.AddMinutes(-1), "c-not", actionType: "notify");
            await db.SaveChangesAsync();
        }

        var result = await List(tenantId, connStr, _factory, actionType: "webhook");

        var item = Assert.Single(result.Value!.Items);
        Assert.Contains("webhook", item.ActionTypes);
    }

    [Fact]
    public async Task List_DateRangeIsHalfOpenAndIncludesTheWholeFinalDay()
    {
        var tenantId = Guid.NewGuid();
        var (connStr, _, leadId) = await SetupAsync(tenantId, "dates");

        await using (var db = _factory.CreateForTenant(connStr, tenantId))
        {
            var rule = await db.WorkflowRules.FirstAsync();
            // Late on the last day of the range: an inclusive "<= end of day" bound expressed
            // as a .NET tick would drop this row.
            AddRun(db, tenantId, rule, leadId, WorkflowExecutionStatus.Succeeded,
                new DateTime(2026, 9, 10, 23, 59, 59, 999, DateTimeKind.Utc), "c-late");
            AddRun(db, tenantId, rule, leadId, WorkflowExecutionStatus.Succeeded,
                new DateTime(2026, 9, 11, 0, 0, 1, DateTimeKind.Utc), "c-next");
            await db.SaveChangesAsync();
        }

        var result = await List(tenantId, connStr, _factory,
            from: new DateTime(2026, 9, 10), to: new DateTime(2026, 9, 10));

        Assert.Single(result.Value!.Items);
        Assert.Equal(new DateTime(2026, 9, 10, 23, 59, 59, 999, DateTimeKind.Utc),
            result.Value!.Items[0].StartedAt);
    }

    [Fact]
    public async Task List_FiltersByRuleAndLead()
    {
        var tenantId = Guid.NewGuid();
        var (connStr, ruleId, leadId) = await SetupAsync(tenantId, "rulelead");

        await using (var db = _factory.CreateForTenant(connStr, tenantId))
        {
            var rule = await db.WorkflowRules.FirstAsync();
            var other = WorkflowRule.Create(tenantId, "Other rule", WorkflowTrigger.FieldChange, "{}", "{}");
            db.WorkflowRules.Add(other);

            var now = DateTime.UtcNow;
            AddRun(db, tenantId, rule, leadId, WorkflowExecutionStatus.Succeeded, now, "c-a");
            AddRun(db, tenantId, other, Guid.NewGuid(), WorkflowExecutionStatus.Succeeded, now.AddMinutes(-1), "c-b");
            await db.SaveChangesAsync();
        }

        var byRule = await List(tenantId, connStr, _factory, ruleId: ruleId);
        Assert.Equal(ruleId, Assert.Single(byRule.Value!.Items).WorkflowRuleId);

        var byLead = await List(tenantId, connStr, _factory, leadId: leadId);
        Assert.Equal(leadId, Assert.Single(byLead.Value!.Items).LeadId);
    }

    [Fact]
    public async Task Detail_ReturnsTheOrderedTimeline()
    {
        var tenantId = Guid.NewGuid();
        var (connStr, _, leadId) = await SetupAsync(tenantId, "detail");
        Guid executionId;

        await using (var db = _factory.CreateForTenant(connStr, tenantId))
        {
            var rule = await db.WorkflowRules.FirstAsync();
            var log = AddRun(db, tenantId, rule, leadId, WorkflowExecutionStatus.Failed,
                DateTime.UtcNow, "c-detail");
            await db.SaveChangesAsync();
            executionId = log.Id;
        }

        var result = await GetWorkflowExecutionEndpoint.Handle(
            executionId, new FixedTenantService(tenantId, connStr), _factory, CancellationToken.None);

        var detail = Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.Ok<GetWorkflowExecutionEndpoint.ExecutionDetail>>(result.Result);

        Assert.Equal("Failed", detail.Value!.Status);
        Assert.Equal([1, 2], detail.Value!.Steps.Select(s => s.Sequence));
        Assert.Equal("Condition", detail.Value!.Steps[0].Kind);
        Assert.Equal("Action", detail.Value!.Steps[1].Kind);
        Assert.Equal("webhook", detail.Value!.Steps[1].ActionType);
        Assert.NotEmpty(detail.Value!.CorrelationId);
    }

    [Fact]
    public async Task Detail_LinksTheOtherAttemptsOfTheSameTrigger()
    {
        var tenantId = Guid.NewGuid();
        var (connStr, _, leadId) = await SetupAsync(tenantId, "attempts");
        Guid secondAttemptId;

        await using (var db = _factory.CreateForTenant(connStr, tenantId))
        {
            var rule = await db.WorkflowRules.FirstAsync();
            var now = DateTime.UtcNow;
            AddRun(db, tenantId, rule, leadId, WorkflowExecutionStatus.Failed, now.AddMinutes(-1), "corr-shared", attempt: 1);
            var second = AddRun(db, tenantId, rule, leadId, WorkflowExecutionStatus.Succeeded, now, "corr-shared", attempt: 2);
            await db.SaveChangesAsync();
            secondAttemptId = second.Id;
        }

        var result = await GetWorkflowExecutionEndpoint.Handle(
            secondAttemptId, new FixedTenantService(tenantId, connStr), _factory, CancellationToken.None);

        var detail = Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.Ok<GetWorkflowExecutionEndpoint.ExecutionDetail>>(result.Result);

        // The retry is readable as a retry rather than as an unrelated run.
        Assert.Equal(2, detail.Value!.Attempt);
        var related = Assert.Single(detail.Value!.RelatedAttempts);
        Assert.Equal(1, related.Attempt);
        Assert.Equal("Failed", related.Status);
    }

    [Fact]
    public async Task Detail_ForAnotherTenantsExecution_Is404AndSaysNothingElse()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var (connStr, _, leadId) = await SetupAsync(tenantA, "isolation");
        Guid executionId;

        await using (var db = _factory.CreateForTenant(connStr, tenantA))
        {
            var rule = await db.WorkflowRules.FirstAsync();
            var log = AddRun(db, tenantA, rule, leadId, WorkflowExecutionStatus.Succeeded, DateTime.UtcNow, "c-iso");
            await db.SaveChangesAsync();
            executionId = log.Id;
        }

        // Same connection string, different tenant claim: the query filter must hide it, and
        // the response must be the same bare 404 an unknown id returns — anything richer would
        // confirm to a caller that the execution exists somewhere.
        var result = await GetWorkflowExecutionEndpoint.Handle(
            executionId, new FixedTenantService(tenantB, connStr), _factory, CancellationToken.None);

        Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.NotFound>(result.Result);

        var unknown = await GetWorkflowExecutionEndpoint.Handle(
            Guid.NewGuid(), new FixedTenantService(tenantA, connStr), _factory, CancellationToken.None);

        Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.NotFound>(unknown.Result);
    }

    [Fact]
    public async Task List_ForAnotherTenant_ReturnsNothing()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var (connStr, _, leadId) = await SetupAsync(tenantA, "listiso");

        await using (var db = _factory.CreateForTenant(connStr, tenantA))
        {
            var rule = await db.WorkflowRules.FirstAsync();
            AddRun(db, tenantA, rule, leadId, WorkflowExecutionStatus.Succeeded, DateTime.UtcNow, "c-iso2");
            await db.SaveChangesAsync();
        }

        var result = await List(tenantB, connStr, _factory);

        Assert.Empty(result.Value!.Items);
        Assert.Equal(0, result.Value!.TotalCount);
    }

    [Fact]
    public async Task List_ReportsRetentionSoAnEmptyOlderPageIsExplainable()
    {
        var tenantId = Guid.NewGuid();
        var (connStr, _, leadId) = await SetupAsync(tenantId, "retention");
        var oldest = new DateTime(2026, 8, 1, 9, 0, 0, DateTimeKind.Utc);

        await using (var db = _factory.CreateForTenant(connStr, tenantId))
        {
            var rule = await db.WorkflowRules.FirstAsync();
            AddRun(db, tenantId, rule, leadId, WorkflowExecutionStatus.Succeeded, oldest, "c-old");
            AddRun(db, tenantId, rule, leadId, WorkflowExecutionStatus.Succeeded, oldest.AddDays(5), "c-new");
            await db.SaveChangesAsync();
        }

        var result = await List(tenantId, connStr, _factory);

        Assert.Equal(oldest, result.Value!.OldestRetainedAt);
        Assert.Equal(90, result.Value!.RetentionDays);
    }
}
