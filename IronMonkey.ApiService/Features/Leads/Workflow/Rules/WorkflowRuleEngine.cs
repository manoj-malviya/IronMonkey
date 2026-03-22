using System.Text.Json;
using IronMonkey.ApiService.Notifications;
using IronMonkey.Data;
using IronMonkey.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace IronMonkey.ApiService.Features.Leads.Workflow.Rules;

public class WorkflowRuleEngine(INotificationService notificationService, ILogger<WorkflowRuleEngine> logger)
    : IWorkflowRuleEngine
{
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

    private static bool EvaluateCondition(string conditionJson, Lead lead)
    {
        // Minimal condition evaluation: parse conditionJson, check simple field conditions.
        // For Phase 4 v1: support {"always":true} as pass-through, and
        // {"field":"Source","equals":"Api"} for single-field conditions.
        // Full RulesEngine integration can be layered on in Phase 5.
        try
        {
            using var doc = JsonDocument.Parse(conditionJson);
            var root = doc.RootElement;

            if (root.TryGetProperty("always", out var always) && always.GetBoolean())
                return true;

            if (root.TryGetProperty("field", out var fieldProp) &&
                root.TryGetProperty("equals", out var equalsProp))
            {
                var fieldName = fieldProp.GetString();
                var expectedValue = equalsProp.GetString();
                var actualValue = fieldName switch
                {
                    "Source" => lead.Source.ToString(),
                    _ => null
                };
                return string.Equals(actualValue, expectedValue, StringComparison.OrdinalIgnoreCase);
            }

            // Default: empty condition = always fire
            return true;
        }
        catch
        {
            return false;
        }
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
                var message = root.GetProperty("message").GetString()!;
                await notificationService.CreateAsync(db, tenantId, userId, message, lead.Id, cancellationToken);
                break;

            case "assign":
                var assignUserId = Guid.Parse(root.GetProperty("userId").GetString()!);
                lead.AssignTo(assignUserId);
                await db.SaveChangesAsync(cancellationToken);
                break;

            case "schedule_task":
                var title = root.GetProperty("title").GetString()!;
                var dueDays = root.GetProperty("dueDays").GetInt32();
                var task = LeadTask.Create(tenantId, lead.Id, title,
                    DateTime.UtcNow.AddDays(dueDays), TaskPriority.Medium, lead.AssignedToUserId);
                db.LeadTasks.Add(task);
                await db.SaveChangesAsync(cancellationToken);
                break;

            default:
                logger.LogWarning("Unknown workflow action type: {ActionType}", actionType);
                break;
        }
    }
}
