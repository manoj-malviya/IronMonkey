using IronMonkey.Data.Abstractions;

namespace IronMonkey.Data.Entities;

/// <summary>
/// Records every change event that occurs on entities within a tenant's database.
/// Populated automatically by ActivityChangeInterceptor — no manual endpoint instrumentation needed.
/// Stores old/new field values as JSONB for flexible change payload serialization.
/// </summary>
public sealed class ActivityLog : BaseTenantEntity
{
    private ActivityLog() { }

    /// <summary>The lead this activity is associated with.</summary>
    public Guid LeadId { get; private set; }

    /// <summary>The user who performed the action. May be Guid.Empty for system-generated events.</summary>
    public Guid ActorId { get; private set; }

    /// <summary>Discriminator for the type of event. E.g. "Created", "Updated", "Deleted", "StageMoved", "Note".</summary>
    public string EventType { get; private set; } = string.Empty;

    /// <summary>The entity type that changed. E.g. "Lead", "LeadTask", "Contact", "Opportunity".</summary>
    public string EntityType { get; private set; } = string.Empty;

    /// <summary>
    /// String representation of the entity's Id. Stored as string so it works for
    /// both Guid-keyed entities and other key types without FK constraints.
    /// </summary>
    public string EntityId { get; private set; } = string.Empty;

    /// <summary>Original property values before the change. Null for Created events.</summary>
    public Dictionary<string, object?>? OldValues { get; private set; }

    /// <summary>New property values after the change. Null for Deleted events.</summary>
    public Dictionary<string, object?>? NewValues { get; private set; }

    // Navigation properties
    public User? Actor { get; private set; }
    public Lead Lead { get; private set; } = null!;

    public static ActivityLog Create(
        Guid tenantId,
        Guid leadId,
        Guid actorId,
        string eventType,
        string entityType,
        string entityId,
        Dictionary<string, object?>? oldValues = null,
        Dictionary<string, object?>? newValues = null)
        => new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            LeadId = leadId,
            ActorId = actorId,
            EventType = eventType,
            EntityType = entityType,
            EntityId = entityId,
            OldValues = oldValues,
            NewValues = newValues
        };
}
