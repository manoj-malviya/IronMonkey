using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
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
/// <param name="serviceProvider">
/// The actor is resolved per save rather than injected, because this interceptor is held by
/// the singleton TenantDbContextFactory and IUserContext is scoped. Resolving it eagerly
/// would be a captive dependency that always reported the first request's user.
/// </param>
public class ActivityChangeInterceptor(IServiceProvider serviceProvider) : SaveChangesInterceptor
{
    // Entity types to exclude from activity logging to prevent noise/recursion
    private static readonly HashSet<Type> ExcludedTypes =
    [
        typeof(ActivityLog),
        typeof(OutboxMessage),
    ];

    // Audit columns move on every write and carry no meaning on a timeline.
    private static readonly HashSet<string> IgnoredProperties =
    [
        nameof(BaseTenantEntity.CreatedAt),
        nameof(BaseTenantEntity.UpdatedAt),
        nameof(BaseTenantEntity.TenantId),
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

            // Resolve which timeline this change belongs on.
            var subject = ResolveSubject(entity);
            if (subject is null) continue; // Not shown on any timeline

            Dictionary<string, object?>? oldValues = null;
            Dictionary<string, object?>? newValues = null;

            if (entry.State == EntityState.Modified)
            {
                var changed = entry.Properties
                    .Where(p => p.IsModified && !IgnoredProperties.Contains(p.Metadata.Name))
                    .ToList();

                oldValues = changed.ToDictionary(p => p.Metadata.Name, p => Describe(p.OriginalValue));
                newValues = changed.ToDictionary(p => p.Metadata.Name, p => Describe(p.CurrentValue));
            }
            else if (entry.State == EntityState.Added)
            {
                newValues = entry.Properties
                    .Where(p => !IgnoredProperties.Contains(p.Metadata.Name))
                    .ToDictionary(p => p.Metadata.Name, p => Describe(p.CurrentValue));
            }

            // A "modified" entry whose only changes are bookkeeping is noise on a timeline.
            if (entry.State == EntityState.Modified && newValues is { Count: 0 })
                continue;

            var log = ActivityLog.CreateFor(
                tenantId, subject.Value.Type, subject.Value.Id, actorId,
                eventType, entityType, entityId, oldValues, newValues);

            // TenantDbContext stamps timestamps before calling base.SaveChangesAsync, and
            // this interceptor runs inside that call — so these rows miss that sweep and
            // would persist with CreatedAt = 0001-01-01, breaking timeline ordering.
            log.CreatedAt = log.UpdatedAt = DateTime.UtcNow;

            db.ActivityLogs.Add(log);
        }
    }

    /// <summary>
    /// Maps a changed entity to the timeline it belongs on. A LeadTask shows on its lead's
    /// timeline rather than getting one of its own, which is why this is distinct from the
    /// entity's own type.
    /// </summary>
    private static (string Type, Guid Id)? ResolveSubject(BaseTenantEntity entity) => entity switch
    {
        Lead lead => ("Lead", lead.Id),
        LeadTask task => ("Lead", task.LeadId),
        Contact contact => ("Contact", contact.Id),
        Opportunity opportunity => ("Opportunity", opportunity.Id),
        _ => null
    };

    /// <summary>
    /// Values land in a jsonb column. Complex types (the CustomFieldValues bag, enums)
    /// serialize unpredictably, so reduce anything non-primitive to its string form.
    /// </summary>
    private static object? Describe(object? value) => value switch
    {
        null => null,
        string or bool or int or long or decimal or double or Guid or DateTime or DateTimeOffset => value,
        Enum e => e.ToString(),
        _ => value.ToString()
    };

    private Guid TryGetActorId()
    {
        try
        {
            // IHttpContextAccessor flows the ambient request, so a scope created here still
            // sees the current caller. Background jobs have no request and fall through to
            // Guid.Empty, which is what a "system" actor means on the timeline.
            using var scope = serviceProvider.CreateScope();
            return scope.ServiceProvider.GetRequiredService<IUserContext>().UserId;
        }
        catch { return Guid.Empty; }
    }
}
