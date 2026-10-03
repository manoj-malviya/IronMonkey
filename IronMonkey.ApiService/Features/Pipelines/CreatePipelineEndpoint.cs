using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.Common;
using IronMonkey.Data;
using IronMonkey.Data.Entities;

namespace IronMonkey.ApiService.Features.Pipelines;

/// <summary>
/// Creates a pipeline, with stages, in one transaction.
///
/// A pipeline with no stages accepts no records — it would appear in the picker and then
/// fail every create — so stages are part of creating one, not a follow-up step that can be
/// forgotten. When the caller supplies none, the product's default set for the record type
/// is used, exactly as provisioning does for a recipe that expresses no opinion.
/// </summary>
public class CreatePipelineEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapPost("/api/pipelines", Handle)
        .WithSummary("Create a pipeline and its stages for the authenticated tenant")
        .WithTags("Pipelines")
        .RequireAuthorization();

    public record StageInput(string Name, string StageType = "Active");

    /// <param name="RecordType">"Lead" or "Opportunity". Defaults to Lead.</param>
    /// <param name="Stages">
    /// The pipeline's stages in order. Omitted or empty seeds the product defaults for the
    /// record type rather than creating an unusable pipeline.
    /// </param>
    /// <param name="MakeDefault">
    /// Promotes the new pipeline to the tenant's default for its record type, demoting the
    /// previous one in the same transaction. Off by default: creating a second pipeline must
    /// not silently redirect every existing "no pipeline specified" caller to it.
    /// </param>
    public record Request(
        string Name,
        string? RecordType = null,
        string? Description = null,
        List<StageInput>? Stages = null,
        bool MakeDefault = false);

    public record Response(Guid Id, string Name, string RecordType, bool IsDefault, int StageCount);

    internal static async Task<Results<Created<Response>, BadRequest<string>, Conflict<string>>> Handle(
        Request request,
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        CancellationToken cancellationToken)
    {
        var name = request.Name?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(name) || name.Length > 100)
            return TypedResults.BadRequest("Pipeline name must be between 1 and 100 characters.");

        if (!PipelineRecordTypes.TryParse(request.RecordType, out var recordType))
            return TypedResults.BadRequest(PipelineRecordTypes.ParseError);

        var stageInputs = request.Stages is { Count: > 0 }
            ? request.Stages
            : DefaultStagesFor(recordType);

        if (stageInputs.Count > 100)
            return TypedResults.BadRequest("A pipeline cannot have more than 100 stages.");

        foreach (var stage in stageInputs)
        {
            if (string.IsNullOrWhiteSpace(stage.Name) || stage.Name.Trim().Length > 100)
                return TypedResults.BadRequest("Every stage name must be between 1 and 100 characters.");

            if (!Enum.TryParse<StageType>(stage.StageType, ignoreCase: true, out _))
                return TypedResults.BadRequest("StageType must be one of: Entry, Active, ClosedWon, ClosedLost.");
        }

        // Caught here rather than left to the unique index, which is per pipeline: two
        // stages of the same name inside one pipeline are indistinguishable on its board.
        var duplicateStage = stageInputs
            .GroupBy(s => s.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(g => g.Count() > 1);

        if (duplicateStage is not null)
            return TypedResults.BadRequest($"Stage '{duplicateStage.Key}' appears more than once.");

        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);
        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        var nameExists = await db.Pipelines
            .AnyAsync(p => p.RecordType == recordType && p.Name.ToLower() == name.ToLower(), cancellationToken);

        if (nameExists)
            return TypedResults.Conflict($"A pipeline named '{name}' already exists for this record type.");

        var maxOrder = await db.Pipelines
            .Where(p => p.RecordType == recordType)
            .Select(p => (int?)p.Order)
            .MaxAsync(cancellationToken) ?? 0;

        // Whether this tenant has any pipeline of this type yet. The very first one is the
        // default whatever the caller asked — a record type with no default pipeline has no
        // answer for "no pipeline specified", which every existing caller relies on.
        var hasAny = await db.Pipelines.AnyAsync(p => p.RecordType == recordType, cancellationToken);
        var makeDefault = request.MakeDefault || !hasAny;

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        if (makeDefault && hasAny)
        {
            // Demoted in the same transaction as the promotion. The filtered unique index
            // rejects two defaults, so doing these in either order across two saves would
            // fail — and leaving zero defaults is worse than failing.
            var previous = await db.Pipelines
                .Where(p => p.RecordType == recordType && p.IsDefault)
                .ToListAsync(cancellationToken);

            foreach (var p in previous) p.SetDefault(false);
        }

        var pipeline = Pipeline.Create(
            tenantId, recordType, name, maxOrder + 1, makeDefault, request.Description?.Trim());

        db.Pipelines.Add(pipeline);

        var order = 1;
        foreach (var stage in stageInputs)
        {
            var stageType = Enum.TryParse<StageType>(stage.StageType, ignoreCase: true, out var parsed)
                ? parsed
                : StageType.Active;

            db.PipelineStages.Add(PipelineStage.CreateIn(pipeline, stage.Name.Trim(), order++, stageType));
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return TypedResults.Created(
            $"/api/pipelines/{pipeline.Id}",
            new Response(pipeline.Id, pipeline.Name, pipeline.RecordType.ToString(),
                pipeline.IsDefault, stageInputs.Count));
    }

    /// <summary>
    /// The product's default stage set for a record type, used when a caller creates a
    /// pipeline without naming stages. Opportunity reuses the same seed list provisioning
    /// and the migration use, so the three cannot drift.
    /// </summary>
    internal static List<StageInput> DefaultStagesFor(PipelineRecordType recordType) =>
        recordType == PipelineRecordType.Opportunity
            ? [.. OpportunityStages.Defaults.Select(d => new StageInput(d.Name, d.StageType))]
            :
            [
                new("New", nameof(StageType.Entry)),
                new("Contacted", nameof(StageType.Active)),
                new("Qualified", nameof(StageType.Active)),
                new("Converted", nameof(StageType.ClosedWon)),
                new("Closed Lost", nameof(StageType.ClosedLost))
            ];
}
