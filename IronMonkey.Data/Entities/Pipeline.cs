using IronMonkey.Data.Abstractions;

namespace IronMonkey.Data.Entities;

/// <summary>
/// A named, ordered pipeline owning a set of <see cref="PipelineStage"/> rows for one
/// <see cref="PipelineRecordType"/> within one tenant.
///
/// A dealership runs new-car sales alongside service bookings; a university runs
/// undergraduate admissions alongside executive education. Those need different stages,
/// different fields and different routing inside the same tenant, which one flat stage list
/// per record type cannot express.
///
/// Scoped to a record type as well as to a tenant, because <see cref="PipelineStage"/> is:
/// a lead pipeline and an opportunity pipeline are different shapes of thing and a stage
/// belongs to exactly one of each. Pairing them would let a lead board offer deal stages.
///
/// <para>
/// <b>Every stage belongs to exactly one pipeline.</b> <c>PipelineStage.PipelineId</c> is
/// non-nullable with a real foreign key, so "a stage belonging to no pipeline" is not a
/// state the database can hold — the migration backfills every existing row into a default
/// pipeline before the constraint is added. That is the guarantee, not a convention.
/// </para>
///
/// <para>
/// <b><see cref="IsDefault"/> is what keeps a single-pipeline tenant's experience
/// unchanged.</b> Exactly one pipeline per (tenant, record type) carries it, and every call
/// site that is not given an explicit pipeline falls back to it. A tenant that never creates
/// a second pipeline therefore never sees a pipeline picker, never has to choose, and every
/// endpoint behaves exactly as it did before this entity existed.
/// </para>
/// </summary>
public sealed class Pipeline : BaseTenantEntity
{
    private Pipeline() { }

    public string Name { get; private set; } = string.Empty;

    /// <summary>Which kind of record runs through this pipeline. Immutable after creation:
    /// changing it would strand every stage and every record already in it.</summary>
    public PipelineRecordType RecordType { get; private set; } = PipelineRecordType.Lead;

    /// <summary>
    /// The pipeline used when a caller names none. Exactly one per (tenant, record type),
    /// enforced by a filtered unique index. It is the reason a single-pipeline tenant is
    /// never asked to choose: absence of a choice resolves here.
    /// </summary>
    public bool IsDefault { get; private set; }

    /// <summary>
    /// An inactive pipeline is hidden from pickers but keeps its stages and its records —
    /// it is retired, not removed. Removing one outright is refused while anything points
    /// at it; see the delete endpoint's impact report.
    /// </summary>
    public bool IsActive { get; private set; } = true;

    /// <summary>Display order in a picker, ascending. Ties break on Name.</summary>
    public int Order { get; private set; }

    /// <summary>Optional one-line explanation shown beside the name in the workspace.</summary>
    public string? Description { get; private set; }

    public static Pipeline Create(
        Guid tenantId,
        PipelineRecordType recordType,
        string name,
        int order = 1,
        bool isDefault = false,
        string? description = null)
        => new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            RecordType = recordType,
            Name = name,
            Order = order,
            IsDefault = isDefault,
            Description = description
        };

    public void Rename(string name) => Name = name;

    public void SetOrder(int order) => Order = order;

    public void SetDescription(string? description) => Description = description;

    public void SetActive(bool isActive) => IsActive = isActive;

    /// <summary>
    /// Marks this pipeline as the tenant's default for its record type. The caller must
    /// clear the flag on the previous default in the same transaction — the unique index
    /// rejects two, which is the point: a tenant with no default, or two, would make the
    /// "no explicit pipeline" fallback ambiguous and the single-pipeline UX impossible.
    /// </summary>
    public void SetDefault(bool isDefault) => IsDefault = isDefault;
}
