using IronMonkey.Data;
using IronMonkey.Data.Entities;

namespace IronMonkey.ApiService.Features.RoleManagement;

/// <summary>
/// Writes privilege changes to the activity trail: who changed which role, team or user
/// assignment, from what to what, and when. These are exactly the rows an auditor asks for.
///
/// <para>Recorded explicitly rather than through the interceptor: role/permission rows are a
/// many-to-many join table and int-keyed entities the interceptor does not observe, and a
/// privilege change deserves a deliberate, readable entry rather than a diff of join rows.</para>
/// </summary>
public static class PermissionAudit
{
    public const string RoleSubject = "Role";
    public const string TeamSubject = "Team";
    public const string UserSubject = "User";

    /// <summary>A role is int-keyed; its timeline subject id is that int widened into a Guid.</summary>
    public static Guid RoleSubjectId(int roleId) => new(roleId, 0, 0, new byte[8]);

    public static void Record(TenantDbContext db, Guid tenantId, Guid actorId, string subjectType, Guid subjectId,
        string eventType, string entityId, Dictionary<string, object?>? before, Dictionary<string, object?>? after) =>
        db.ActivityLogs.Add(ActivityLog.CreateFor(tenantId, subjectType, subjectId, actorId, eventType,
            subjectType, entityId, before, after));
}
