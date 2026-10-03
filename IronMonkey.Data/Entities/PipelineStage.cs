using IronMonkey.Data.Abstractions;

namespace IronMonkey.Data.Entities;

public enum StageType { Entry = 0, Active = 1, ClosedWon = 2, ClosedLost = 3 }

/// <summary>
/// Which kind of record a stage belongs to.
///
/// Leads and opportunities run through the same stage machinery — ordering, stage type,
/// active/inactive, the reorder and impact rules — and differ only in which records sit in
/// them. So this is a discriminator on one entity rather than a second, parallel stage type:
/// a divergent opportunity stage model is precisely the failure mode to avoid, and every fix
/// to the lead pipeline's semantics would otherwise have to be made twice.
///
/// Default is <see cref="Lead"/> so every row that existed before this column did keeps
/// meaning exactly what it meant.
/// </summary>
public enum PipelineRecordType { Lead = 0, Opportunity = 1 }

public sealed class PipelineStage : BaseTenantEntity
{
    private PipelineStage() { }

    public string Name { get; private set; } = string.Empty;
    public int Order { get; private set; }
    public bool IsActive { get; private set; } = true;
    public StageType StageType { get; private set; } = StageType.Active;

    /// <summary>
    /// The record type this stage serves. Every query that lists or validates stages must
    /// filter on it — without the predicate an opportunity stage picker offers lead stages
    /// and the two sequences interleave in the same ordering space.
    /// </summary>
    public PipelineRecordType RecordType { get; private set; } = PipelineRecordType.Lead;

    /// <summary>
    /// The pipeline this stage belongs to. Non-nullable by design: a stage that belongs to
    /// no pipeline would appear in no picker and on no board while still holding records,
    /// so the database forbids the state outright. The migration backfills every pre-existing
    /// stage into its tenant's default pipeline before this constraint is added.
    ///
    /// <see cref="RecordType"/> is kept alongside it rather than read through the navigation:
    /// every stage query already filters on it, and duplicating it lets those queries stay a
    /// single-table scan. <see cref="Pipeline.RecordType"/> is the authority and the two are
    /// held consistent by <see cref="CreateIn"/> and by the endpoints.
    /// </summary>
    public Guid PipelineId { get; private set; }

    public Pipeline Pipeline { get; private set; } = null!;

    public bool IsTerminal => StageType is StageType.ClosedWon or StageType.ClosedLost;

    public static PipelineStage Create(Guid tenantId, string name, int order, StageType stageType = StageType.Active)
        => new() { Id = Guid.NewGuid(), TenantId = tenantId, Name = name, Order = order, StageType = stageType };

    /// <summary>
    /// Creates a stage for a specific record type. The <see cref="Create"/> overload above
    /// keeps its original signature — and its Lead default — so the existing lead call sites
    /// and their tests are untouched by this change.
    /// </summary>
    public static PipelineStage CreateFor(
        Guid tenantId,
        PipelineRecordType recordType,
        string name,
        int order,
        StageType stageType = StageType.Active)
        => new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            RecordType = recordType,
            Name = name,
            Order = order,
            StageType = stageType
        };

    /// <summary>
    /// Creates a stage inside a specific pipeline. This is the factory every call site
    /// should use now: it takes the record type from the pipeline itself, so a stage can
    /// never be filed under a record type its pipeline does not serve.
    /// </summary>
    public static PipelineStage CreateIn(
        Pipeline pipeline,
        string name,
        int order,
        StageType stageType = StageType.Active)
        => new()
        {
            Id = Guid.NewGuid(),
            TenantId = pipeline.TenantId,
            PipelineId = pipeline.Id,
            RecordType = pipeline.RecordType,
            Name = name,
            Order = order,
            StageType = stageType
        };

    /// <summary>
    /// Attaches the stage to a pipeline. Used by seeding and by tests that build a stage
    /// through the older factories; moving a stage between pipelines is deliberately NOT
    /// exposed as a product operation, because it would silently relocate every record
    /// sitting in it to a pipeline whose other stages they have never been through.
    /// </summary>
    public void AssignToPipeline(Pipeline pipeline)
    {
        PipelineId = pipeline.Id;
        RecordType = pipeline.RecordType;
    }

    public void Update(string name, int order) { Name = name; Order = order; }

    /// <summary>
    /// Moves the stage to a new position. Separate from <see cref="Update"/> so a bulk
    /// reorder does not have to restate the name it is not changing.
    /// </summary>
    public void SetOrder(int order) => Order = order;

    public void Rename(string name) => Name = name;

    public void Deactivate() => IsActive = false;

    public void Activate() => IsActive = true;

    /// <summary>Sets active state directly, for a form that edits it as a toggle.</summary>
    public void SetActive(bool isActive) => IsActive = isActive;

    public void SetStageType(StageType type) => StageType = type;
}
