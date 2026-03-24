using IronMonkey.Data;
using Microsoft.EntityFrameworkCore;

namespace IronMonkey.ApiService.Features.Leads.Pipeline.States;

public class StateValidationService(ITenantDbContextFactory factory) : IStateValidationService
{
    public async Task<string?> ValidateTransitionAsync(
        Guid tenantId, string connectionString, Guid leadId, Guid targetStageId,
        CancellationToken cancellationToken)
    {
        await using var db = factory.CreateForTenant(connectionString, tenantId);

        var lead = await db.Leads.FindAsync(new object[] { leadId }, cancellationToken);
        if (lead == null) return "Lead not found.";

        // Already in target stage — no-op, allow
        if (lead.PipelineStageId == targetStageId) return null;

        var allowedTransitions = await db.StageTransitions
            .Where(t => t.FromStageId == lead.PipelineStageId)
            .Include(t => t.ToStage)
            .ToListAsync(cancellationToken);

        var isAllowed = allowedTransitions.Any(t => t.ToStageId == targetStageId);
        if (isAllowed) return null;

        // Build error: "Cannot move from [current] to [target]. Allowed: [stage names]"
        var fromStage = await db.PipelineStages.FindAsync(new object[] { lead.PipelineStageId }, cancellationToken);
        var targetStage = await db.PipelineStages.FindAsync(new object[] { targetStageId }, cancellationToken);
        var allowedNames = allowedTransitions.Select(t => t.ToStage.Name).ToList();

        var fromName = fromStage?.Name ?? "Unknown";
        var targetName = targetStage?.Name ?? "Unknown";
        var allowedList = allowedNames.Any()
            ? string.Join(", ", allowedNames)
            : "none configured";

        return $"Cannot move from [{fromName}] to [{targetName}]. Allowed: [{allowedList}]";
    }
}
