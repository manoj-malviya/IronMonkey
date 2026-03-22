using Hangfire;
using IronMonkey.ApiService.Features.Leads.Workflow.Rules;
using IronMonkey.Data;
using IronMonkey.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace IronMonkey.ApiService.BackgroundJobs;

public class TimeElapsedRuleScanJob(
    ITenantRegistry tenantRegistry,
    ITenantDbContextFactory tenantContextFactory,
    IWorkflowRuleEngine ruleEngine,
    ILogger<TimeElapsedRuleScanJob> logger)
{
    [AutomaticRetry(Attempts = 2, DelaysInSeconds = [60, 300])]
    [Queue("default")]
    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Starting time-elapsed workflow rule scan");

        var tenants = await tenantRegistry.GetAllProvisionedTenantsAsync(cancellationToken);

        foreach (var (tenantId, connectionString) in tenants)
        {
            try
            {
                await using var db = tenantContextFactory.CreateForTenant(connectionString, tenantId);

                // Check if this tenant has any active TimeElapsed rules
                var hasTimeRules = await db.WorkflowRules
                    .AnyAsync(r => r.Trigger == WorkflowTrigger.TimeElapsed && r.IsActive, cancellationToken);

                if (!hasTimeRules) continue;

                // Evaluate all non-terminal leads against time-elapsed rules
                var leads = await db.Leads
                    .Include(l => l.Stage)
                    .Where(l => !l.Stage.IsTerminal)
                    .ToListAsync(cancellationToken);

                foreach (var lead in leads)
                {
                    await ruleEngine.EvaluateAsync(tenantId, lead, WorkflowTrigger.TimeElapsed, db, cancellationToken);
                }

                logger.LogDebug("Time-elapsed scan complete for tenant {TenantId}: {Count} leads evaluated",
                    tenantId, leads.Count);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Time-elapsed scan failed for tenant {TenantId}", tenantId);
                // Continue with next tenant — don't let one failure block others
            }
        }

        logger.LogInformation("Time-elapsed workflow rule scan complete");
    }
}
