using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.Data;
using IronMonkey.Data.Abstractions;
using IronMonkey.Data.Entities;
using IronMonkey.Data.Outbox;

namespace IronMonkey.ApiService.Interceptors;

/// <summary>
/// EF Core SaveChanges interceptor that automatically creates ActivityLog entries
/// for all entity changes on TenantDbContext. Registered once in DI — no per-endpoint instrumentation needed.
///
/// Captures: Created, Updated, Deleted events on all BaseTenantEntity types.
/// Does NOT capture changes to ActivityLog itself (prevents infinite recursion).
/// Note entities created via IActivityTrackingService.AddNoteAsync are excluded
/// because AddNoteAsync calls SaveChangesAsync directly on a new db instance.
/// </summary>
public class ActivityChangeInterceptor(IUserContext userContext) : SaveChangesInterceptor
{
    // Entity types to exclude from activity logging to prevent noise/recursion
    private static readonly HashSet<Type> ExcludedTypes =
    [
        typeof(ActivityLog),
        typeof(OutboxMessage),
    ];

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is TenantDbContext db)
        {
            CaptureActivityLogs(db);
        }

        return await base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void CaptureActivityLogs(TenantDbContext db)
    {
        // Get current actor — Guid.Empty for system/background job actions
        var actorId = TryGetActorId();
        var tenantId = db.TenantId;

        var entries = db.ChangeTracker.Entries()
            .Where(e => e.Entity is BaseTenantEntity
                        && !ExcludedTypes.Contains(e.Entity.GetType())
                        && e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .ToList();

        foreach (var entry in entries)
        {
            var entity = (BaseTenantEntity)entry.Entity;
            var entityType = entity.GetType().Name;
            var entityId = entity.Id.ToString();

            var eventType = entry.State switch
            {
                EntityState.Added => "Created",
                EntityState.Modified => "Updated",
                EntityState.Deleted => "Deleted",
                _ => "Updated"
            };

            // Resolve the LeadId for this entity — for Lead itself, use its own Id
            var leadId = ResolveLeadId(entity);
            if (leadId == Guid.Empty) continue; // Skip entities not associated with a lead

            Dictionary<string, object?>? oldValues = null;
            Dictionary<string, object?>? newValues = null;

            if (entry.State == EntityState.Modified)
            {
                oldValues = entry.Properties
                    .Where(p => p.IsModified)
                    .ToDictionary(
                        p => p.Metadata.Name,
                        p => p.OriginalValue as object);
                newValues = entry.Properties
                    .Where(p => p.IsModified)
                    .ToDictionary(
                        p => p.Metadata.Name,
                        p => p.CurrentValue as object);
            }
            else if (entry.State == EntityState.Added)
            {
                newValues = entry.Properties
                    .ToDictionary(
                        p => p.Metadata.Name,
                        p => p.CurrentValue as object);
            }

            var log = ActivityLog.Create(tenantId, leadId, actorId, eventType, entityType, entityId, oldValues, newValues);
            db.ActivityLogs.Add(log);
        }
    }

    private static Guid ResolveLeadId(BaseTenantEntity entity) => entity switch
    {
        Lead lead => lead.Id,
        LeadTask task => task.LeadId,
        Contact => Guid.Empty,        // Contacts not lead-scoped in v1
        Opportunity => Guid.Empty, // Opportunities are contact-scoped in v1
        _ => Guid.Empty
    };

    private Guid TryGetActorId()
    {
        try { return userContext.UserId; }
        catch { return Guid.Empty; } // Background jobs have no HTTP context
    }
}
