using IronMonkey.Data;
using IronMonkey.Data.Entities;

namespace IronMonkey.ApiService.Features.Activity;

public class ActivityTrackingService(ITenantDbContextFactory dbContextFactory) : IActivityTrackingService
{
    public async Task AddNoteAsync(Guid tenantId, string connectionString, Guid leadId, Guid actorId, string noteContent, CancellationToken cancellationToken = default)
    {
        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);
        var entry = ActivityLog.Create(
            tenantId,
            leadId,
            actorId,
            eventType: "Note",
            entityType: "Lead",
            entityId: leadId.ToString(),
            oldValues: null,
            newValues: new Dictionary<string, object?> { ["Content"] = noteContent });
        db.ActivityLogs.Add(entry);
        await db.SaveChangesAsync(cancellationToken);
    }
}
