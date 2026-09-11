using System.Text.Json;
using IronMonkey.Data;
using IronMonkey.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace IronMonkey.ApiService.Features.Leads.Pipeline.Routing;

public class LeadRoutingService : ILeadRoutingService
{
    public async Task<Guid?> GetNextAssigneeAsync(
        Guid tenantId, string connectionString, Lead lead, TenantDbContext db,
        CancellationToken cancellationToken)
    {
        var config = await db.RoutingConfigs
            .FirstOrDefaultAsync(r => r.TenantId == tenantId, cancellationToken);

        if (config == null || !config.IsEnabled) return null;

        // Get active agent users in stable order for consistent round-robin (D-13)
        var agents = await db.Users
            .Where(u => !u.IsDeleted)
            .OrderBy(u => u.Id)  // stable sort for consistent ordering
            .Select(u => u.Id)
            .ToListAsync(cancellationToken);

        if (!agents.Any()) return null;

        if (config.Strategy == RoutingStrategy.RoundRobin)
        {
            // Strict round-robin: advance pointer, assign next agent (D-13)
            var nextIndex = (config.RoundRobinPointer + 1) % agents.Count;
            config.RoundRobinPointer = nextIndex;
            await db.SaveChangesAsync(cancellationToken);
            return agents[nextIndex];
        }
        else // Territory routing
        {
            if (string.IsNullOrEmpty(config.TerritoryMapJson)) return null;

            // Stored either as the structured rule set the configuration UI writes, or as the
            // original flat {"value": "agent-guid"} map. Parse handles both and flattens to
            // the lookup this method has always used.
            var ruleSet = TerritoryRuleSet.Parse(config.TerritoryMapJson);
            if (ruleSet == null) return null;

            var map = ruleSet.ToLookup();
            if (map.Count == 0) return null;

            string? dimensionValue = null;

            if (config.Dimension == RoutingDimension.LeadSource)
            {
                dimensionValue = lead.Source.ToString();
            }
            else if (config.Dimension == RoutingDimension.CustomField && config.CustomFieldKey != null)
            {
                lead.CustomFields.Values.TryGetValue(config.CustomFieldKey, out var fieldVal);
                dimensionValue = fieldVal?.ToString();
            }

            if (dimensionValue == null || !map.TryGetValue(dimensionValue, out var agentIdStr))
                return null;  // D-16: leave unassigned if no match

            if (!Guid.TryParse(agentIdStr, out var agentId)) return null;
            return agents.Contains(agentId) ? agentId : null;
        }
    }
}
