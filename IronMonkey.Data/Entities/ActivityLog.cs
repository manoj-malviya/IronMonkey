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

    /// <summary>
    /// The lead this activity is associated with, when there is one. Null for activity on
    /// records that are not lead-scoped (a contact or opportunity created directly).
    /// </summary>
    public Guid? LeadId { get; private set; }

    /// <summary>
    /// The record this activity belongs to on a timeline — "Lead", "Contact" or
    /// "Opportunity". Distinct from EntityType, which is the thing that actually changed:
    /// a LeadTask edit has EntityType "LeadTask" but SubjectType "Lead", so it shows on
    /// the lead's timeline.
    /// </summary>
    public string SubjectType { get; private set; } = string.Empty;

    /// <summary>Id of the record named by SubjectType. No FK, so it spans entity types.</summary>
    public Guid SubjectId { get; private set; }

    /// <summary>
    /// The user who performed the action, or null for system/background-job events.
    /// </summary>
    public Guid? ActorId { get; private set; }

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
    public Lead? Lead { get; private set; }

    public static ActivityLog Create(
        Guid tenantId,
        Guid leadId,
        Guid actorId,
        string eventType,
        string entityType,
        string entityId,
        Dictionary<string, object?>? oldValues = null,
        Dictionary<string, object?>? newValues = null)
        => CreateFor(
            tenantId,
            subjectType: "Lead",
            subjectId: leadId,
            actorId: actorId,
            eventType: eventType,
            entityType: entityType,
            entityId: entityId,
            oldValues: oldValues,
            newValues: newValues);

    /// <summary>
    /// Records activity against any timeline subject. LeadId is populated only for lead
    /// subjects, so the existing FK and lead-scoped queries keep working unchanged.
    /// </summary>
    public static ActivityLog CreateFor(
        Guid tenantId,
        string subjectType,
        Guid subjectId,
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
            SubjectType = subjectType,
            SubjectId = subjectId,
            LeadId = subjectType == "Lead" ? subjectId : null,
            // Guid.Empty means "no user" — store it as null so the optional FK resolves.
            ActorId = actorId == Guid.Empty ? null : actorId,
            EventType = eventType,
            EntityType = entityType,
            EntityId = entityId,
            OldValues = oldValues,
            NewValues = newValues
        };
}
