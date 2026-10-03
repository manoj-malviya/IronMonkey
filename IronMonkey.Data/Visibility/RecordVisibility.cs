namespace IronMonkey.Data.Visibility;

/// <summary>How much of a record type a role may see.</summary>
public enum VisibilityScope
{
    /// <summary>Records the user owns (assigned to them).</summary>
    Own = 0,

    /// <summary>Own records plus those owned by teammates, and by everyone in teams the user
    /// manages — directly or through sub-teams, to <see cref="TeamHierarchy.MaxDepth"/>.</summary>
    Team = 1,

    /// <summary>Every record in the tenant. The default, so no tenant loses access on upgrade.</summary>
    All = 2
}

/// <summary>Record types that carry an owner and are scoped by visibility.</summary>
public enum VisibilityRecordType
{
    Lead = 0,
    Contact = 1,
    Opportunity = 2,
    Task = 3
}

/// <summary>
/// What the current caller may see, per record type: everything, or only records owned by a
/// fixed set of users. Resolved once per request and captured by every
/// <see cref="TenantDbContext"/> created in that request, where it becomes part of the global
/// query filter beside <c>TenantId</c>.
///
/// <para><b>Why ambient.</b> There are well over a hundred <c>CreateForTenant</c> call sites.
/// Threading the caller's visibility through each would make enforcement a convention every
/// call site has to remember; capturing it at context construction makes it a property of
/// querying, which no endpoint can skip — lists, counts, detail lookups, dashboard aggregates
/// and duplicate checks all inherit it. A request that resolves nothing (anonymous public
/// pages, background jobs, provisioning) gets <see cref="Unrestricted"/>: those paths act for
/// the system, not for a user, and routing must be able to assign to anyone.</para>
///
/// <para>An unowned record is visible only under <see cref="VisibilityScope.All"/>: with no
/// owner there is no one for an Own or Team scope to match.</para>
/// </summary>
public sealed class RecordVisibility
{
    private static readonly AsyncLocal<RecordVisibility?> Ambient = new();

    private readonly Dictionary<VisibilityRecordType, Guid[]?> _owners;

    private RecordVisibility(Dictionary<VisibilityRecordType, Guid[]?> owners) => _owners = owners;

    /// <summary>Sees everything. Used for system work and for every unresolved caller.</summary>
    public static RecordVisibility Unrestricted { get; } = new(new Dictionary<VisibilityRecordType, Guid[]?>());

    /// <summary>The visibility of the current request, or <see cref="Unrestricted"/>.</summary>
    public static RecordVisibility Current => Ambient.Value ?? Unrestricted;

    /// <summary>
    /// Builds a visibility from per-type owner sets. A type absent from the map, or mapped to
    /// null, is unrestricted.
    /// </summary>
    public static RecordVisibility Create(IReadOnlyDictionary<VisibilityRecordType, IReadOnlyCollection<Guid>?> owners) =>
        new(owners.ToDictionary(kv => kv.Key, kv => kv.Value?.Distinct().ToArray()));

    /// <summary>
    /// Makes <paramref name="visibility"/> the ambient visibility until the returned scope is
    /// disposed. Flows across awaits within the request, and not into Hangfire jobs it enqueues.
    /// </summary>
    public static IDisposable Enter(RecordVisibility visibility)
    {
        var previous = Ambient.Value;
        Ambient.Value = visibility;
        return new Restore(previous);
    }

    public bool IsUnrestricted(VisibilityRecordType type) => !_owners.TryGetValue(type, out var set) || set is null;

    /// <summary>The users whose records are visible, or an empty array when unrestricted.</summary>
    public Guid[] OwnersFor(VisibilityRecordType type) =>
        _owners.TryGetValue(type, out var set) && set is not null ? set : [];

    public bool IsFullyUnrestricted => _owners.Values.All(v => v is null);

    /// <summary>Whether a record owned by <paramref name="ownerId"/> is visible — for code
    /// that holds a record outside a filtered query.</summary>
    public bool CanSee(VisibilityRecordType type, Guid? ownerId) =>
        IsUnrestricted(type) || (ownerId is { } id && OwnersFor(type).Contains(id));

    private sealed class Restore(RecordVisibility? previous) : IDisposable
    {
        public void Dispose() => Ambient.Value = previous;
    }
}
