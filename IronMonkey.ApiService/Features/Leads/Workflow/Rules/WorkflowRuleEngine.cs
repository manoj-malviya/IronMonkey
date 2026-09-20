using System.Net.Http.Json;
using System.Text.Json;
using IronMonkey.ApiService.Features.Communications;
using IronMonkey.ApiService.Features.Leads.Workflow.Execution;
using IronMonkey.ApiService.Notifications;
using IronMonkey.Data.Communications;
using IronMonkey.Data;
using IronMonkey.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace IronMonkey.ApiService.Features.Leads.Workflow.Rules;

public class WorkflowRuleEngine(
    INotificationService notificationService,
    IHttpClientFactory httpClientFactory,
    ILogger<WorkflowRuleEngine> logger,
    IWorkflowExecutionRecorder? executionRecorder = null,
    IMessageDispatcher? messageDispatcher = null)
    : IWorkflowRuleEngine
{
    /// <summary>Named client so the webhook action gets its own timeout and no ambient auth.</summary>
    public const string WebhookClientName = "WorkflowWebhook";

    public async Task EvaluateAsync(Guid tenantId, Lead lead, WorkflowTrigger trigger,
        TenantDbContext db, CancellationToken cancellationToken,
        WorkflowExecutionContext? context = null)
    {
        var rules = await db.WorkflowRules
            .Where(r => r.Trigger == trigger && r.IsActive)
            .ToListAsync(cancellationToken);

        foreach (var rule in rules)
        {
            // Each rule is recorded and failed independently. One rule's webhook being down
            // must not stop the next rule from being evaluated, and must not roll back the
            // lead change that triggered any of them.
            var run = await StartRecordingAsync(db, context, tenantId, rule, lead, trigger, cancellationToken);

            try
            {
                await EvaluateRuleAsync(rule, tenantId, lead, trigger, db, run, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // The worker is shutting down. Close the row as cancelled so it does not sit
                // Running forever waiting on reconciliation, then let the cancellation
                // propagate — Hangfire needs to see it to schedule the retry.
                if (run is not null)
                {
                    await run.FailAsync(WorkflowErrorCategory.Cancelled,
                        "Evaluation was cancelled before it completed.", CancellationToken.None);
                }

                logger.LogWarning(
                    "Workflow evaluation cancelled: tenant {TenantId}, rule {RuleId}, lead {LeadId}, trigger {Trigger}, execution {ExecutionId}",
                    tenantId, rule.Id, lead.Id, trigger, run?.ExecutionId);
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Failed to evaluate workflow rule {RuleId} for lead {LeadId} in tenant {TenantId} " +
                    "(trigger {Trigger}, execution {ExecutionId})",
                    rule.Id, lead.Id, tenantId, trigger, run?.ExecutionId);

                if (run is not null)
                    await run.FailAsync(WorkflowErrorCategory.UnexpectedError, ex.Message, cancellationToken);

                // Don't rethrow — continue processing other rules.
            }
        }
    }

    /// <summary>
    /// Opens the history row for one rule, tolerating a recorder that is absent (tests
    /// constructing the engine directly) or that failed to write. Evaluation proceeds either
    /// way: losing the audit trail is bad, but not acting on a configured rule is worse.
    /// </summary>
    private async Task<WorkflowExecutionRun?> StartRecordingAsync(
        TenantDbContext db, WorkflowExecutionContext? context, Guid tenantId,
        WorkflowRule rule, Lead lead, WorkflowTrigger trigger, CancellationToken cancellationToken)
    {
        if (executionRecorder is null || context is null) return null;

        return await executionRecorder.StartAsync(db, context, rule, lead, trigger, cancellationToken);
    }

    private async Task EvaluateRuleAsync(
        WorkflowRule rule, Guid tenantId, Lead lead, WorkflowTrigger trigger,
        TenantDbContext db, WorkflowExecutionRun? run, CancellationToken cancellationToken)
    {
        var condition = EvaluateConditionDetailed(rule.ConditionJson, lead);

        if (!condition.Parsed)
        {
            // Malformed condition JSON is a broken rule, not an inapplicable one — and it is
            // invisible without this, because the engine's fallback is "do not fire".
            logger.LogWarning(
                "Workflow rule {RuleId} ({RuleName}) has invalid condition JSON; rule did not fire. Tenant {TenantId}, lead {LeadId}",
                rule.Id, rule.Name, tenantId, lead.Id);

            if (run is not null)
                await run.ConditionFailedAsync(condition.Error ?? "Condition JSON could not be parsed.", cancellationToken);
            return;
        }

        if (!condition.Matched)
        {
            logger.LogDebug(
                "Workflow rule {RuleId} skipped for lead {LeadId}: condition did not match",
                rule.Id, lead.Id);

            if (run is not null)
                await run.ConditionNotMatchedAsync("Condition did not match.", cancellationToken);
            return;
        }

        if (run is not null)
            await run.ConditionMatchedAsync("Condition matched.", cancellationToken);

        await ExecuteActionAsync(rule.ActionJson, tenantId, lead, db, run, rule.Id, trigger, cancellationToken);

        if (run is not null)
            await run.CompleteAsync(cancellationToken);
    }

    /// <summary>
    /// Supported conditions:
    ///   {"always": true}
    ///   {"field": "Source",  "equals": "Api"}
    ///   {"field": "Email",   "contains": "@acme.com"}
    ///   {"field": "StageName", "equals": "Qualified"}
    ///   {"customField": "&lt;definitionId&gt;", "equals": "Swift"}
    ///   {"olderThanDays": 3}        — lead created at least N days ago
    ///   {"all": [ ...conditions ]}  — every nested condition must pass
    /// </summary>
    internal static bool EvaluateCondition(string conditionJson, Lead lead)
        => EvaluateConditionDetailed(conditionJson, lead).Matched;

    /// <summary>
    /// Whether the condition parsed, and whether it matched.
    ///
    /// The two are separate because the engine's safe fallback for malformed JSON is "do not
    /// fire", which is indistinguishable from "did not match" to a caller that only sees a
    /// bool — and the whole point of the execution log is to tell an Admin which of the two
    /// happened.
    /// </summary>
    internal readonly record struct ConditionResult(bool Parsed, bool Matched, string? Error);

    internal static ConditionResult EvaluateConditionDetailed(string conditionJson, Lead lead)
    {
        try
        {
            using var doc = JsonDocument.Parse(conditionJson);
            return new ConditionResult(Parsed: true, Matched: Matches(doc.RootElement, lead), Error: null);
        }
        catch (JsonException ex)
        {
            // Malformed JSON must not silently fire the action.
            return new ConditionResult(Parsed: false, Matched: false,
                Error: $"Condition JSON is not valid: {ex.Message}");
        }
    }

    private static bool Matches(JsonElement root, Lead lead)
    {
        if (root.TryGetProperty("always", out var always) && always.ValueKind == JsonValueKind.True)
            return true;

        // Composite: every nested condition must hold.
        if (root.TryGetProperty("all", out var all) && all.ValueKind == JsonValueKind.Array)
            return all.EnumerateArray().All(child => Matches(child, lead));

        if (root.TryGetProperty("any", out var any) && any.ValueKind == JsonValueKind.Array)
            return any.EnumerateArray().Any(child => Matches(child, lead));

        // Age gate. Without this, a TimeElapsed rule fires on every hourly scan forever,
        // because the scan itself carries no notion of how long the lead has been waiting.
        if (root.TryGetProperty("olderThanDays", out var olderThan) &&
            olderThan.TryGetInt32(out var days))
        {
            if ((DateTime.UtcNow - lead.CreatedAt).TotalDays < days)
                return false;

            // An age gate on its own is a complete condition.
            if (!root.TryGetProperty("field", out _) && !root.TryGetProperty("customField", out _))
                return true;
        }

        var actual = ResolveValue(root, lead);
        if (actual is null) return false;

        if (root.TryGetProperty("equals", out var eq))
            return string.Equals(actual, eq.GetString(), StringComparison.OrdinalIgnoreCase);

        if (root.TryGetProperty("notEquals", out var neq))
            return !string.Equals(actual, neq.GetString(), StringComparison.OrdinalIgnoreCase);

        if (root.TryGetProperty("contains", out var contains))
            return actual.Contains(contains.GetString() ?? "", StringComparison.OrdinalIgnoreCase);

        if (root.TryGetProperty("isEmpty", out var isEmpty) && isEmpty.ValueKind == JsonValueKind.True)
            return string.IsNullOrWhiteSpace(actual);

        // A field named with no comparison is treated as "has any value".
        return !string.IsNullOrWhiteSpace(actual);
    }

    private static string? ResolveValue(JsonElement root, Lead lead)
    {
        if (root.TryGetProperty("customField", out var customField))
        {
            var key = customField.GetString();
            if (string.IsNullOrEmpty(key)) return null;
            var value = lead.CustomFields.Get(key);
            return value switch
            {
                null => "",
                JsonElement je => je.ValueKind == JsonValueKind.String ? je.GetString() : je.ToString(),
                _ => value.ToString()
            };
        }

        if (!root.TryGetProperty("field", out var fieldProp)) return null;

        return fieldProp.GetString() switch
        {
            "Source" => lead.Source.ToString(),
            "Email" => lead.Email,
            "Mobile" => lead.Mobile,
            "FirstName" => lead.FirstName,
            "LastName" => lead.LastName,
            "StageName" => lead.Stage?.Name ?? "",
            "IsConverted" => lead.IsConverted.ToString(),
            "AssignedToUserId" => lead.AssignedToUserId?.ToString() ?? "",
            _ => null
        };
    }

    private async Task ExecuteActionAsync(string actionJson, Guid tenantId, Lead lead,
        TenantDbContext db, WorkflowExecutionRun? run, Guid ruleId, WorkflowTrigger trigger,
        CancellationToken cancellationToken)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(actionJson);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(
                "Workflow action JSON is invalid for lead {LeadId} in tenant {TenantId}: {Reason}",
                lead.Id, tenantId, ex.Message);

            if (run is not null)
            {
                // Opened as a step with no action type, because there is no parseable type to
                // name — the row still belongs in the timeline.
                await run.BeginActionAsync(actionType: null, cancellationToken);
                await run.ActionFailedAsync(WorkflowErrorCategory.InvalidActionJson,
                    $"Action JSON is not valid: {ex.Message}", cancellationToken);
            }
            return;
        }

        using (doc)
        {
            var root = doc.RootElement;

            var actionType = root.ValueKind == JsonValueKind.Object
                             && root.TryGetProperty("type", out var typeProp)
                ? typeProp.GetString()
                : null;

            if (run is not null)
                await run.BeginActionAsync(actionType, cancellationToken);

            LogActionStart(tenantId, lead, trigger, actionType, run);

            await DispatchActionAsync(root, actionType, tenantId, lead, db, run, ruleId, trigger, cancellationToken);
        }
    }

    private async Task DispatchActionAsync(
        JsonElement root, string? actionType, Guid tenantId, Lead lead,
        TenantDbContext db, WorkflowExecutionRun? run, Guid ruleId, WorkflowTrigger trigger,
        CancellationToken cancellationToken)
    {
        switch (actionType)
        {
            case "notify":
                await RunNotifyAsync(root, tenantId, lead, db, run, cancellationToken);
                break;

            case "assign":
                await RunAssignAsync(root, lead, db, run, cancellationToken);
                break;

            case "schedule_task":
                await RunScheduleTaskAsync(root, tenantId, lead, db, run, cancellationToken);
                break;

            case "webhook":
                await SendWebhookAsync(root, tenantId, lead, run, cancellationToken);
                break;

            case "email":
                await SendEmailAsync(root, tenantId, lead, db, run, ruleId, trigger, cancellationToken);
                break;

            default:
                logger.LogWarning("Unknown workflow action type: {ActionType}", actionType);
                if (run is not null)
                {
                    await run.ActionFailedAsync(WorkflowErrorCategory.UnknownActionType,
                        actionType is null
                            ? "Action JSON has no \"type\" property."
                            : $"\"{actionType}\" is not a supported action type.",
                        cancellationToken);
                }
                break;
        }
    }

    private async Task RunNotifyAsync(JsonElement root, Guid tenantId, Lead lead,
        TenantDbContext db, WorkflowExecutionRun? run, CancellationToken cancellationToken)
    {
        if (!TryGetGuid(root, "userId", out var userId))
        {
            await FailActionAsync(run, WorkflowErrorCategory.InvalidActionConfiguration,
                "Action is missing a valid \"userId\".", cancellationToken);
            return;
        }

        var message = Interpolate(
            root.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "", lead);

        try
        {
            await notificationService.CreateAsync(db, tenantId, userId, message, lead.Id, cancellationToken);
            await SucceedActionAsync(run, "Notification created.", cancellationToken);
        }
        catch (Exception ex) when (ex is DbUpdateException or InvalidOperationException)
        {
            await FailActionAsync(run, WorkflowErrorCategory.PersistenceFailure,
                $"Notification could not be saved: {ex.Message}", cancellationToken);
        }
    }

    private async Task RunAssignAsync(JsonElement root, Lead lead,
        TenantDbContext db, WorkflowExecutionRun? run, CancellationToken cancellationToken)
    {
        if (!TryGetGuid(root, "userId", out var assignUserId))
        {
            await FailActionAsync(run, WorkflowErrorCategory.InvalidActionConfiguration,
                "Action is missing a valid \"userId\".", cancellationToken);
            return;
        }

        // Checked before assigning: the column has no FK to Users, so a typo'd id would
        // otherwise persist as an assignment to nobody and read as a success.
        var userExists = await db.Users.AnyAsync(u => u.Id == assignUserId, cancellationToken);
        if (!userExists)
        {
            await FailActionAsync(run, WorkflowErrorCategory.ReferencedRecordNotFound,
                "The user named by the rule does not exist in this organisation.", cancellationToken);
            return;
        }

        try
        {
            lead.AssignTo(assignUserId);
            await db.SaveChangesAsync(cancellationToken);
            await SucceedActionAsync(run, "Lead assigned.", cancellationToken);
        }
        catch (Exception ex) when (ex is DbUpdateException or InvalidOperationException)
        {
            await FailActionAsync(run, WorkflowErrorCategory.PersistenceFailure,
                $"Assignment could not be saved: {ex.Message}", cancellationToken);
        }
    }

    private async Task RunScheduleTaskAsync(JsonElement root, Guid tenantId, Lead lead,
        TenantDbContext db, WorkflowExecutionRun? run, CancellationToken cancellationToken)
    {
        var title = Interpolate(
            root.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "", lead);

        if (string.IsNullOrWhiteSpace(title))
        {
            await FailActionAsync(run, WorkflowErrorCategory.InvalidActionConfiguration,
                "Action is missing a \"title\".", cancellationToken);
            return;
        }

        if (!root.TryGetProperty("dueDays", out var dueProp) || !dueProp.TryGetInt32(out var dueDays))
        {
            await FailActionAsync(run, WorkflowErrorCategory.InvalidActionConfiguration,
                "Action is missing a valid integer \"dueDays\".", cancellationToken);
            return;
        }

        try
        {
            var task = LeadTask.Create(tenantId, lead.Id, title,
                DateTime.UtcNow.AddDays(dueDays), TaskPriority.Medium, lead.AssignedToUserId);
            db.LeadTasks.Add(task);
            await db.SaveChangesAsync(cancellationToken);
            await SucceedActionAsync(run, $"Task scheduled, due in {dueDays} day(s).", cancellationToken);
        }
        catch (Exception ex) when (ex is DbUpdateException or InvalidOperationException)
        {
            await FailActionAsync(run, WorkflowErrorCategory.PersistenceFailure,
                $"Task could not be saved: {ex.Message}", cancellationToken);
        }
    }

    /// <summary>
    /// POSTs the lead to an external URL. Failures are recorded, not rethrown: a third party
    /// being down must not roll back the lead change that triggered the rule.
    /// </summary>
    private async Task SendWebhookAsync(JsonElement action, Guid tenantId, Lead lead,
        WorkflowExecutionRun? run, CancellationToken cancellationToken)
    {
        var url = action.TryGetProperty("url", out var urlProp) ? urlProp.GetString() : null;

        if (string.IsNullOrWhiteSpace(url) ||
            !Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            logger.LogWarning("Workflow webhook skipped: {Url} is not a valid http(s) URL", url);

            // The invalid URL is deliberately not echoed into the stored message: a
            // "malformed" URL is frequently a well-formed one with a secret in it.
            await FailActionAsync(run, WorkflowErrorCategory.InvalidWebhookUrl,
                "The webhook URL is not a valid absolute http(s) URL.", cancellationToken);
            return;
        }

        run?.RecordTargetHost(uri);

        var client = httpClientFactory.CreateClient(WebhookClientName);

        using var request = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = JsonContent.Create(new
            {
                tenantId,
                leadId = lead.Id,
                firstName = lead.FirstName,
                lastName = lead.LastName,
                email = lead.Email,
                mobile = lead.Mobile,
                source = lead.Source.ToString(),
                stage = lead.Stage?.Name,
                assignedToUserId = lead.AssignedToUserId,
                createdAt = lead.CreatedAt,
                customFields = lead.CustomFields.Values
            })
        };

        // Optional static headers, e.g. {"headers": {"X-Api-Key": "..."}}
        if (action.TryGetProperty("headers", out var headers) && headers.ValueKind == JsonValueKind.Object)
        {
            foreach (var header in headers.EnumerateObject())
                request.Headers.TryAddWithoutValidation(header.Name, header.Value.GetString());
        }

        try
        {
            var response = await client.SendAsync(request, cancellationToken);
            run?.RecordHttpResult((int)response.StatusCode, uri);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Workflow webhook to {Url} returned {StatusCode} for lead {LeadId}",
                    uri, (int)response.StatusCode, lead.Id);

                // Status and reason phrase only. The response body is not stored: it is
                // third-party content of unknown size that may echo the request's own headers.
                await FailActionAsync(run, WorkflowErrorCategory.WebhookNonSuccessResponse,
                    $"Webhook returned HTTP {(int)response.StatusCode} {response.ReasonPhrase}.".Trim(),
                    cancellationToken);
            }
            else
            {
                logger.LogInformation("Workflow webhook to {Url} succeeded for lead {LeadId}", uri, lead.Id);
                await SucceedActionAsync(run,
                    $"Webhook returned HTTP {(int)response.StatusCode}.", cancellationToken);
            }
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            // The request's own timeout, not the worker shutting down — the named client's
            // timeout elapsed. Distinguished from a cancellation so the Admin sees "the
            // endpoint did not answer in time", which is actionable.
            logger.LogWarning(ex, "Workflow webhook to {Url} timed out for lead {LeadId}", uri, lead.Id);
            await FailActionAsync(run, WorkflowErrorCategory.WebhookTimeout,
                "Webhook did not respond before the request timed out.", cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Workflow webhook to {Url} failed for lead {LeadId}", uri, lead.Id);
            await FailActionAsync(run, WorkflowErrorCategory.WebhookNetworkFailure,
                $"Webhook could not be reached: {ex.Message}", cancellationToken);
        }
    }

    /// <summary>
    /// Sends the rule's email through the communications layer.
    ///
    /// The failure semantics of the old logged-only implementation are kept deliberately: a
    /// send that cannot be made is recorded as a <em>failed action</em> on this run, and does
    /// not roll back the lead write that triggered the rule or stop the remaining rules from
    /// being evaluated. Only the outcome changed — a message is now actually delivered.
    ///
    /// Queueing is what succeeds here, not delivery. The dispatcher hands the message to
    /// Hangfire and returns; the provider result lands on the message row minutes later. So
    /// this step reports "queued", and the message's own status is where delivery is read —
    /// which is honest, unlike reporting a send that has not happened yet as succeeded.
    ///
    /// The rendered subject and body are never persisted into the execution history: a body
    /// carries lead PII and this table is tenant-readable and exportable.
    /// </summary>
    private async Task SendEmailAsync(JsonElement action, Guid tenantId, Lead lead,
        TenantDbContext db, WorkflowExecutionRun? run, Guid ruleId, WorkflowTrigger trigger,
        CancellationToken cancellationToken)
    {
        var to = action.TryGetProperty("to", out var toProp) ? toProp.GetString() : null;

        // "to": "lead" addresses whoever the lead is, rather than a fixed mailbox.
        var recipient = string.Equals(to, "lead", StringComparison.OrdinalIgnoreCase) ? lead.Email : to;

        var subject = Interpolate(
            action.TryGetProperty("subject", out var s) ? s.GetString() ?? "" : "", lead);
        var body = Interpolate(
            action.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "", lead);

        if (string.IsNullOrWhiteSpace(recipient))
        {
            logger.LogWarning("Workflow email skipped for lead {LeadId}: no recipient", lead.Id);

            await FailActionAsync(run, WorkflowErrorCategory.MissingEmailRecipient,
                string.Equals(to, "lead", StringComparison.OrdinalIgnoreCase)
                    ? "No recipient: the rule addresses the lead, and this lead has no email address."
                    : "No recipient: the rule has no \"to\" address.",
                cancellationToken);
            return;
        }

        run?.RecordRecipient(recipient);

        if (messageDispatcher is null)
        {
            // The engine is constructed without a dispatcher only in tests that exercise
            // condition evaluation. Reported as not-configured rather than as success, for the
            // same reason as before: a silently undelivered email must never look sent.
            await FailActionAsync(run, WorkflowErrorCategory.EmailDeliveryNotConfigured,
                "Email was rendered but not delivered: messaging is not available in this context.",
                cancellationToken);
            return;
        }

        // Derived from the rule, the lead and the trigger — all of which are known here
        // regardless of whether the audit row opened. It deliberately does NOT fall back to a
        // different shape when `run` is null: a key that changes because the history table
        // rejected a write is not an idempotency key, and that fallback sent a second copy to
        // the customer whenever a retry's execution row collided on the unique correlation
        // index. Recording failing must never change what the customer receives.
        var idempotencyKey = $"workflow:{ruleId}:{lead.Id}:{trigger}";

        var outcome = await messageDispatcher.QueueAsync(db, new SendMessageCommand(
            tenantId,
            MessageChannel.Email,
            recipient,
            subject,
            body,
            idempotencyKey,
            LeadId: lead.Id,
            WorkflowRuleId: ruleId), cancellationToken);

        if (outcome.Queued || outcome.Accepted)
        {
            await SucceedActionAsync(run,
                $"Email queued for delivery (message {outcome.MessageId}).", cancellationToken);
            return;
        }

        // A refusal before the provider — unconfigured channel, opted-out recipient, channel
        // rule violation — is a failed action with the dispatcher's own category, so the
        // execution history distinguishes "we chose not to send" from "sending broke".
        await FailActionAsync(run, MapCategory(outcome.ErrorCategory),
            outcome.ErrorMessage ?? $"The message was not sent ({outcome.ErrorCategory}).",
            cancellationToken);
    }

    /// <summary>
    /// Maps a messaging refusal onto the workflow's own category vocabulary, so the execution
    /// history keeps one set of categories an Admin can filter on.
    /// </summary>
    private static WorkflowErrorCategory MapCategory(MessageErrorCategory category) => category switch
    {
        MessageErrorCategory.ChannelNotConfigured => WorkflowErrorCategory.EmailDeliveryNotConfigured,
        MessageErrorCategory.InvalidRecipient => WorkflowErrorCategory.MissingEmailRecipient,
        _ => WorkflowErrorCategory.InvalidActionConfiguration
    };

    private static Task SucceedActionAsync(WorkflowExecutionRun? run, string message, CancellationToken cancellationToken)
        => run?.ActionSucceededAsync(message, cancellationToken) ?? Task.CompletedTask;

    private static Task FailActionAsync(WorkflowExecutionRun? run, WorkflowErrorCategory category,
        string message, CancellationToken cancellationToken)
        => run?.ActionFailedAsync(category, message, cancellationToken) ?? Task.CompletedTask;

    /// <summary>
    /// Reads a Guid property without throwing on a missing or malformed value. The previous
    /// <c>Guid.Parse(root.GetProperty(...))</c> threw two different exception types for a
    /// typo'd rule, both of which surfaced only as "failed to evaluate rule".
    /// </summary>
    private static bool TryGetGuid(JsonElement root, string property, out Guid value)
    {
        value = Guid.Empty;
        return root.TryGetProperty(property, out var prop)
               && prop.ValueKind == JsonValueKind.String
               && Guid.TryParse(prop.GetString(), out value);
    }

    /// <summary>
    /// The structured operator event for an action starting. Carries the execution id so an
    /// operator reading logs and an Admin reading the UI can be talking about the same run.
    /// </summary>
    private void LogActionStart(Guid tenantId, Lead lead, WorkflowTrigger trigger,
        string? actionType, WorkflowExecutionRun? run)
    {
        logger.LogInformation(
            "Workflow action starting: tenant {TenantId}, execution {ExecutionId}, rule {RuleId}, " +
            "lead {LeadId}, trigger {Trigger}, action {ActionType}, correlation {CorrelationId}",
            tenantId, run?.ExecutionId, run?.RuleId, lead.Id, trigger, actionType, run?.CorrelationId);
    }

    /// <summary>Replaces {{FirstName}}-style placeholders with the lead's values.</summary>
    internal static string Interpolate(string template, Lead lead)
    {
        if (string.IsNullOrEmpty(template) || !template.Contains("{{")) return template;

        return template
            .Replace("{{FirstName}}", lead.FirstName)
            .Replace("{{LastName}}", lead.LastName)
            .Replace("{{FullName}}", $"{lead.FirstName} {lead.LastName}".Trim())
            .Replace("{{Email}}", lead.Email)
            .Replace("{{Mobile}}", lead.Mobile)
            .Replace("{{Source}}", lead.Source.ToString())
            .Replace("{{StageName}}", lead.Stage?.Name ?? "");
    }
}
