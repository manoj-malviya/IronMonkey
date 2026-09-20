using IronMonkey.ApiService.Features.Communications;
using IronMonkey.ApiService.Features.Communications.Providers;
using IronMonkey.ApiService.Features.Leads.Workflow.Execution;
using IronMonkey.ApiService.Features.Leads.Workflow.Rules;
using IronMonkey.ApiService.Notifications;
using IronMonkey.Data;
using IronMonkey.Data.Communications;
using IronMonkey.Data.Entities;
using IronMonkey.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace IronMonkey.Tests.Integration;

/// <summary>
/// The workflow engine's email action, now that it dispatches for real.
///
/// The action used to log and record itself as failed because delivery was not wired up.
/// These pin that it now produces an actual message row, that its failure semantics are
/// unchanged (a failed send does not roll back the lead write or stop other rules), and that a
/// workflow-triggered send is subject to the same consent gate as a manual one.
/// </summary>
[Collection("Integration")]
public class WorkflowEmailDispatchTests(PostgreSqlFixture fixture) : IClassFixture<PostgreSqlFixture>
{
    private readonly TenantDbContextFactory _factory = new();

    private sealed class CountingProvider(SendResult result) : IMessageProvider
    {
        public int SendCount { get; private set; }
        public MessageChannel Channel => MessageChannel.Email;
        public string Name => "Fake";
        public bool IsConfigured => true;
        public string FromAddress => "crm@example.com";

        public Task<SendResult> SendAsync(OutboundMessageRequest request, CancellationToken cancellationToken)
        {
            SendCount++;
            return Task.FromResult(result);
        }
    }

    private sealed class RecordingScheduler : IMessageSendScheduler
    {
        public List<Guid> Enqueued { get; } = [];

        /// <summary>Deferred sends, with the instant they were scheduled for.</summary>
        public List<(Guid MessageId, DateTime RunAtUtc)> Scheduled { get; } = [];

        public void Enqueue(Guid tenantId, Guid messageId) => Enqueued.Add(messageId);

        public void Schedule(Guid tenantId, Guid messageId, DateTime runAtUtc) =>
            Scheduled.Add((messageId, runAtUtc));
    }

    private async Task<(string ConnectionString, Guid LeadId)> SetupAsync(Guid tenantId, string leadEmail)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var connectionString = fixture.ConnectionString.Replace("ironmonkey_test", $"wfmail_{suffix}");

        await using var db = _factory.CreateForTenant(connectionString, tenantId);
        await db.Database.MigrateAsync();

        var stage = PipelineStage.Create(tenantId, "New", 1);
        db.PipelineStages.Add(stage);
        var lead = Lead.Create(tenantId, "Ada", "Lovelace", "555-0000", leadEmail, LeadSource.Api, stage.Id);
        db.Leads.Add(lead);
        await db.SaveChangesAsync();

        return (connectionString, lead.Id);
    }

    private static WorkflowRuleEngine BuildEngine(IMessageDispatcher dispatcher)
    {
        var httpFactory = new Mock<IHttpClientFactory>();
        httpFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(new HttpClient());

        return new WorkflowRuleEngine(
            new Mock<INotificationService>().Object,
            httpFactory.Object,
            NullLogger<WorkflowRuleEngine>.Instance,
            new WorkflowExecutionRecorder(NullLogger<WorkflowExecutionRecorder>.Instance, TimeProvider.System),
            dispatcher);
    }

    private static MessageDispatcher BuildDispatcher(IMessageSendScheduler scheduler, params IMessageProvider[] providers) =>
        new(new MessageProviderRegistry(providers),
            scheduler,
            new SuppressionService(NullLogger<SuppressionService>.Instance, TimeProvider.System),
            TimeProvider.System,
            NullLogger<MessageDispatcher>.Instance);

    private static WorkflowExecutionContext Context(Guid tenantId, Guid leadId) =>
        new(tenantId,
            WorkflowExecutionContext.CorrelationFor(leadId, WorkflowTrigger.StatusChange, null),
            null, 1);

    private const string EmailAction =
        """{"type":"email","to":"lead","subject":"Hello {{FirstName}}","body":"Hi {{FullName}}"}""";

    [Fact]
    public async Task An_email_rule_queues_a_real_message_against_the_lead()
    {
        var tenantId = Guid.NewGuid();
        var (connectionString, leadId) = await SetupAsync(tenantId, "ada@example.com");

        await using var db = _factory.CreateForTenant(connectionString, tenantId);

        var scheduler = new RecordingScheduler();
        var dispatcher = BuildDispatcher(scheduler, new CountingProvider(SendResult.Success("pid")));

        db.WorkflowRules.Add(WorkflowRule.Create(tenantId, "Welcome",
            WorkflowTrigger.StatusChange, """{"always":true}""", EmailAction));
        await db.SaveChangesAsync();

        var lead = await db.Leads.Include(l => l.Stage).FirstAsync(l => l.Id == leadId);

        await BuildEngine(dispatcher).EvaluateAsync(tenantId, lead, WorkflowTrigger.StatusChange, db,
            default, Context(tenantId, leadId));

        var message = await db.Messages.SingleAsync();

        Assert.Equal(MessageChannel.Email, message.Channel);
        Assert.Equal("ada@example.com", message.ToAddress);
        Assert.Equal(leadId, message.LeadId);
        Assert.Equal(MessageStatus.Queued, message.Status);

        // The rule's placeholders are interpolated before the message is queued.
        Assert.Equal("Hello Ada", message.Subject);
        Assert.Contains("Ada Lovelace", message.Body);

        // Attributed to the rule, not to a user — this was an automated send.
        Assert.NotNull(message.WorkflowRuleId);
        Assert.Null(message.SentByUserId);

        Assert.Single(scheduler.Enqueued);
    }

    [Fact]
    public async Task The_execution_history_records_the_email_action_as_succeeded()
    {
        var tenantId = Guid.NewGuid();
        var (connectionString, leadId) = await SetupAsync(tenantId, "ada@example.com");

        await using var db = _factory.CreateForTenant(connectionString, tenantId);

        var dispatcher = BuildDispatcher(new RecordingScheduler(), new CountingProvider(SendResult.Success("pid")));

        db.WorkflowRules.Add(WorkflowRule.Create(tenantId, "Welcome",
            WorkflowTrigger.StatusChange, """{"always":true}""", EmailAction));
        await db.SaveChangesAsync();

        var lead = await db.Leads.Include(l => l.Stage).FirstAsync(l => l.Id == leadId);

        await BuildEngine(dispatcher).EvaluateAsync(tenantId, lead, WorkflowTrigger.StatusChange, db,
            default, Context(tenantId, leadId));

        var log = await db.WorkflowExecutionLogs.SingleAsync();
        Assert.Equal(WorkflowExecutionStatus.Succeeded, log.Status);

        var step = await db.WorkflowExecutionSteps
            .SingleAsync(s => s.Kind == WorkflowStepKind.Action && s.ActionType == "email");

        Assert.Equal(WorkflowStepStatus.Succeeded, step.Status);

        // The rendered body is never persisted into the history: it carries lead PII and this
        // table is tenant-readable and exportable.
        Assert.DoesNotContain("Ada Lovelace", step.Message ?? "");

        // The recipient is masked, keeping the domain — a rule mailing the wrong domain is
        // the failure an Admin needs to see.
        Assert.Equal("a***@example.com", step.RecipientRedacted);
    }

    [Fact]
    public async Task A_workflow_send_to_an_opted_out_recipient_is_refused()
    {
        // The central acceptance criterion for consent: suppression must apply to
        // workflow-triggered sends, not only to ones a user pressed a button for.
        var tenantId = Guid.NewGuid();
        var (connectionString, leadId) = await SetupAsync(tenantId, "ada@example.com");

        await using var db = _factory.CreateForTenant(connectionString, tenantId);

        var provider = new CountingProvider(SendResult.Success("pid"));
        var scheduler = new RecordingScheduler();
        var dispatcher = BuildDispatcher(scheduler, provider);

        var suppression = new SuppressionService(NullLogger<SuppressionService>.Instance, TimeProvider.System);
        await suppression.OptOutAsync(db, tenantId, MessageChannel.Email, "ada@example.com", "Manual", default);

        db.WorkflowRules.Add(WorkflowRule.Create(tenantId, "Welcome",
            WorkflowTrigger.StatusChange, """{"always":true}""", EmailAction));
        await db.SaveChangesAsync();

        var lead = await db.Leads.Include(l => l.Stage).FirstAsync(l => l.Id == leadId);

        await BuildEngine(dispatcher).EvaluateAsync(tenantId, lead, WorkflowTrigger.StatusChange, db,
            default, Context(tenantId, leadId));

        Assert.Equal(0, provider.SendCount);
        Assert.Empty(scheduler.Enqueued);

        var message = await db.Messages.SingleAsync();
        Assert.Equal(MessageStatus.Rejected, message.Status);
        Assert.Equal(MessageErrorCategory.RecipientSuppressed, message.ErrorCategory);

        // Recorded as a failed action, so an Admin can see the rule ran and chose not to send.
        var log = await db.WorkflowExecutionLogs.SingleAsync();
        Assert.Equal(WorkflowExecutionStatus.Failed, log.Status);
    }

    [Fact]
    public async Task A_lead_with_no_email_fails_the_action_without_queueing_anything()
    {
        var tenantId = Guid.NewGuid();
        var (connectionString, leadId) = await SetupAsync(tenantId, "");

        await using var db = _factory.CreateForTenant(connectionString, tenantId);

        var dispatcher = BuildDispatcher(new RecordingScheduler(), new CountingProvider(SendResult.Success("pid")));

        db.WorkflowRules.Add(WorkflowRule.Create(tenantId, "Welcome",
            WorkflowTrigger.StatusChange, """{"always":true}""", EmailAction));
        await db.SaveChangesAsync();

        var lead = await db.Leads.Include(l => l.Stage).FirstAsync(l => l.Id == leadId);

        await BuildEngine(dispatcher).EvaluateAsync(tenantId, lead, WorkflowTrigger.StatusChange, db,
            default, Context(tenantId, leadId));

        Assert.Equal(0, await db.Messages.CountAsync());

        var step = await db.WorkflowExecutionSteps
            .SingleAsync(s => s.Kind == WorkflowStepKind.Action);

        Assert.Equal(WorkflowStepStatus.Failed, step.Status);
        Assert.Equal(WorkflowErrorCategory.MissingEmailRecipient, step.ErrorCategory);
    }

    [Fact]
    public async Task An_unconfigured_email_channel_is_reported_as_not_configured()
    {
        // The diagnostic that lets an Admin tell "the rule is fine, email is not set up" from
        // "the rule is broken" — preserved from the previous implementation.
        var tenantId = Guid.NewGuid();
        var (connectionString, leadId) = await SetupAsync(tenantId, "ada@example.com");

        await using var db = _factory.CreateForTenant(connectionString, tenantId);

        // No providers registered at all.
        var dispatcher = BuildDispatcher(new RecordingScheduler());

        db.WorkflowRules.Add(WorkflowRule.Create(tenantId, "Welcome",
            WorkflowTrigger.StatusChange, """{"always":true}""", EmailAction));
        await db.SaveChangesAsync();

        var lead = await db.Leads.Include(l => l.Stage).FirstAsync(l => l.Id == leadId);

        await BuildEngine(dispatcher).EvaluateAsync(tenantId, lead, WorkflowTrigger.StatusChange, db,
            default, Context(tenantId, leadId));

        var step = await db.WorkflowExecutionSteps.SingleAsync(s => s.Kind == WorkflowStepKind.Action);

        Assert.Equal(WorkflowStepStatus.Failed, step.Status);
        Assert.Equal(WorkflowErrorCategory.EmailDeliveryNotConfigured, step.ErrorCategory);
    }

    [Fact]
    public async Task Re_evaluating_the_same_trigger_does_not_queue_a_second_message()
    {
        // A Hangfire retry of the rule evaluation re-runs the whole rule. The idempotency key
        // is derived from the correlation id, which is stable across retries of one trigger,
        // so the customer does not receive the message twice.
        var tenantId = Guid.NewGuid();
        var (connectionString, leadId) = await SetupAsync(tenantId, "ada@example.com");

        await using var db = _factory.CreateForTenant(connectionString, tenantId);

        var dispatcher = BuildDispatcher(new RecordingScheduler(), new CountingProvider(SendResult.Success("pid")));

        db.WorkflowRules.Add(WorkflowRule.Create(tenantId, "Welcome",
            WorkflowTrigger.StatusChange, """{"always":true}""", EmailAction));
        await db.SaveChangesAsync();

        var lead = await db.Leads.Include(l => l.Stage).FirstAsync(l => l.Id == leadId);
        var engine = BuildEngine(dispatcher);
        var context = Context(tenantId, leadId);

        await engine.EvaluateAsync(tenantId, lead, WorkflowTrigger.StatusChange, db, default, context);
        await engine.EvaluateAsync(tenantId, lead, WorkflowTrigger.StatusChange, db, default, context);

        Assert.Equal(1, await db.Messages.CountAsync());
    }

    [Fact]
    public async Task A_failing_email_rule_does_not_stop_the_next_rule_from_running()
    {
        // Failure semantics preserved: one rule's send failing must not prevent another rule
        // from being evaluated.
        var tenantId = Guid.NewGuid();
        var (connectionString, leadId) = await SetupAsync(tenantId, "ada@example.com");

        await using var db = _factory.CreateForTenant(connectionString, tenantId);

        // No email provider, so the email rule cannot send.
        var dispatcher = BuildDispatcher(new RecordingScheduler());

        db.WorkflowRules.Add(WorkflowRule.Create(tenantId, "A-email",
            WorkflowTrigger.StatusChange, """{"always":true}""", EmailAction));

        db.WorkflowRules.Add(WorkflowRule.Create(tenantId, "B-assign",
            WorkflowTrigger.StatusChange, """{"always":true}""",
            $$"""{"type":"assign","userId":"{{Guid.NewGuid()}}"}"""));

        await db.SaveChangesAsync();

        var lead = await db.Leads.Include(l => l.Stage).FirstAsync(l => l.Id == leadId);

        await BuildEngine(dispatcher).EvaluateAsync(tenantId, lead, WorkflowTrigger.StatusChange, db,
            default, Context(tenantId, leadId));

        // Both rules produced their own execution row — the second was evaluated.
        Assert.Equal(2, await db.WorkflowExecutionLogs.CountAsync());
    }
}
