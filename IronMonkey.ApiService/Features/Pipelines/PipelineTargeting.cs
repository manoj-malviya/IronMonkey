namespace IronMonkey.ApiService.Features.Pipelines;

/// <summary>
/// Applies the precedence rule shared by everything that may target a pipeline: custom
/// fields, workflow rules and routing configs.
///
/// <para><b>The rule: the more specific target wins, and does not merge.</b></para>
///
/// <para>
/// For a record in pipeline P, the applicable set is the items targeting P if there are any,
/// and otherwise the tenant-wide items (<c>PipelineId == null</c>). The two are never
/// unioned.
/// </para>
///
/// <para>
/// Unioning is the tempting alternative and it is wrong, because it makes a pipeline-scoped
/// item unable to <i>override</i> anything — it could only ever add. A tenant whose second
/// pipeline needs a different routing config, or a narrower version of a rule, would have no
/// way to express it: the tenant-wide one would keep firing alongside. "Scoped to this
/// pipeline" has to mean "instead of", or it means nothing beyond "extra".
/// </para>
///
/// <para>
/// <b>The fallback is what keeps a single-pipeline tenant unchanged.</b> Every row that
/// existed before pipelines carries <c>PipelineId == null</c>, so for any pipeline with no
/// scoped items the applicable set is exactly the tenant-wide set — which is exactly what
/// applied before. A tenant that never scopes anything never sees a behaviour change.
/// </para>
///
/// <para>
/// Custom fields are the one deliberate exception and take <see cref="ResolveAdditive"/>
/// instead; the reason is documented there.
/// </para>
/// </summary>
public static class PipelineTargeting
{
    /// <summary>
    /// The items that apply to a record in <paramref name="pipelineId"/>: the pipeline's own
    /// if it has any, else the tenant-wide ones.
    /// </summary>
    public static List<T> Resolve<T>(
        IEnumerable<T> items, Guid pipelineId, Func<T, Guid?> targetOf)
    {
        var all = items as IList<T> ?? [.. items];

        var scoped = all.Where(i => targetOf(i) == pipelineId).ToList();

        return scoped.Count > 0
            ? scoped
            : [.. all.Where(i => targetOf(i) is null)];
    }

    /// <summary>
    /// The additive variant, used <b>only</b> for custom fields.
    ///
    /// A field is a column of captured data, not a behaviour. Hiding the tenant-wide fields
    /// on a record just because its pipeline added one of its own would make data already
    /// stored on that record unreachable and unedited — the override semantics that are
    /// right for a rule are data loss for a field. So a record's form shows the tenant-wide
    /// fields plus its pipeline's, and a field scoped to a different pipeline is excluded.
    /// </summary>
    public static List<T> ResolveAdditive<T>(
        IEnumerable<T> items, Guid pipelineId, Func<T, Guid?> targetOf) =>
        [.. items.Where(i => targetOf(i) is null || targetOf(i) == pipelineId)];
}
