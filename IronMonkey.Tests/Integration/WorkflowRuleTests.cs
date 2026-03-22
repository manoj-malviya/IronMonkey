using IronMonkey.Data;
using IronMonkey.Tests.Fixtures;
using Xunit;

namespace IronMonkey.Tests.Integration;

// PIPE-04: Tenant can define workflow rules: triggers, conditions, and auto-actions
[Collection("Integration")]
public class WorkflowRuleTests(PostgreSqlFixture fixture) : IClassFixture<PostgreSqlFixture>
{
    private readonly TenantDbContextFactory _factory = new();

    [Fact(Skip = "Stub: implement in 04-06")]
    public async Task CreateWorkflowRule_PersistsWithTriggerConditionAndAction()
    {
        // TODO: POST rule with Trigger="status_change", ConditionJson (RulesEngine format),
        // ActionJson={"type":"notify","userId":...}, verify persisted with IsActive=true
        await Task.CompletedTask;
    }

    [Fact(Skip = "Stub: implement in 04-06")]
    public async Task WorkflowRule_StatusChangeTrigger_FiresNotificationAction()
    {
        // TODO: Create rule trigger=status_change -> action=notify, move lead to new stage,
        // run WorkflowRuleEvaluationJob, verify Notification created for target user
        await Task.CompletedTask;
    }

    [Fact(Skip = "Stub: implement in 04-06")]
    public async Task WorkflowRule_IsActive_False_DoesNotFire()
    {
        // TODO: Create disabled rule (IsActive=false), trigger condition, run job,
        // verify no Notification created
        await Task.CompletedTask;
    }

    [Fact(Skip = "Stub: implement in 04-06")]
    public async Task WorkflowRule_TimeElapsedTrigger_FiresViaHangfireJob()
    {
        // TODO: Create rule trigger=time_elapsed (1 hour), manually invoke TimeElapsedRuleScanJob,
        // verify action fires for qualifying leads
        await Task.CompletedTask;
    }

    [Fact(Skip = "Stub: implement in 04-06")]
    public async Task ListWorkflowRules_ReturnsAllRulesWithIsActiveStatus()
    {
        // TODO: Create 2 rules (1 active, 1 inactive), GET /api/workflow-rules,
        // verify both returned with correct IsActive
        await Task.CompletedTask;
    }
}
