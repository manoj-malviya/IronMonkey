using IronMonkey.Data;
using IronMonkey.Data.Entities;

namespace IronMonkey.ApiService.Features.Activity;

public class ActivityTrackingService(ITenantDbContextFactory dbContextFactory) : IActivityTrackingService
{
    public Task<Guid> AddNoteAsync(Guid tenantId, string connectionString, Guid leadId, Guid actorId, string noteContent, CancellationToken cancellationToken = default)
        => AddNoteForAsync(tenantId, connectionString, "Lead", leadId, actorId, noteContent, cancellationToken);

    public async Task<Guid> AddNoteForAsync(Guid tenantId, string connectionString, string subjectType, Guid subjectId, Guid actorId, string noteContent, CancellationToken cancellationToken = default)
    {
        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);
        var entry = ActivityLog.CreateFor(
            tenantId,
            subjectType,
            subjectId,
            actorId,
            eventType: "Note",
            entityType: subjectType,
            entityId: subjectId.ToString(),
            oldValues: null,
            newValues: new Dictionary<string, object?> { ["Content"] = noteContent });
        db.ActivityLogs.Add(entry);
        await db.SaveChangesAsync(cancellationToken);
        return entry.Id;
    }
}
