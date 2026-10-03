using IronMonkey.Data.Abstractions;

namespace IronMonkey.Data.Entities;

/// <summary>
/// A group of users inside a tenant — a branch, a desk, an admissions office.
///
/// <para>Teams nest through <see cref="ParentTeamId"/>, and that tree is the hierarchy: the
/// manager of a team sees the records of its members and of every team beneath it, down to
/// <see cref="Visibility.TeamHierarchy.MaxDepth"/> levels. Cycles are rejected when a parent
/// is set, never discovered when a query walks the tree.</para>
/// </summary>
public sealed class Team : BaseTenantEntity
{
    private Team() { }

    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }

    /// <summary>The user who manages the team. Need not be a member.</summary>
    public Guid? ManagerUserId { get; private set; }

    public Guid? ParentTeamId { get; private set; }

    private readonly List<TeamMembership> _members = [];
    public IReadOnlyCollection<TeamMembership> Members => _members;

    public static Team Create(Guid tenantId, string name, string? description, Guid? managerUserId) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        Name = name.Trim(),
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
        ManagerUserId = managerUserId
    };

    public void Update(string name, string? description, Guid? managerUserId)
    {
        Name = name.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        ManagerUserId = managerUserId;
    }

    /// <summary>Sets the parent. The caller validates with <see cref="Visibility.TeamHierarchy"/> first.</summary>
    public void SetParent(Guid? parentTeamId) => ParentTeamId = parentTeamId;

    public void SetMembers(IEnumerable<Guid> userIds)
    {
        var wanted = userIds.Distinct().ToHashSet();
        _members.RemoveAll(m => !wanted.Contains(m.UserId));
        foreach (var id in wanted.Where(id => _members.All(m => m.UserId != id)))
            _members.Add(TeamMembership.Create(TenantId, Id, id));
    }
}

public sealed class TeamMembership
{
    private TeamMembership() { }

    public Guid TenantId { get; private set; }
    public Guid TeamId { get; private set; }
    public Guid UserId { get; private set; }

    internal static TeamMembership Create(Guid tenantId, Guid teamId, Guid userId) =>
        new() { TenantId = tenantId, TeamId = teamId, UserId = userId };
}

/// <summary>
/// The visibility a role grants over one record type. No row means <see cref="Visibility.VisibilityScope.All"/>,
/// which is how every role behaved before scopes existed — so an upgrade changes nothing until
/// an Admin narrows a scope.
/// </summary>
public sealed class RoleRecordScope
{
    private RoleRecordScope() { }

    public int RoleId { get; private set; }
    public Visibility.VisibilityRecordType RecordType { get; private set; }
    public Visibility.VisibilityScope Scope { get; private set; }

    public static RoleRecordScope Create(int roleId, Visibility.VisibilityRecordType type, Visibility.VisibilityScope scope) =>
        new() { RoleId = roleId, RecordType = type, Scope = scope };

    public void Set(Visibility.VisibilityScope scope) => Scope = scope;
}
