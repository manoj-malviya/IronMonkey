using IronMonkey.Data.Abstractions;

namespace IronMonkey.Data.Entities;

/// <summary>
/// The events a team-administration audit trail has to answer for.
///
/// Values are explicit so a rename in code never reinterprets rows already written.
/// </summary>
public static class UserAuditEvent
{
    public const string Invited = "Invited";
    public const string InvitationResent = "InvitationResent";
    public const string InvitationRevoked = "InvitationRevoked";
    public const string InvitationAccepted = "InvitationAccepted";
    public const string UserCreated = "UserCreated";
    public const string RoleChanged = "RoleChanged";
    public const string Deactivated = "Deactivated";
    public const string Reactivated = "Reactivated";
    public const string PasswordReset = "PasswordReset";
    public const string ProfileUpdated = "ProfileUpdated";
}

/// <summary>
/// Audit trail for team administration: who changed whose role, who was deactivated, who
/// invited whom.
///
/// Separate from <see cref="ActivityLog"/> on purpose. ActivityLog is a CRM *timeline*: every
/// row needs a SubjectType of Lead/Contact/Opportunity and carries an FK to Lead, and it is
/// written automatically by the change interceptor for records on those timelines. A role
/// change has no lead, belongs on no customer timeline, and would have to be smuggled in
/// under a fake subject to fit. Keeping it apart also means the team audit can be read by
/// someone who may not read lead activity.
///
/// Tenant-scoped like everything else in this database, so one tenant's administration is
/// never visible to another.
/// </summary>
public sealed class UserAuditLog : BaseTenantEntity
{
    private UserAuditLog() { }

    /// <summary>One of <see cref="UserAuditEvent"/>.</summary>
    public string EventType { get; private set; } = string.Empty;

    /// <summary>The user the event was performed on, when there is one.</summary>
    public Guid? TargetUserId { get; private set; }

    /// <summary>The invitation the event concerns, when there is one.</summary>
    public Guid? InvitationId { get; private set; }

    /// <summary>Email of the subject, kept so the row stays readable after a rename.</summary>
    public string TargetEmail { get; private set; } = string.Empty;

    /// <summary>
    /// Who did it. Null for an unauthenticated actor — acceptance is performed by the
    /// invitee before they have a session, so there is no acting user id to record.
    /// </summary>
    public Guid? ActorUserId { get; private set; }

    /// <summary>Short human-readable detail, e.g. "Owner -> Admin". Never a secret.</summary>
    public string? Detail { get; private set; }

    public DateTime OccurredAt { get; private set; }

    public static UserAuditLog Record(
        Guid tenantId,
        string eventType,
        string targetEmail,
        DateTime nowUtc,
        Guid? targetUserId = null,
        Guid? invitationId = null,
        Guid? actorUserId = null,
        string? detail = null)
        => new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EventType = eventType,
            TargetUserId = targetUserId,
            InvitationId = invitationId,
            TargetEmail = targetEmail,
            // Guid.Empty is "no actor" at the call sites; store null so it reads as absent
            // rather than as a real user who happens to have the empty id.
            ActorUserId = actorUserId == Guid.Empty ? null : actorUserId,
            Detail = detail,
            OccurredAt = nowUtc
        };
}
