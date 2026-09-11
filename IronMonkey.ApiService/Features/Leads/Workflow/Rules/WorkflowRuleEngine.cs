using System.Net.Http.Json;
using System.Text.Json;
using IronMonkey.ApiService.Notifications;
using IronMonkey.Data;
using IronMonkey.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace IronMonkey.ApiService.Features.Leads.Workflow.Rules;

public class WorkflowRuleEngine(
    INotificationService notificationService,
    IHttpClientFactory httpClientFactory,
    ILogger<WorkflowRuleEngine> logger)
    : IWorkflowRuleEngine
{
    /// <summary>Named client so the webhook action gets its own timeout and no ambient auth.</summary>
    public const string WebhookClientName = "WorkflowWebhook";

    public async Task EvaluateAsync(Guid tenantId, Lead lead, WorkflowTrigger trigger,
        TenantDbContext db, CancellationToken cancellationToken)
    {
        var rules = await db.WorkflowRules
            .Where(r => r.Trigger == trigger && r.IsActive)
            .ToListAsync(cancellationToken);

        foreach (var rule in rules)
        {
            try
            {
                if (!EvaluateCondition(rule.ConditionJson, lead))
                    continue;

                await ExecuteActionAsync(rule.ActionJson, tenantId, lead, db, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to evaluate workflow rule {RuleId} for lead {LeadId}",
                    rule.Id, lead.Id);
                // Don't rethrow — continue processing other rules
            }
        }
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
    {
        try
        {
            using var doc = JsonDocument.Parse(conditionJson);
            return Matches(doc.RootElement, lead);
        }
        catch (JsonException)
        {
            // Malformed JSON must not silently fire the action.
            return false;
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
        TenantDbContext db, CancellationToken cancellationToken)
    {
        using var doc = JsonDocument.Parse(actionJson);
        var root = doc.RootElement;
        var actionType = root.GetProperty("type").GetString();

        switch (actionType)
        {
            case "notify":
                var userId = Guid.Parse(root.GetProperty("userId").GetString()!);
                var message = Interpolate(root.GetProperty("message").GetString()!, lead);
                await notificationService.CreateAsync(db, tenantId, userId, message, lead.Id, cancellationToken);
                break;

            case "assign":
                var assignUserId = Guid.Parse(root.GetProperty("userId").GetString()!);
                lead.AssignTo(assignUserId);
                await db.SaveChangesAsync(cancellationToken);
                break;

            case "schedule_task":
                var title = Interpolate(root.GetProperty("title").GetString()!, lead);
                var dueDays = root.GetProperty("dueDays").GetInt32();
                var task = LeadTask.Create(tenantId, lead.Id, title,
                    DateTime.UtcNow.AddDays(dueDays), TaskPriority.Medium, lead.AssignedToUserId);
                db.LeadTasks.Add(task);
                await db.SaveChangesAsync(cancellationToken);
                break;

            case "webhook":
                await SendWebhookAsync(root, tenantId, lead, cancellationToken);
                break;

            case "email":
                await SendEmailAsync(root, lead, cancellationToken);
                break;

            default:
                logger.LogWarning("Unknown workflow action type: {ActionType}", actionType);
                break;
        }
    }

    /// <summary>
    /// POSTs the lead to an external URL. Failures are logged, not rethrown: a third party
    /// being down must not roll back the lead change that triggered the rule.
    /// </summary>
    private async Task SendWebhookAsync(JsonElement action, Guid tenantId, Lead lead,
        CancellationToken cancellationToken)
    {
        var url = action.GetProperty("url").GetString();
        if (string.IsNullOrWhiteSpace(url) ||
            !Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            logger.LogWarning("Workflow webhook skipped: {Url} is not a valid http(s) URL", url);
            return;
        }

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
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Workflow webhook to {Url} returned {StatusCode} for lead {LeadId}",
                    uri, (int)response.StatusCode, lead.Id);
            }
            else
            {
                logger.LogInformation("Workflow webhook to {Url} succeeded for lead {LeadId}", uri, lead.Id);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Workflow webhook to {Url} failed for lead {LeadId}", uri, lead.Id);
        }
    }

    /// <summary>
    /// Records the email that would be sent. Delivery is deliberately not wired up yet —
    /// there is no tenant-level SMTP configuration, so sending would depend on a single
    /// shared mailbox. The rendered subject and body are logged so the rule can be verified.
    /// </summary>
    private Task SendEmailAsync(JsonElement action, Lead lead, CancellationToken cancellationToken)
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
            return Task.CompletedTask;
        }

        logger.LogInformation(
            "Workflow email (not delivered — SMTP not configured) to {Recipient} for lead {LeadId}: {Subject} | {Body}",
            recipient, lead.Id, subject, body);

        return Task.CompletedTask;
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
