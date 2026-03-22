using IronMonkey.ApiService.Features.Leads.Workflow.Rules;
using IronMonkey.ApiService.Notifications;
using IronMonkey.Data;
using IronMonkey.Data.Entities;
using IronMonkey.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace IronMonkey.Tests.Integration;

// PIPE-04: Tenant can define workflow rules: triggers, conditions, and auto-actions
[Collection("Integration")]
public class WorkflowRuleTests(PostgreSqlFixture fixture) : IClassFixture<PostgreSqlFixture>
{
    private readonly TenantDbContextFactory _factory = new();

    private async Task<(string connStr, Guid stageId, Guid leadId)> SetupDbAsync(Guid tenantId)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var connStr = fixture.ConnectionString.Replace("ironmonkey_test", $"wf_{suffix}");
        await using var db = _factory.CreateForTenant(connStr, tenantId);
        await db.Database.MigrateAsync();
        var stage = PipelineStage.Create(tenantId, "New", 1);
        db.PipelineStages.Add(stage);
        var lead = Lead.Create(tenantId, "Test", "Lead", "555-0000", "wf@test.com", LeadSource.Api, stage.Id);
        db.Leads.Add(lead);
        await db.SaveChangesAsync();
        return (connStr, stage.Id, lead.Id);
    }

    [Fact]
    public async Task CreateWorkflowRule_PersistsWithTriggerConditionAndAction()
    {
        var tenantId = Guid.NewGuid();
        var (connStr, _, _) = await SetupDbAsync(tenantId);
        await using var db = _factory.CreateForTenant(connStr, tenantId);

        var userId = Guid.NewGuid();
        var rule = WorkflowRule.Create(tenantId, "Notify on status change",
            WorkflowTrigger.StatusChange,
            "{\"always\":true}",
            $"{{\"type\":\"notify\",\"userId\":\"{userId}\",\"message\":\"Lead moved\"}}");
        db.WorkflowRules.Add(rule);
        await db.SaveChangesAsync();

        var saved = await db.WorkflowRules.FirstAsync(r => r.Id == rule.Id);
        Assert.Equal(WorkflowTrigger.StatusChange, saved.Trigger);
        Assert.True(saved.IsActive);
        Assert.Contains("notify", saved.ActionJson);
    }

    [Fact]
    public async Task WorkflowRule_StatusChangeTrigger_FiresNotificationAction()
    {
        var tenantId = Guid.NewGuid();
        var (connStr, _, leadId) = await SetupDbAsync(tenantId);
        await using var db = _factory.CreateForTenant(connStr, tenantId);

        var recipientId = Guid.NewGuid();
        var rule = WorkflowRule.Create(tenantId, "Notify",
            WorkflowTrigger.StatusChange,
            "{\"always\":true}",
            $"{{\"type\":\"notify\",\"userId\":\"{recipientId}\",\"message\":\"Stage changed\"}}");
        db.WorkflowRules.Add(rule);
        await db.SaveChangesAsync();

        var notifyMock = new Mock<INotificationService>();
        var engine = new WorkflowRuleEngine(notifyMock.Object, NullLogger<WorkflowRuleEngine>.Instance);

        var lead = await db.Leads.FirstAsync(l => l.Id == leadId);
        await engine.EvaluateAsync(tenantId, lead, WorkflowTrigger.StatusChange, db, CancellationToken.None);

        notifyMock.Verify(n => n.CreateAsync(db, tenantId, recipientId, "Stage changed", leadId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task WorkflowRule_IsActive_False_DoesNotFire()
    {
        var tenantId = Guid.NewGuid();
        var (connStr, _, leadId) = await SetupDbAsync(tenantId);
        await using var db = _factory.CreateForTenant(connStr, tenantId);

        var recipientId = Guid.NewGuid();
        var rule = WorkflowRule.Create(tenantId, "Inactive rule",
            WorkflowTrigger.StatusChange,
            "{\"always\":true}",
            $"{{\"type\":\"notify\",\"userId\":\"{recipientId}\",\"message\":\"Should not fire\"}}");
        rule.SetActive(false);  // Disable rule (D-12)
        db.WorkflowRules.Add(rule);
        await db.SaveChangesAsync();

        var notifyMock = new Mock<INotificationService>();
        var engine = new WorkflowRuleEngine(notifyMock.Object, NullLogger<WorkflowRuleEngine>.Instance);

        var lead = await db.Leads.FirstAsync(l => l.Id == leadId);
        await engine.EvaluateAsync(tenantId, lead, WorkflowTrigger.StatusChange, db, CancellationToken.None);

        notifyMock.Verify(n => n.CreateAsync(It.IsAny<TenantDbContext>(), It.IsAny<Guid>(), It.IsAny<Guid>(),
            It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ListWorkflowRules_ReturnsAllRulesWithIsActiveStatus()
    {
        var tenantId = Guid.NewGuid();
        var (connStr, _, _) = await SetupDbAsync(tenantId);
        await using var db = _factory.CreateForTenant(connStr, tenantId);

        var rule1 = WorkflowRule.Create(tenantId, "Active Rule", WorkflowTrigger.StatusChange,
            "{}", "{\"type\":\"notify\",\"userId\":\"00000000-0000-0000-0000-000000000001\",\"message\":\"hi\"}");
        var rule2 = WorkflowRule.Create(tenantId, "Inactive Rule", WorkflowTrigger.FieldChange,
            "{}", "{\"type\":\"notify\",\"userId\":\"00000000-0000-0000-0000-000000000001\",\"message\":\"hi\"}");
        rule2.SetActive(false);
        db.WorkflowRules.AddRange(rule1, rule2);
        await db.SaveChangesAsync();

        var rules = await db.WorkflowRules.ToListAsync();
        Assert.Equal(2, rules.Count);
        Assert.Single(rules.Where(r => r.IsActive));
        Assert.Single(rules.Where(r => !r.IsActive));
    }
}
