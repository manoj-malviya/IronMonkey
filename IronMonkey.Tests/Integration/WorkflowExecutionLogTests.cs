using System.Net;
using IronMonkey.ApiService.Features.Leads.Workflow.Execution;
using IronMonkey.ApiService.Features.Leads.Workflow.Rules;
using IronMonkey.ApiService.Notifications;
using IronMonkey.Data;
using IronMonkey.Data.Entities;
using IronMonkey.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;
using Xunit;

namespace IronMonkey.Tests.Integration;

/// <summary>
/// Covers the acceptance criteria for tenant-visible workflow execution history.
///
/// These drive the real <see cref="WorkflowRuleEngine"/> against a real tenant database, so
/// the recording under test is the recording that ships — the engine decides the outcome and
/// the recorder persists it, rather than the test asserting against a re-implementation.
/// </summary>
[Collection("Integration")]
public class WorkflowExecutionLogTests(PostgreSqlFixture fixture) : IClassFixture<PostgreSqlFixture>
{
    private readonly TenantDbContextFactory _factory = new();
    // The real clock: none of these assert on a specific instant, only on ordering and on
    // whether a timestamp was written at all.
    private readonly TimeProvider _time = TimeProvider.System;

    private async Task<(string connStr, Guid stageId, Guid leadId)> SetupDbAsync(Guid tenantId, string? leadEmail = "wf@test.com")
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var connStr = fixture.ConnectionString.Replace("ironmonkey_test", $"wfx_{suffix}");
        await using var db = _factory.CreateForTenant(connStr, tenantId);
        await db.Database.MigrateAsync();

        var stage = PipelineStage.Create(tenantId, "New", 1);
        db.PipelineStages.Add(stage);
        var lead = Lead.Create(tenantId, "Test", "Lead", "555-0000", leadEmail ?? "", LeadSource.Api, stage.Id);
        db.Leads.Add(lead);
        await db.SaveChangesAsync();

        return (connStr, stage.Id, lead.Id);
    }

    private WorkflowRuleEngine BuildEngine(HttpClient? webhookClient = null, Mock<INotificationService>? notify = null)
    {
        var factoryMock = new Mock<IHttpClientFactory>();
        factoryMock.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(webhookClient ?? new HttpClient());

        var recorder = new WorkflowExecutionRecorder(
            NullLogger<WorkflowExecutionRecorder>.Instance, _time);

        return new WorkflowRuleEngine(
            (notify ?? new Mock<INotificationService>()).Object,
            factoryMock.Object,
            NullLogger<WorkflowRuleEngine>.Instance,
            recorder);
    }

    private static WorkflowRule AddRule(TenantDbContext db, Guid tenantId, string name,
        string conditionJson, string actionJson, WorkflowTrigger trigger = WorkflowTrigger.StatusChange)
    {
        var rule = WorkflowRule.Create(tenantId, name, trigger, conditionJson, actionJson);
        db.WorkflowRules.Add(rule);
        return rule;
    }

    private static WorkflowExecutionContext ContextFor(Guid tenantId, Guid leadId, int attempt = 1, string? jobId = "job-1")
        => new(tenantId, WorkflowExecutionContext.CorrelationFor(leadId, WorkflowTrigger.StatusChange, jobId), jobId, attempt);

    private static async Task<WorkflowExecutionLog> SingleRunAsync(TenantDbContext db) =>
        await db.WorkflowExecutionLogs.Include(l => l.Steps).SingleAsync();

    /// <summary>An HttpClient whose every request returns a fixed status, or throws.</summary>
    private static HttpClient StubHttp(HttpStatusCode status) => StubHttp(_ => new HttpResponseMessage(status));

    private static HttpClient StubHttp(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync((HttpRequestMessage req, CancellationToken _) => respond(req));
        return new HttpClient(handler.Object);
    }

    private static HttpClient ThrowingHttp(Exception ex)
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(ex);
        return new HttpClient(handler.Object);
    }

    // ── Acceptance: a successful run is durably recorded ──────────────────────

    [Fact]
    public async Task SuccessfulRun_RecordsTriggerRuleLeadConditionAndActionOutcome()
    {
        var tenantId = Guid.NewGuid();
        var (connStr, _, leadId) = await SetupDbAsync(tenantId);
        await using var db = _factory.CreateForTenant(connStr, tenantId);

        var rule = AddRule(db, tenantId, "Notify owner", "{\"always\":true}",
            $"{{\"type\":\"notify\",\"userId\":\"{Guid.NewGuid()}\",\"message\":\"Stage changed\"}}");
        await db.SaveChangesAsync();

        var lead = await db.Leads.FirstAsync(l => l.Id == leadId);
        await BuildEngine().EvaluateAsync(tenantId, lead, WorkflowTrigger.StatusChange, db,
            CancellationToken.None, ContextFor(tenantId, leadId));

        var run = await SingleRunAsync(db);

        Assert.Equal(WorkflowExecutionStatus.Succeeded, run.Status);
        Assert.Equal(rule.Id, run.WorkflowRuleId);
        Assert.Equal("Notify owner", run.WorkflowRuleName);
        Assert.Equal(leadId, run.LeadId);
        Assert.Equal("Test Lead", run.LeadName);
        Assert.Equal(WorkflowTrigger.StatusChange, run.Trigger);
        Assert.True(run.ConditionEvaluated);
        Assert.True(run.ConditionMatched);
        Assert.NotNull(run.CompletedAt);
        Assert.NotNull(run.DurationMs);
        Assert.Equal(WorkflowErrorCategory.None, run.ErrorCategory);

        // Timeline: condition, then the action.
        var steps = run.Steps.OrderBy(s => s.Sequence).ToList();
        Assert.Equal(2, steps.Count);
        Assert.Equal(WorkflowStepKind.Condition, steps[0].Kind);
        Assert.Equal(WorkflowStepStatus.Succeeded, steps[0].Status);
        Assert.Equal(WorkflowStepKind.Action, steps[1].Kind);
        Assert.Equal("notify", steps[1].ActionType);
        Assert.Equal(WorkflowStepStatus.Succeeded, steps[1].Status);
    }

    // ── Acceptance: a condition mismatch is a visible, intentional skip ────────

    [Fact]
    public async Task ConditionDoesNotMatch_IsRecordedAsSkippedNotFailed()
    {
        var tenantId = Guid.NewGuid();
        var (connStr, _, leadId) = await SetupDbAsync(tenantId);
        await using var db = _factory.CreateForTenant(connStr, tenantId);

        AddRule(db, tenantId, "Only Web leads", "{\"field\":\"Source\",\"equals\":\"WebForm\"}",
            $"{{\"type\":\"notify\",\"userId\":\"{Guid.NewGuid()}\",\"message\":\"hi\"}}");
        await db.SaveChangesAsync();

        var lead = await db.Leads.FirstAsync(l => l.Id == leadId); // Source is Api, not WebForm
        await BuildEngine().EvaluateAsync(tenantId, lead, WorkflowTrigger.StatusChange, db,
            CancellationToken.None, ContextFor(tenantId, leadId));

        var run = await SingleRunAsync(db);

        // The whole point: a narrow rule that did not apply must not read as broken.
        Assert.Equal(WorkflowExecutionStatus.Skipped, run.Status);
        Assert.Equal(WorkflowErrorCategory.None, run.ErrorCategory);
        Assert.Null(run.ErrorMessage);
        Assert.NotNull(run.SkipReason);
        Assert.Contains("did not match", run.SkipReason, StringComparison.OrdinalIgnoreCase);
        Assert.True(run.ConditionEvaluated);
        Assert.False(run.ConditionMatched);

        // No action was attempted, and the skip is visible in the timeline too.
        Assert.DoesNotContain(run.Steps, s => s.Kind == WorkflowStepKind.Action);
        Assert.Equal(WorkflowStepStatus.Skipped, Assert.Single(run.Steps).Status);
    }

    // Malformed condition/action JSON is covered in WorkflowConditionTests rather than here:
    // ConditionJson and ActionJson are jsonb columns, so Postgres rejects an invalid value on
    // insert and the case cannot be staged through EF. The engine still has to handle it —
    // the column type was added after the rules feature, so older rows and any raw-SQL writer
    // can still present one — which is what those unit tests pin.

    // ── Acceptance: webhook outcomes are distinguishable, without leaking secrets ──

    [Fact]
    public async Task WebhookSuccess_RecordsStatusCodeAndHost()
    {
        var tenantId = Guid.NewGuid();
        var (connStr, _, leadId) = await SetupDbAsync(tenantId);
        await using var db = _factory.CreateForTenant(connStr, tenantId);

        AddRule(db, tenantId, "Push to CRM", "{\"always\":true}",
            "{\"type\":\"webhook\",\"url\":\"https://hooks.example.com/inbound?token=s3cr3t\"}");
        await db.SaveChangesAsync();

        var lead = await db.Leads.FirstAsync(l => l.Id == leadId);
        await BuildEngine(StubHttp(HttpStatusCode.OK)).EvaluateAsync(
            tenantId, lead, WorkflowTrigger.StatusChange, db, CancellationToken.None,
            ContextFor(tenantId, leadId));

        var run = await SingleRunAsync(db);
        var step = run.Steps.Single(s => s.Kind == WorkflowStepKind.Action);

        Assert.Equal(WorkflowExecutionStatus.Succeeded, run.Status);
        Assert.Equal(200, step.HttpStatusCode);
        Assert.Equal("https://hooks.example.com", step.TargetHost);

        // The token in the configured URL must not have been persisted anywhere.
        Assert.DoesNotContain("s3cr3t", step.TargetHost);
        Assert.DoesNotContain("s3cr3t", step.Message ?? "");
    }

    [Fact]
    public async Task WebhookNonSuccess_IsFailedWithStatusCodeAndNoResponseBody()
    {
        var tenantId = Guid.NewGuid();
        var (connStr, _, leadId) = await SetupDbAsync(tenantId);
        await using var db = _factory.CreateForTenant(connStr, tenantId);

        AddRule(db, tenantId, "Push", "{\"always\":true}",
            "{\"type\":\"webhook\",\"url\":\"https://hooks.example.com/inbound\"}");
        await db.SaveChangesAsync();

        var body = "internal error: db password is hunter2";
        var client = StubHttp(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent(body)
        });

        var lead = await db.Leads.FirstAsync(l => l.Id == leadId);
        await BuildEngine(client).EvaluateAsync(tenantId, lead, WorkflowTrigger.StatusChange, db,
            CancellationToken.None, ContextFor(tenantId, leadId));

        var run = await SingleRunAsync(db);
        var step = run.Steps.Single(s => s.Kind == WorkflowStepKind.Action);

        Assert.Equal(WorkflowExecutionStatus.Failed, run.Status);
        Assert.Equal(WorkflowErrorCategory.WebhookNonSuccessResponse, run.ErrorCategory);
        Assert.Equal(500, step.HttpStatusCode);

        // The third party's response body is never stored: it is unbounded content of unknown
        // provenance that may echo the request's own credentials back.
        Assert.DoesNotContain("hunter2", step.Message ?? "");
    }

    [Fact]
    public async Task WebhookTimeout_IsItsOwnCategory()
    {
        var tenantId = Guid.NewGuid();
        var (connStr, _, leadId) = await SetupDbAsync(tenantId);
        await using var db = _factory.CreateForTenant(connStr, tenantId);

        AddRule(db, tenantId, "Push", "{\"always\":true}",
            "{\"type\":\"webhook\",\"url\":\"https://hooks.example.com/inbound\"}");
        await db.SaveChangesAsync();

        var lead = await db.Leads.FirstAsync(l => l.Id == leadId);
        await BuildEngine(ThrowingHttp(new TaskCanceledException("timed out"))).EvaluateAsync(
            tenantId, lead, WorkflowTrigger.StatusChange, db, CancellationToken.None,
            ContextFor(tenantId, leadId));

        var run = await SingleRunAsync(db);

        Assert.Equal(WorkflowErrorCategory.WebhookTimeout, run.ErrorCategory);
    }

    [Fact]
    public async Task WebhookNetworkFailure_IsItsOwnCategory()
    {
        var tenantId = Guid.NewGuid();
        var (connStr, _, leadId) = await SetupDbAsync(tenantId);
        await using var db = _factory.CreateForTenant(connStr, tenantId);

        AddRule(db, tenantId, "Push", "{\"always\":true}",
            "{\"type\":\"webhook\",\"url\":\"https://hooks.example.com/inbound\"}");
        await db.SaveChangesAsync();

        var lead = await db.Leads.FirstAsync(l => l.Id == leadId);
        await BuildEngine(ThrowingHttp(new HttpRequestException("no such host"))).EvaluateAsync(
            tenantId, lead, WorkflowTrigger.StatusChange, db, CancellationToken.None,
            ContextFor(tenantId, leadId));

        var run = await SingleRunAsync(db);

        Assert.Equal(WorkflowErrorCategory.WebhookNetworkFailure, run.ErrorCategory);
    }

    [Fact]
    public async Task InvalidWebhookUrl_IsFailedAndTheUrlIsNotEchoedBack()
    {
        var tenantId = Guid.NewGuid();
        var (connStr, _, leadId) = await SetupDbAsync(tenantId);
        await using var db = _factory.CreateForTenant(connStr, tenantId);

        AddRule(db, tenantId, "Bad URL", "{\"always\":true}",
            "{\"type\":\"webhook\",\"url\":\"ftp://internal-host/secret-path\"}");
        await db.SaveChangesAsync();

        var lead = await db.Leads.FirstAsync(l => l.Id == leadId);
        await BuildEngine().EvaluateAsync(tenantId, lead, WorkflowTrigger.StatusChange, db,
            CancellationToken.None, ContextFor(tenantId, leadId));

        var run = await SingleRunAsync(db);
        var step = run.Steps.Single(s => s.Kind == WorkflowStepKind.Action);

        Assert.Equal(WorkflowErrorCategory.InvalidWebhookUrl, run.ErrorCategory);
        // A "malformed" URL is frequently a well-formed one with a credential in it.
        Assert.DoesNotContain("secret-path", step.Message ?? "");
    }

    // ── Acceptance: email cases are distinguishable, recipient is redacted ─────

    [Fact]
    public async Task EmailNotConfigured_IsItsOwnCategoryWithMaskedRecipient()
    {
        var tenantId = Guid.NewGuid();
        var (connStr, _, leadId) = await SetupDbAsync(tenantId, leadEmail: "jane.doe@acme.com");
        await using var db = _factory.CreateForTenant(connStr, tenantId);

        AddRule(db, tenantId, "Welcome email", "{\"always\":true}",
            "{\"type\":\"email\",\"to\":\"lead\",\"subject\":\"Hi {{FirstName}}\",\"body\":\"Personal data here\"}");
        await db.SaveChangesAsync();

        var lead = await db.Leads.FirstAsync(l => l.Id == leadId);
        await BuildEngine().EvaluateAsync(tenantId, lead, WorkflowTrigger.StatusChange, db,
            CancellationToken.None, ContextFor(tenantId, leadId));

        var run = await SingleRunAsync(db);
        var step = run.Steps.Single(s => s.Kind == WorkflowStepKind.Action);

        Assert.Equal(WorkflowErrorCategory.EmailDeliveryNotConfigured, run.ErrorCategory);
        // Masked: domain kept so a misrouted rule is diagnosable, local part is not.
        Assert.Equal("j***@acme.com", step.RecipientRedacted);
        // The rendered body carries lead PII and is never persisted.
        Assert.DoesNotContain("Personal data here", step.Message ?? "");
    }

    [Fact]
    public async Task EmailWithNoRecipient_IsMissingRecipientCategory()
    {
        var tenantId = Guid.NewGuid();
        var (connStr, _, leadId) = await SetupDbAsync(tenantId, leadEmail: "");
        await using var db = _factory.CreateForTenant(connStr, tenantId);

        AddRule(db, tenantId, "Email the lead", "{\"always\":true}",
            "{\"type\":\"email\",\"to\":\"lead\",\"subject\":\"Hi\",\"body\":\"there\"}");
        await db.SaveChangesAsync();

        var lead = await db.Leads.FirstAsync(l => l.Id == leadId);
        await BuildEngine().EvaluateAsync(tenantId, lead, WorkflowTrigger.StatusChange, db,
            CancellationToken.None, ContextFor(tenantId, leadId));

        var run = await SingleRunAsync(db);

        Assert.Equal(WorkflowErrorCategory.MissingEmailRecipient, run.ErrorCategory);
    }

    [Fact]
    public async Task UnknownActionType_IsRecordedAsUnknownWithTheTypeSnapshot()
    {
        var tenantId = Guid.NewGuid();
        var (connStr, _, leadId) = await SetupDbAsync(tenantId);
        await using var db = _factory.CreateForTenant(connStr, tenantId);

        AddRule(db, tenantId, "Typo rule", "{\"always\":true}", "{\"type\":\"notifyy\"}");
        await db.SaveChangesAsync();

        var lead = await db.Leads.FirstAsync(l => l.Id == leadId);
        await BuildEngine().EvaluateAsync(tenantId, lead, WorkflowTrigger.StatusChange, db,
            CancellationToken.None, ContextFor(tenantId, leadId));

        var run = await SingleRunAsync(db);
        var step = run.Steps.Single(s => s.Kind == WorkflowStepKind.Action);

        Assert.Equal(WorkflowErrorCategory.UnknownActionType, run.ErrorCategory);
        // The unsupported value is snapshotted, which is why ActionType is a string.
        Assert.Equal("notifyy", step.ActionType);
    }

    [Fact]
    public async Task ActionMissingItsRequiredFields_IsInvalidConfigurationNotAnUnknownType()
    {
        // Valid JSON of the wrong shape is the failure an Admin actually writes: the type is
        // recognised, the field it needs is absent. It has its own category so the UI does not
        // tell them the action type is unsupported when it is not.
        var tenantId = Guid.NewGuid();
        var (connStr, _, leadId) = await SetupDbAsync(tenantId);
        await using var db = _factory.CreateForTenant(connStr, tenantId);

        AddRule(db, tenantId, "Assign to nobody", "{\"always\":true}", "{\"type\":\"assign\"}");
        await db.SaveChangesAsync();

        var lead = await db.Leads.FirstAsync(l => l.Id == leadId);
        await BuildEngine().EvaluateAsync(tenantId, lead, WorkflowTrigger.StatusChange, db,
            CancellationToken.None, ContextFor(tenantId, leadId));

        var run = await SingleRunAsync(db);

        Assert.Equal(WorkflowExecutionStatus.Failed, run.Status);
        Assert.Equal(WorkflowErrorCategory.InvalidActionConfiguration, run.ErrorCategory);
        Assert.Equal("assign", run.Steps.Single(s => s.Kind == WorkflowStepKind.Action).ActionType);
    }

    [Fact]
    public async Task AssignToUserThatDoesNotExist_IsReferencedRecordNotFound()
    {
        // AssignedToUserId has no FK, so a typo'd id would otherwise persist as an assignment
        // to nobody and be recorded as a success.
        var tenantId = Guid.NewGuid();
        var (connStr, _, leadId) = await SetupDbAsync(tenantId);
        await using var db = _factory.CreateForTenant(connStr, tenantId);

        AddRule(db, tenantId, "Assign", "{\"always\":true}",
            $"{{\"type\":\"assign\",\"userId\":\"{Guid.NewGuid()}\"}}");
        await db.SaveChangesAsync();

        var lead = await db.Leads.FirstAsync(l => l.Id == leadId);
        await BuildEngine().EvaluateAsync(tenantId, lead, WorkflowTrigger.StatusChange, db,
            CancellationToken.None, ContextFor(tenantId, leadId));

        var run = await SingleRunAsync(db);

        Assert.Equal(WorkflowErrorCategory.ReferencedRecordNotFound, run.ErrorCategory);
        // And the lead was genuinely left unassigned rather than pointed at a missing user.
        Assert.Null((await db.Leads.FirstAsync(l => l.Id == leadId)).AssignedToUserId);
    }

    // ── Acceptance: a failing rule does not stop other rules or the lead write ──

    [Fact]
    public async Task OneRuleFailing_DoesNotPreventOtherRulesAndBothAreRecorded()
    {
        var tenantId = Guid.NewGuid();
        var (connStr, _, leadId) = await SetupDbAsync(tenantId);
        await using var db = _factory.CreateForTenant(connStr, tenantId);

        AddRule(db, tenantId, "Failing webhook", "{\"always\":true}",
            "{\"type\":\"webhook\",\"url\":\"https://down.example.com/hook\"}");
        AddRule(db, tenantId, "Working notify", "{\"always\":true}",
            $"{{\"type\":\"notify\",\"userId\":\"{Guid.NewGuid()}\",\"message\":\"still ran\"}}");
        await db.SaveChangesAsync();

        var notify = new Mock<INotificationService>();
        var lead = await db.Leads.FirstAsync(l => l.Id == leadId);

        await BuildEngine(ThrowingHttp(new HttpRequestException("down")), notify).EvaluateAsync(
            tenantId, lead, WorkflowTrigger.StatusChange, db, CancellationToken.None,
            ContextFor(tenantId, leadId));

        var runs = await db.WorkflowExecutionLogs.OrderBy(l => l.StartedAt).ToListAsync();

        // Both rules were evaluated and both have their own row.
        Assert.Equal(2, runs.Count);
        Assert.Contains(runs, r => r.Status == WorkflowExecutionStatus.Failed);
        Assert.Contains(runs, r => r.Status == WorkflowExecutionStatus.Succeeded);

        // The second rule genuinely ran despite the first failing.
        notify.Verify(n => n.CreateAsync(It.IsAny<TenantDbContext>(), tenantId, It.IsAny<Guid>(),
            "still ran", leadId, It.IsAny<CancellationToken>()), Times.Once);

        // And the lead that triggered all of this is untouched and still readable.
        Assert.NotNull(await db.Leads.FirstOrDefaultAsync(l => l.Id == leadId));
    }

    // ── Acceptance: retries do not produce misleading duplicate runs ───────────

    [Fact]
    public async Task RetryOfSameAttempt_DoesNotWriteASecondRun()
    {
        var tenantId = Guid.NewGuid();
        var (connStr, _, leadId) = await SetupDbAsync(tenantId);
        await using var db = _factory.CreateForTenant(connStr, tenantId);

        AddRule(db, tenantId, "Notify", "{\"always\":true}",
            $"{{\"type\":\"notify\",\"userId\":\"{Guid.NewGuid()}\",\"message\":\"hi\"}}");
        await db.SaveChangesAsync();

        var engine = BuildEngine();
        var context = ContextFor(tenantId, leadId, attempt: 1);
        var lead = await db.Leads.FirstAsync(l => l.Id == leadId);

        // The same attempt delivered twice — a worker losing its lock and the job being
        // requeued. The second delivery must not add a second "success".
        await engine.EvaluateAsync(tenantId, lead, WorkflowTrigger.StatusChange, db, CancellationToken.None, context);
        await engine.EvaluateAsync(tenantId, lead, WorkflowTrigger.StatusChange, db, CancellationToken.None, context);

        Assert.Equal(1, await db.WorkflowExecutionLogs.CountAsync());
    }

    [Fact]
    public async Task HangfireRetry_LinksAttemptsUnderOneCorrelationId()
    {
        var tenantId = Guid.NewGuid();
        var (connStr, _, leadId) = await SetupDbAsync(tenantId);
        await using var db = _factory.CreateForTenant(connStr, tenantId);

        AddRule(db, tenantId, "Push", "{\"always\":true}",
            "{\"type\":\"webhook\",\"url\":\"https://hooks.example.com/hook\"}");
        await db.SaveChangesAsync();

        var lead = await db.Leads.FirstAsync(l => l.Id == leadId);

        // Attempt 1 fails, attempt 2 (the Hangfire retry, same job id) succeeds.
        await BuildEngine(ThrowingHttp(new HttpRequestException("down"))).EvaluateAsync(
            tenantId, lead, WorkflowTrigger.StatusChange, db, CancellationToken.None,
            ContextFor(tenantId, leadId, attempt: 1));

        await BuildEngine(StubHttp(HttpStatusCode.OK)).EvaluateAsync(
            tenantId, lead, WorkflowTrigger.StatusChange, db, CancellationToken.None,
            ContextFor(tenantId, leadId, attempt: 2));

        var runs = await db.WorkflowExecutionLogs.OrderBy(l => l.Attempt).ToListAsync();

        Assert.Equal(2, runs.Count);
        // Two rows, but one logical trigger — which is what makes the retry readable as a
        // retry rather than as two unrelated runs.
        Assert.Single(runs.Select(r => r.CorrelationId).Distinct());
        Assert.Equal([1, 2], runs.Select(r => r.Attempt));
        Assert.Equal(WorkflowExecutionStatus.Failed, runs[0].Status);
        Assert.Equal(WorkflowExecutionStatus.Succeeded, runs[1].Status);
    }

    // ── Acceptance: a stale Running row is recoverable ────────────────────────

    [Fact]
    public async Task RunningRow_IsMarkedAbandonedWithAnEndTime()
    {
        var tenantId = Guid.NewGuid();
        var (connStr, _, leadId) = await SetupDbAsync(tenantId);
        await using var db = _factory.CreateForTenant(connStr, tenantId);

        var rule = AddRule(db, tenantId, "Stuck", "{\"always\":true}", "{\"type\":\"notify\"}");
        await db.SaveChangesAsync();

        var lead = await db.Leads.FirstAsync(l => l.Id == leadId);
        var started = _time.GetUtcNow().UtcDateTime;

        // A row the worker never closed, exactly as a killed process leaves it.
        var stuck = WorkflowExecutionLog.Start(tenantId, rule, lead.Id, "Test Lead",
            WorkflowTrigger.StatusChange, "corr-stuck", "job-stuck", 1, started);
        db.WorkflowExecutionLogs.Add(stuck);
        await db.SaveChangesAsync();

        stuck.MarkAbandoned("Worker stopped.", started.AddHours(2));
        await db.SaveChangesAsync();

        var reloaded = await db.WorkflowExecutionLogs.SingleAsync(l => l.Id == stuck.Id);

        // Abandoned, not Failed: we do not know whether the actions ran.
        Assert.Equal(WorkflowExecutionStatus.Abandoned, reloaded.Status);
        // And it has an end time, so the UI never renders a run that appears still in flight.
        Assert.NotNull(reloaded.CompletedAt);
        Assert.NotNull(reloaded.DurationMs);
    }

    // ── Acceptance: logs are isolated per tenant ──────────────────────────────

    [Fact]
    public async Task ExecutionLogs_AreScopedToTheirOwnTenant()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var (connStr, _, leadId) = await SetupDbAsync(tenantA);

        await using (var db = _factory.CreateForTenant(connStr, tenantA))
        {
            AddRule(db, tenantA, "Notify", "{\"always\":true}",
                $"{{\"type\":\"notify\",\"userId\":\"{Guid.NewGuid()}\",\"message\":\"hi\"}}");
            await db.SaveChangesAsync();

            var lead = await db.Leads.FirstAsync(l => l.Id == leadId);
            await BuildEngine().EvaluateAsync(tenantA, lead, WorkflowTrigger.StatusChange, db,
                CancellationToken.None, ContextFor(tenantA, leadId));

            Assert.Equal(1, await db.WorkflowExecutionLogs.CountAsync());
        }

        // Same physical database, different tenant id: the global query filter must hide it.
        // In production the databases are separate too, so this is the inner of two defences.
        await using (var dbAsB = _factory.CreateForTenant(connStr, tenantB))
        {
            Assert.Equal(0, await dbAsB.WorkflowExecutionLogs.CountAsync());
            Assert.Equal(0, await dbAsB.WorkflowExecutionSteps.CountAsync());
        }
    }

    [Fact]
    public async Task RuleRenamedAfterTheRun_DoesNotRewriteHistory()
    {
        var tenantId = Guid.NewGuid();
        var (connStr, _, leadId) = await SetupDbAsync(tenantId);
        await using var db = _factory.CreateForTenant(connStr, tenantId);

        var rule = AddRule(db, tenantId, "Original name", "{\"always\":true}",
            $"{{\"type\":\"notify\",\"userId\":\"{Guid.NewGuid()}\",\"message\":\"hi\"}}");
        await db.SaveChangesAsync();

        var lead = await db.Leads.FirstAsync(l => l.Id == leadId);
        await BuildEngine().EvaluateAsync(tenantId, lead, WorkflowTrigger.StatusChange, db,
            CancellationToken.None, ContextFor(tenantId, leadId));

        rule.Update("Renamed later", rule.Trigger, rule.ConditionJson, rule.ActionJson);
        await db.SaveChangesAsync();

        var run = await SingleRunAsync(db);

        // The snapshot is why the row stays readable after a rename — or a deletion.
        Assert.Equal("Original name", run.WorkflowRuleName);
    }
}
