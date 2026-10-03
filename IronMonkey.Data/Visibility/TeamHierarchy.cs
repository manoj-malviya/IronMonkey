namespace IronMonkey.Data.Visibility;

/// <summary>
/// Rules for the team tree, kept pure so they are tested without a database.
///
/// The tree is walked when visibility is resolved. A cycle there would loop forever, and an
/// unbounded depth would make every request's resolution cost grow with the org chart — so
/// both are rejected at write time and the walk itself is also capped, as a backstop for any
/// row that reached the table another way.
/// </summary>
public static class TeamHierarchy
{
    /// <summary>Maximum levels below a team that its manager's visibility reaches, and the
    /// maximum depth of the tree itself.</summary>
    public const int MaxDepth = 5;

    /// <summary>
    /// Validates making <paramref name="parentId"/> the parent of <paramref name="teamId"/>.
    /// Returns an error, or null when the change is allowed.
    /// </summary>
    /// <param name="parents">Every team's current parent (team id → parent id).</param>
    public static string? ValidateParent(Guid teamId, Guid? parentId, IReadOnlyDictionary<Guid, Guid?> parents)
    {
        if (parentId is null) return null;
        if (parentId == teamId) return "A team cannot be its own parent.";
        if (!parents.ContainsKey(parentId.Value)) return "The parent team does not exist.";

        // Walk up from the proposed parent: meeting the team itself means a cycle.
        var depthAbove = 1;
        var cursor = parentId;
        var seen = new HashSet<Guid>();
        while (cursor is { } id && parents.TryGetValue(id, out var next))
        {
            if (id == teamId) return "That parent would create a cycle in the team hierarchy.";
            if (!seen.Add(id)) return "The team hierarchy already contains a cycle.";
            cursor = next;
            if (cursor is not null) depthAbove++;
        }

        // Depth of the subtree hanging from the team being moved.
        var children = parents.Where(kv => kv.Value is not null).ToLookup(kv => kv.Value!.Value, kv => kv.Key);
        var depthBelow = SubtreeDepth(teamId, children, 0);

        if (depthAbove + 1 + depthBelow > MaxDepth)
            return $"Team hierarchies may be at most {MaxDepth} levels deep.";

        return null;
    }

    /// <summary>
    /// Every team at or below <paramref name="roots"/>, at most <see cref="MaxDepth"/> levels down.
    /// Cycle-safe: a team already visited is not revisited.
    /// </summary>
    public static HashSet<Guid> Descendants(IEnumerable<Guid> roots, IReadOnlyDictionary<Guid, Guid?> parents)
    {
        var children = parents.Where(kv => kv.Value is not null).ToLookup(kv => kv.Value!.Value, kv => kv.Key);
        var result = new HashSet<Guid>();
        var frontier = roots.Where(result.Add).ToList();

        for (var depth = 1; depth < MaxDepth && frontier.Count > 0; depth++)
            frontier = frontier.SelectMany(t => children[t]).Where(result.Add).ToList();

        return result;
    }

    private static int SubtreeDepth(Guid teamId, ILookup<Guid, Guid> children, int guard)
    {
        if (guard > MaxDepth * 2) return guard; // malformed data; treat as too deep
        var kids = children[teamId].ToList();
        return kids.Count == 0 ? 0 : 1 + kids.Max(k => SubtreeDepth(k, children, guard + 1));
    }
}
