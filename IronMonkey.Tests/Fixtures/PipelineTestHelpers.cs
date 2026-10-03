using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Features.Pipelines;
using IronMonkey.Data;
using IronMonkey.Data.Entities;

namespace IronMonkey.Tests.Fixtures;

/// <summary>
/// Shared helpers for tests that touch pipeline-aware endpoints.
///
/// The handlers under test take an <see cref="IPipelineScopeResolver"/> and return
/// <c>Results&lt;Ok&lt;T&gt;, BadRequest&lt;string&gt;&gt;</c> rather than a bare
/// <c>Ok&lt;T&gt;</c>, because an unrecognised pipeline id has to be refusable. Both
/// additions are mechanical at the call site, so they live here rather than being repeated
/// in every test file.
/// </summary>
public static class PipelineTestHelpers
{
    /// <summary>
    /// The real resolver, not a mock. It is stateless and its whole job is reading the
    /// pipelines table — substituting a fake would make every scoping test assert against
    /// behaviour that does not ship.
    /// </summary>
    public static IPipelineScopeResolver Scope() => new PipelineScopeResolver();

    /// <summary>
    /// Unwraps a successful result, failing the test with the refusal message if the handler
    /// returned a BadRequest instead. An <c>as Ok&lt;T&gt;</c> that silently yielded null
    /// would surface as an unrelated NullReferenceException several lines later.
    /// </summary>
    public static T Ok<T>(this Results<Ok<T>, BadRequest<string>> result)
    {
        if (result.Result is BadRequest<string> bad)
            throw new Xunit.Sdk.XunitException($"Expected success, got BadRequest: {bad.Value}");

        var ok = result.Result as Ok<T>
            ?? throw new Xunit.Sdk.XunitException($"Expected Ok<{typeof(T).Name}>, got {result.Result?.GetType().Name ?? "null"}.");

        return ok.Value!;
    }

    /// <summary>The refusal message, failing the test if the handler in fact succeeded.</summary>
    public static string BadRequest<T>(this Results<Ok<T>, BadRequest<string>> result)
    {
        var bad = result.Result as BadRequest<string>
            ?? throw new Xunit.Sdk.XunitException(
                $"Expected BadRequest, got {result.Result?.GetType().Name ?? "null"}.");

        return bad.Value!;
    }

    /// <summary>
    /// Creates a pipeline with stages for a tenant, the way provisioning would.
    ///
    /// Tests that need a SECOND pipeline use this; the first one is created automatically by
    /// <c>TenantDbContext.DerivePipelineMembership</c> when the first stage is saved, which is
    /// what keeps the several hundred pre-existing fixtures working untouched.
    /// </summary>
    public static async Task<(Pipeline Pipeline, List<PipelineStage> Stages)> CreatePipelineAsync(
        TenantDbContext db,
        Guid tenantId,
        PipelineRecordType recordType,
        string name,
        (string Name, StageType Type)[] stages,
        bool isDefault = false,
        int order = 2)
    {
        var pipeline = Pipeline.Create(tenantId, recordType, name, order, isDefault);
        db.Pipelines.Add(pipeline);

        var created = new List<PipelineStage>();
        for (var i = 0; i < stages.Length; i++)
        {
            var stage = PipelineStage.CreateIn(pipeline, stages[i].Name, i + 1, stages[i].Type);
            db.PipelineStages.Add(stage);
            created.Add(stage);
        }

        await db.SaveChangesAsync();
        return (pipeline, created);
    }

    /// <summary>The tenant's default pipeline for a record type — the one every unscoped
    /// caller resolves to.</summary>
    public static Task<Pipeline> DefaultPipelineAsync(TenantDbContext db, PipelineRecordType recordType) =>
        db.Pipelines
            .Where(p => p.RecordType == recordType)
            .OrderByDescending(p => p.IsDefault)
            .ThenBy(p => p.Order)
            .FirstAsync();
}
