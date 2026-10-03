using IronMonkey.Common;
using IronMonkey.Data;
using IronMonkey.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace IronMonkey.Tests.Fixtures;

/// <summary>
/// Seeds a tenant's default opportunity stages and resolves them by name.
///
/// Opportunity stages are tenant rows rather than a hardcoded list, so a test that wants a
/// deal "in Proposal" has to have a Proposal stage first. This keeps that a one-liner and —
/// more importantly — keeps every test seeding the *same* default set, so a test asserting on
/// won/lost behaviour is asserting against the stage types the product actually ships.
/// </summary>
public static class OpportunityStageSeed
{
    /// <summary>
    /// Adds the default opportunity stage set to the tenant and returns a name → id map.
    /// Idempotent: calling it twice returns the existing stages rather than duplicating them.
    /// </summary>
    public static async Task<Dictionary<string, Guid>> EnsureAsync(
        TenantDbContext db, Guid tenantId, CancellationToken ct = default)
    {
        var existing = await db.PipelineStages
            .Where(s => s.RecordType == PipelineRecordType.Opportunity)
            .ToListAsync(ct);

        if (existing.Count > 0)
            return existing.ToDictionary(s => s.Name, s => s.Id, StringComparer.OrdinalIgnoreCase);

        var created = new List<PipelineStage>();
        foreach (var definition in OpportunityStages.Defaults)
        {
            var stageType = Enum.Parse<StageType>(definition.StageType);
            created.Add(PipelineStage.CreateFor(
                tenantId, PipelineRecordType.Opportunity, definition.Name, definition.Order, stageType));
        }

        db.PipelineStages.AddRange(created);
        await db.SaveChangesAsync(ct);

        return created.ToDictionary(s => s.Name, s => s.Id, StringComparer.OrdinalIgnoreCase);
    }
}
